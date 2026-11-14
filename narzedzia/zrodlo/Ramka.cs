// ECXP – ramka binarna i klient stosu (potok nazwany \\.\pipe\ecxp-<skrót katalogu>).
//
// Nagłówek 72 B, little-endian: 0 magic "ECXP" | 4 wersja | 5 typ | 6 flagi(16) | 8 połączenie(64) | 16 seq(64)
// | 24 ack(64) | 32 okno(32) | 36 długość(32) | 40 SHA-256 bajtów 0–39 i ładunku. Ładunek: JSON UTF-8, najwyżej 1 MiB.
// Połączenie: HELLO(SYN, seq=x) → WITAJ(SYN|ACK, seq=y, ack=x+1) → ACK(seq=x+1, ack=y+1). Potem ZAPYTANIE/ODPOWIEDZ
// z rosnącym seq i skumulowanym ack, DANE wypychane przez stos (klient odpowiada ACK), PING/PONG, FIN, RST.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Ecxp
{
    class Rama
    {
        public byte Typ;
        public ushort Flagi;
        public ulong Polaczenie, Seq, Ack;
        public uint Okno = 16;
        public byte[] Ladunek = new byte[0];

        public Dictionary<string, object> Json()
        {
            if (Ladunek.Length == 0) return new Dictionary<string, object>();
            return P.Js().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(Ladunek)) ?? new Dictionary<string, object>();
        }
        public Rama Z(object json) { Ladunek = new UTF8Encoding(false).GetBytes(P.Js().Serialize(json)); return this; }
    }

    static class Ramka
    {
        public const int Naglowek = 72, MaxLadunek = 1 << 20;
        public const byte HELLO = 1, WITAJ = 2, ZAPYTANIE = 3, ODPOWIEDZ = 4, DANE = 5, ACK = 6, PING = 7, PONG = 8, BLAD = 9, FIN = 10, RST = 11;
        public const ushort F_SYN = 1, F_ACK = 2, F_FIN = 4, F_RST = 8, F_PSH = 16, F_PILNE = 32;
        static readonly byte[] Magic = { (byte)'E', (byte)'C', (byte)'X', (byte)'P' };

        public static string NazwaPotoku(string root) { return "ecxp-" + P.Sha(Path.GetFullPath(root).TrimEnd('\\').ToLowerInvariant()).Substring(0, 12); }

        public static byte[] Koduj(Rama r)
        {
            if (r.Ladunek.Length > MaxLadunek) throw new EcxpException("Ładunek ramki większy niż 1 MiB.");
            var b = new byte[Naglowek + r.Ladunek.Length];
            Array.Copy(Magic, 0, b, 0, 4);
            b[4] = 1; b[5] = r.Typ;
            Array.Copy(BitConverter.GetBytes(r.Flagi), 0, b, 6, 2);
            Array.Copy(BitConverter.GetBytes(r.Polaczenie), 0, b, 8, 8);
            Array.Copy(BitConverter.GetBytes(r.Seq), 0, b, 16, 8);
            Array.Copy(BitConverter.GetBytes(r.Ack), 0, b, 24, 8);
            Array.Copy(BitConverter.GetBytes(r.Okno), 0, b, 32, 4);
            Array.Copy(BitConverter.GetBytes((uint)r.Ladunek.Length), 0, b, 36, 4);
            Array.Copy(r.Ladunek, 0, b, Naglowek, r.Ladunek.Length);
            Array.Copy(Skrot(b, r.Ladunek.Length), 0, b, 40, 32);
            return b;
        }

        static byte[] Skrot(byte[] b, int dl)
        {
            using (var h = SHA256.Create())
            {
                h.TransformBlock(b, 0, 40, null, 0);
                h.TransformFinalBlock(b, Naglowek, dl);
                return h.Hash;
            }
        }

        static void Dokladnie(Stream s, byte[] b, int off, int n)
        {
            while (n > 0)
            {
                int k = s.Read(b, off, n);
                if (k <= 0) throw new EndOfStreamException("Połączenie ECXP zamknięte.");
                off += k; n -= k;
            }
        }

        public static Rama Czytaj(Stream s)
        {
            var h = new byte[Naglowek];
            Dokladnie(s, h, 0, Naglowek);
            if (h[0] != 'E' || h[1] != 'C' || h[2] != 'X' || h[3] != 'P') throw new EcxpException("Ramka bez znacznika ECXP.");
            if (h[4] != 1) throw new EcxpException("Nieobsługiwana wersja ramki: " + h[4]);
            var dl = BitConverter.ToUInt32(h, 36);
            if (dl > MaxLadunek) throw new EcxpException("Ramka za długa: " + dl);
            var b = new byte[Naglowek + dl];
            Array.Copy(h, b, Naglowek);
            Dokladnie(s, b, Naglowek, (int)dl);
            var sk = Skrot(b, (int)dl);
            for (int i = 0; i < 32; i++) if (sk[i] != b[40 + i]) throw new EcxpException("Ramka uszkodzona: skrót SHA-256 się nie zgadza.");
            var r = new Rama
            {
                Typ = h[5], Flagi = BitConverter.ToUInt16(h, 6), Polaczenie = BitConverter.ToUInt64(h, 8), Seq = BitConverter.ToUInt64(h, 16),
                Ack = BitConverter.ToUInt64(h, 24), Okno = BitConverter.ToUInt32(h, 32), Ladunek = new byte[dl]
            };
            Array.Copy(b, Naglowek, r.Ladunek, 0, (int)dl);
            return r;
        }

        public static ulong Isn() { var b = new byte[8]; using (var g = RandomNumberGenerator.Create()) g.GetBytes(b); return BitConverter.ToUInt64(b, 0) >> 16; }
    }

    // Klient stosu: łączy się (w razie potrzeby uruchamia ecxpd.exe), uzgadnia połączenie, wysyła zapytania, odbiera wypychane DANE.
    class Klient : IDisposable
    {
        NamedPipeClientStream pipe;
        readonly object wlock = new object();
        ulong seq, ackOd, polaczenie;
        readonly Dictionary<ulong, Rama> odpowiedzi = new Dictionary<ulong, Rama>();
        readonly object olock = new object();
        Thread czytnik;
        volatile bool zamkniety;
        public event Action<Dictionary<string, object>> Dane;
        public event Action Rozlaczono;
        public string Przyznane = "";

        public bool Polaczony { get { return !zamkniety && pipe != null && pipe.IsConnected; } }

        public static Klient Polacz(string ja, string rodzaj, bool uruchom)
        {
            var nazwa = Ramka.NazwaPotoku(P.Root);
            var k = new Klient();
            var start = DateTime.Now;
            var limitPolaczenia = Konfiguracja.Liczba("polaczenie_sekundy", 45, 1, 300);
            bool uruchomiony = false;
            for (int proba = 1; ; proba++)
            {
                while (true)
                {
                    var p = new NamedPipeClientStream(".", nazwa, PipeDirection.InOut, PipeOptions.Asynchronous);
                    try { p.Connect(400); k.pipe = p; break; }
                    catch (Exception)
                    {
                        p.Dispose();
                        if (!uruchom) throw new EcxpException("Stos ECXP (ecxpd) nie działa.");
                        if (!uruchomiony) { UruchomStos(); uruchomiony = true; }
                        if ((DateTime.Now - start).TotalSeconds > limitPolaczenia) throw new EcxpException("Nie udało się połączyć ze stosem ECXP (ecxpd) w ustawionym czasie (pierwszy start po aktualizacji może opóźnić skaner antywirusowy).");
                    }
                }
                // Uzgodnienie ponawiane jak SYN w TCP: połączenie zerwane w trakcie (np. tuż po starcie stosu) – nowa próba.
                // Odmowa stosu (RST, EcxpException) nie jest ponawiana.
                try { k.Uzgodnij(ja, rodzaj); break; }
                catch (IOException)
                {
                    try { k.pipe.Dispose(); } catch { }
                    if (proba >= 6 || (DateTime.Now - start).TotalSeconds > limitPolaczenia) throw new EcxpException("Stos ECXP zrywa uzgodnienie połączenia (" + proba + " prób).");
                    Thread.Sleep(150 * proba);
                }
            }
            k.czytnik = new Thread(k.Petla) { IsBackground = true, Name = "ecxp-klient" };
            k.czytnik.Start();
            return k;
        }

        public static void UruchomStos()
        {
            var dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var exe = Path.Combine(dir, "ecxpd.exe");
            if (!File.Exists(exe)) throw new EcxpException("Brak programu stosu: " + exe);
            // CreateProcess z bInheritHandles = false: stos nie dziedziczy uchwytów klienta (stdin/stdout adaptera MCP),
            // więc gospodarz MCP widzi koniec adaptera; bez powłoki Windows (ShellExecute dodawał ~26 s sprawdzania reputacji).
            var si = new STARTUPINFO(); si.cb = System.Runtime.InteropServices.Marshal.SizeOf(si);
            PROCESS_INFORMATION pi;
            var cmd = new StringBuilder("\"" + exe + "\" /root \"" + P.Root + "\"");
            if (!CreateProcess(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, 0x08000000 /* CREATE_NO_WINDOW */, IntPtr.Zero, dir, ref si, out pi))
                throw new EcxpException("Nie udało się uruchomić stosu ECXP (błąd " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ").");
            CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        struct STARTUPINFO
        {
            public int cb; public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        void Wyslij(Rama r)
        {
            var b = Ramka.Koduj(r);
            lock (wlock) { pipe.Write(b, 0, b.Length); pipe.Flush(); }
        }

        void Uzgodnij(string ja, string rodzaj)
        {
            var isn = Ramka.Isn();
            Wyslij(new Rama { Typ = Ramka.HELLO, Flagi = Ramka.F_SYN, Seq = isn }.Z(P.D("ja", ja, "rodzaj", rodzaj, "protokol", P.Protokol, "pid", Process.GetCurrentProcess().Id)));
            var w = Ramka.Czytaj(pipe);
            if (w.Typ == Ramka.RST || w.Typ == Ramka.BLAD) throw new EcxpException("Stos odrzucił połączenie: " + P.S(w.Json(), "blad"));
            if (w.Typ != Ramka.WITAJ || (w.Flagi & (Ramka.F_SYN | Ramka.F_ACK)) != (Ramka.F_SYN | Ramka.F_ACK) || w.Ack != isn + 1)
                throw new EcxpException("Błędne uzgodnienie połączenia ECXP.");
            polaczenie = w.Polaczenie;
            ackOd = w.Seq + 1;
            seq = isn + 1;
            Przyznane = P.S(w.Json(), "ja");
            Wyslij(new Rama { Typ = Ramka.ACK, Flagi = Ramka.F_ACK, Polaczenie = polaczenie, Seq = seq, Ack = ackOd });
        }

        void Petla()
        {
            try
            {
                while (!zamkniety)
                {
                    var r = Ramka.Czytaj(pipe);
                    if (r.Seq >= ackOd) ackOd = r.Seq + 1;
                    if (r.Typ == Ramka.ODPOWIEDZ || r.Typ == Ramka.BLAD)
                    {
                        lock (olock) { odpowiedzi[r.Ack] = r; Monitor.PulseAll(olock); }
                    }
                    else if (r.Typ == Ramka.DANE)
                    {
                        Wyslij(new Rama { Typ = Ramka.ACK, Flagi = Ramka.F_ACK, Polaczenie = polaczenie, Seq = seq, Ack = r.Seq + 1 });
                        var h = Dane; if (h != null) try { h(r.Json()); } catch { }
                    }
                    else if (r.Typ == Ramka.PING)
                        Wyslij(new Rama { Typ = Ramka.PONG, Flagi = Ramka.F_ACK, Polaczenie = polaczenie, Seq = seq, Ack = r.Seq + 1 });
                    else if (r.Typ == Ramka.FIN || r.Typ == Ramka.RST) break;
                }
            }
            catch { }
            zamkniety = true;
            lock (olock) Monitor.PulseAll(olock);
            var z = Rozlaczono; if (z != null) try { z(); } catch { }
        }

        // Zapytanie do stosu: op + argumenty → wynik (albo EcxpException z opisem błędu).
        public object Wywolaj(string op, Dictionary<string, object> args, int msTimeout = 60000)
        {
            if (!Polaczony) throw new EcxpException("Brak połączenia ze stosem ECXP.");
            ulong moj;
            lock (wlock) { moj = ++seq; }
            Wyslij(new Rama { Typ = Ramka.ZAPYTANIE, Flagi = Ramka.F_PSH | Ramka.F_ACK, Polaczenie = polaczenie, Seq = moj, Ack = ackOd }.Z(P.D("op", op, "args", args ?? new Dictionary<string, object>())));
            var koniec = msTimeout == Timeout.Infinite ? DateTime.MaxValue : DateTime.Now.AddMilliseconds(msTimeout);
            Rama r;
            lock (olock)
            {
                while (!odpowiedzi.TryGetValue(moj + 1, out r))
                {
                    if (zamkniety) throw new EcxpException("Stos ECXP zamknął połączenie.");
                    var zostalo = msTimeout == Timeout.Infinite ? Timeout.Infinite : (int)(koniec - DateTime.Now).TotalMilliseconds;
                    if (msTimeout != Timeout.Infinite && zostalo <= 0) throw new EcxpException("Stos ECXP nie odpowiedział w czasie.");
                    Monitor.Wait(olock, zostalo);
                }
                odpowiedzi.Remove(moj + 1);
            }
            var j = r.Json();
            if (r.Typ == Ramka.BLAD || !P.B(j, "ok")) throw new EcxpException(P.S(j, "blad").Length > 0 ? P.S(j, "blad") : "błąd stosu");
            object w; j.TryGetValue("wynik", out w);
            return w;
        }

        public void Dispose()
        {
            if (zamkniety) return;
            try { Wyslij(new Rama { Typ = Ramka.FIN, Flagi = Ramka.F_FIN | Ramka.F_ACK, Polaczenie = polaczenie, Seq = ++seq, Ack = ackOd }); } catch { }
            zamkniety = true;
            try { pipe.Dispose(); } catch { }
        }
    }
}
