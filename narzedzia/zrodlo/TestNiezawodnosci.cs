// Test niezawodności i szybkości rdzenia stosu (dane syntetyczne): indeks wiadomości, przyrostowy dziennik i kolejne zmiany.
// Użycie: TestNiezawodnosci.exe <pusty katalog>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Ecxp {
class TestNiezawodnosci {
 static int failures;
 static void Check(bool ok,string name){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;}
 static List<Msg> Do(string kto){return P.Wszystkie(false).Where(m=>m.Do==kto).ToList();}
 static string Blad(Action a){try{a();return null;}catch(EcxpException ex){return ex.Message;}}
 static string Odcisk(Dictionary<string,Stan> s){return string.Join("\n",s.OrderBy(x=>x.Key).Select(x=>x.Key+"|"+x.Value.Do+"|"+x.Value.Dostarczenia+"|"+x.Value.Dostarczona.HasValue+"|"+x.Value.Potwierdzona.HasValue+"|"+x.Value.Wynik+"|"+x.Value.Ponowienia+"|"+x.Value.ZadanieStan));}
 public static int Main(string[] args){
  if(args.Length<1){Console.Error.WriteLine("Użycie: TestNiezawodnosci.exe <pusty katalog>");return 2;}
  P.Root=Path.GetFullPath(args[0]);Directory.CreateDirectory(P.Root);if(Directory.EnumerateFileSystemEntries(P.Root).Any())throw new Exception("Empty fixture required");
  foreach(var d in P.Foldery)Directory.CreateDirectory(Path.Combine(P.Root,d));
  var config=Path.Combine(P.Root,"projekty","test");Directory.CreateDirectory(config);
  File.WriteAllText(Path.Combine(config,"projekt.json"),P.Js().Serialize(P.D("pary",P.D("test",P.D("codex","codex-p","claude","claude-p","wykonawca","codex-p")))));
  Siec.Przypisz("codex-p","test","test","c-session",new List<string>(),"","codex-p");
  Siec.Przypisz("claude-p","test","test","cl-session",new List<string>(),"","claude-p");
  string sp;

  // ---------- indeks wiadomości w pamięci ----------
  var ids=new List<string>();
  for(int i=0;i<30;i++) ids.Add(P.S(Siec.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do="codex-p",Projekt="test",Modul="test",Typ="informacja",Temat="Indeks "+i,Tresc="Treść "+i,BezKopii=true}),"id"));
  P.Wszystkie(true);
  int zDysku=P.OdczytyZDysku;
  var a1=P.Wszystkie(true);var a2=P.Wszystkie(true);
  Check(P.OdczytyZDysku==zDysku && a1.Count==30 && a2.Count==30,"repeated listing reads no message file from disk again");
  a1[0].Watek="zmienione";a1[0].Kontrole.Add("x");a1[0].Dor=new Stan();
  var a3=P.Wszystkie(true).Single(m=>m.Id==a1[0].Id);
  Check(a3.Watek=="" && a3.Kontrole.Count==0 && a3.Dor==null,"callers get their own copy; changes do not leak into the index");
  P.PamiecWylaczona=true;var bezPamieci=P.Wszystkie(true);P.PamiecWylaczona=false;
  Func<List<Msg>,string> odc=l=>string.Join("\n",l.OrderBy(m=>m.Id).Select(m=>m.Id+"|"+m.Katalog+"|"+m.Od+"|"+m.Do+"|"+m.Typ+"|"+m.Temat+"|"+m.Uwagi+"|"+m.Tytul+"|"+m.Logiczne+"|"+m.Seq+"|"+m.Czas.Ticks+"|"+m.WymagaOdp+"|"+m.Blad));
  Check(odc(bezPamieci)==odc(P.Wszystkie(true)),"indexed listing equals a full disk read");
  var m0=P.Znajdz(ids[5]);
  P.Wycofaj(new List<string>{m0.Plik},"test");
  var poPrzeniesieniu=P.Znajdz(ids[5]);
  Check(poPrzeniesieniu!=null && poPrzeniesieniu.Katalog=="archiwum" && !P.Wszystkie(false).Any(m=>m.Id==ids[5]),"moved message is found in its new folder, not the old one");
  P.Wycofaj(new List<string>{P.Znajdz(ids[6]).Plik},"test",P.Kosz);
  Check(P.Znajdz(ids[6])==null && P.ZKosza().Count==1,"deleted message disappears from lookups");
  P.Przywroc(new List<string>{ids[6]+".json"});
  Check(P.Znajdz(ids[6])!=null && P.Znajdz(ids[6]).Katalog=="przeczytane","restored message is found again");
  // plik podmieniony w miejscu (inna treść, inny rozmiar) – indeks nie może podać starej wersji
  var plik=P.Znajdz(ids[7]).Sciezka;
  File.WriteAllText(plik,File.ReadAllText(plik).Replace("Treść 7","Treść siódma, zmieniona na dysku"));
  Check(P.Znajdz(ids[7]).Blad!=null && P.Znajdz(ids[7]).Uwagi.Contains("zmieniona na dysku"),"a file changed on disk is re-read and its tampering detected");

  // ---------- stan doręczeń bez czytania całego dziennika ----------
  P.Odbierz("codex-p",10);
  var s1=P.Stany(out sp);
  P.PamiecWylaczona=true;string sp2;var pelne=P.Stany(out sp2);P.PamiecWylaczona=false;
  Check(Odcisk(s1)==Odcisk(pelne) && sp==sp2 && sp.StartsWith("spójny"),"incremental delivery state equals a full journal read");
  P.Potwierdz("codex-p",ids[10],"obsluzona","test");
  Siec.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do="codex-p",Projekt="test",Modul="test",Typ="informacja",Temat="Po odczycie",Tresc="Nowa po pierwszym odczycie.",BezKopii=true});
  var s2=P.Stany(out sp);
  P.PamiecWylaczona=true;var pelne2=P.Stany(out sp2);P.PamiecWylaczona=false;
  Check(Odcisk(s2)==Odcisk(pelne2) && sp==sp2 && s2[ids[10]].Potwierdzona.HasValue && s2.Count==s1.Count+1,"entries appended after the first read are picked up");
  string wynikPrzed=s2[ids[10]].Wynik;
  s2[ids[10]].Wynik="zmienione przez wywołującego";s2.Remove(ids[11]);
  var s3=P.Stany(out sp);
  Check(s3[ids[10]].Wynik==wynikPrzed && s3.ContainsKey(ids[11]),"callers cannot change the remembered delivery state");
  // wpis zmieniony w miejscu (ta sama długość) – łańcuch skrótów musi to wykryć mimo pamięci
  var dz=Path.Combine(P.Root,"dziennik","dziennik.jsonl");
  var linie=File.ReadAllLines(dz);
  int nr=Array.FindIndex(linie,l=>l.Contains("\"dostarczona\""));
  linie[nr]=linie[nr].Replace("\"proba\":1","\"proba\":7");
  File.WriteAllLines(dz,linie,new System.Text.UTF8Encoding(false));
  File.SetLastWriteTimeUtc(dz,DateTime.UtcNow.AddSeconds(3));
  P.Stany(out sp);
  Check(sp.StartsWith("NARUSZONY"),"an edited journal entry is detected even with the in-memory state");
  Console.WriteLine("RESULT: "+failures+" failed");
  return failures==0?0:1;
 }
}}
