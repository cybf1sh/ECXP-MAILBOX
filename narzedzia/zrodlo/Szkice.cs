using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Ecxp
{
    public sealed class SzkicWiadomosci
    {
        public string Id = "", Do = "", Projekt = "", Modul = "", Typ = "informacja", Temat = "", Tresc = "", OdpNa = "", Blad = "", Watek = "", Zastepuje = "";
        public bool Pilne, Potwierdz = true, Codex = true, Claude = true;
        public int AdresSzybki;
        public DateTime Zmieniony;
        public List<string> Pliki = new List<string>();
        public List<Dictionary<string, object>> Czesci = new List<Dictionary<string, object>>();
        [System.Web.Script.Serialization.ScriptIgnore]
        public bool MaTresc { get { return !string.IsNullOrWhiteSpace(Tresc) || Pliki.Count > 0 || Czesci.Count > 0; } }
    }

    // Lokalny magazyn tekstu operatora. Nigdy sam nie wysyła wiadomości ani nie zmienia stanu ECXP.
    sealed class MagazynSzkicow
    {
        readonly string plik;
        Dictionary<string, SzkicWiadomosci> szkice = new Dictionary<string, SzkicWiadomosci>();
        static System.Web.Script.Serialization.JavaScriptSerializer Js() { var js=P.Js(); js.MaxJsonLength=int.MaxValue; return js; }
        public MagazynSzkicow(string sciezka)
        {
            plik=Path.GetFullPath(sciezka);
            if (!File.Exists(plik)) return;
            try
            {
                var dane=Js().Deserialize<List<SzkicWiadomosci>>(File.ReadAllText(plik,Encoding.UTF8));
                foreach(var d in dane ?? new List<SzkicWiadomosci>())
                {
                    if (d == null || string.IsNullOrWhiteSpace(d.Id) || szkice.ContainsKey(d.Id)) throw new InvalidDataException("Niepoprawny magazyn szkiców.");
                    if (d.Pliki == null) d.Pliki=new List<string>(); if(d.Czesci==null)d.Czesci=new List<Dictionary<string,object>>();
                    szkice[d.Id]=d;
                }
            }
            catch(Exception ex)
            {
                // Zachowaj oryginał do odzyskania; nie nadpisuj jedynej kopii uszkodzonych szkiców.
                var kopia=plik+".uszkodzony-"+DateTime.Now.ToString("yyyyMMddHHmmssfff")+".bak";
                File.Copy(plik,kopia,false); szkice.Clear(); Bledy.Wyjatek(ex);
            }
        }
        static SzkicWiadomosci Kopia(SzkicWiadomosci d) { return Js().Deserialize<SzkicWiadomosci>(Js().Serialize(d)); }
        public List<SzkicWiadomosci> Lista { get { return szkice.Values.OrderByDescending(x=>x.Zmieniony).Select(Kopia).ToList(); } }
        public SzkicWiadomosci Znajdz(string id) { SzkicWiadomosci d; return szkice.TryGetValue(id,out d)?Kopia(d):null; }
        void ZapiszPlik(Dictionary<string,SzkicWiadomosci> nowe)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(plik));
            string tmp=plik+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(tmp,Js().Serialize(nowe.Values.ToList()),new UTF8Encoding(false));
                if(File.Exists(plik))File.Replace(tmp,plik,null);else File.Move(tmp,plik);
            }
            finally { if(File.Exists(tmp))File.Delete(tmp); }
            szkice=nowe;
        }
        public bool Zapisz(SzkicWiadomosci d)
        {
            if(string.IsNullOrWhiteSpace(d.Id))throw new ArgumentException("Brak identyfikatora szkicu.");
            if(!d.MaTresc)return Usun(d.Id);
            SzkicWiadomosci stary; szkice.TryGetValue(d.Id,out stary);
            d.Zmieniony=stary!=null?stary.Zmieniony:DateTime.MinValue;
            if(stary!=null && Js().Serialize(stary)==Js().Serialize(d))return false;
            d.Zmieniony=DateTime.Now;
            var nowe=new Dictionary<string,SzkicWiadomosci>(szkice);nowe[d.Id]=Kopia(d);ZapiszPlik(nowe);return true;
        }
        public bool Usun(string id)
        {
            if(!szkice.ContainsKey(id))return false;
            var nowe=new Dictionary<string,SzkicWiadomosci>(szkice);nowe.Remove(id);ZapiszPlik(nowe);return true;
        }
        public static SzkicWiadomosci ZFormularza(PiszForm f)
        {
            return new SzkicWiadomosci { Id=f.SzkicId,Do=f.AdresatDoWyslania,Projekt=f.cbProjekt.Text,Modul=f.cbModul.Text,Typ=f.Typ,
                Temat=f.tbTemat.Text,Tresc=f.tbTresc.Text,OdpNa=f.OdpNa,Pilne=f.chPilne.Checked,Potwierdz=f.chPotw.Checked,
                Codex=f.chCodex.Checked,Claude=f.chClaude.Checked,Pliki=f.zal.Pliki.ToList(),Czesci=f.Czesci.ToList(),
                Blad=f.SzkicBlad,Watek=f.SzkicWatek,AdresSzybki=f.SzkicAdres,Zastepuje=f.SzkicZastepuje };
        }
        public static void DoFormularza(PiszForm f,SzkicWiadomosci d)
        {
            f.SzkicId=d.Id;f.SzkicBlad=d.Blad;f.SzkicWatek=d.Watek;f.SzkicAdres=d.AdresSzybki;f.SzkicZastepuje=d.Zastepuje;
            f.UstawAdresata(d.Do);f.cbProjekt.Text=d.Projekt;f.cbModul.Text=d.Modul;f.UstawTyp(d.Typ);
            f.tbTemat.Text=d.Temat;f.tbTresc.Text=d.Tresc;f.OdpNa=d.OdpNa;f.chPilne.Checked=d.Pilne;f.chPotw.Checked=d.Potwierdz;
            f.chCodex.Checked=d.Codex;f.chClaude.Checked=d.Claude;f.zal.UstawPliki(d.Pliki);f.UstawCzesci(d.Czesci);
        }
    }
}
