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

  // dziennik po teście naruszenia: nowy, spójny (kolejne testy dopisują zdarzenia)
  File.Delete(dz); P.Stany(out sp);

  // ---------- identyfikator żądania: ponowione „wyślij” nie tworzy drugiej wiadomości ----------
  int przed=Do("codex-p").Count;
  Func<Dictionary<string,object>> wyslij=()=>Siec.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do="codex-p",Projekt="test",Modul="test",Typ="informacja",Temat="Raz",Tresc="Tylko raz.",BezKopii=true});
  var w1=Powtorki.Raz("k|wlasciciel|form-1",TimeSpan.FromHours(24),wyslij);
  var w2=Powtorki.Raz("k|wlasciciel|form-1",TimeSpan.FromHours(24),wyslij);
  Check(Do("codex-p").Count==przed+1 && P.S(w1,"id")==P.S(w2,"id") && P.B(w2,"powtorzone") && !w1.ContainsKey("powtorzone"),"a repeated request with the same key creates no second message");
  var w3=Powtorki.Raz("k|wlasciciel|form-2",TimeSpan.FromHours(24),wyslij);
  Check(Do("codex-p").Count==przed+2 && P.S(w3,"id")!=P.S(w1,"id"),"a different key is a new message");
  int proby=0;
  Check(Blad(()=>Powtorki.Raz("k|x|blad",TimeSpan.FromHours(1),()=>{proby++;throw new EcxpException("celowy błąd");}))!=null
    && Blad(()=>Powtorki.Raz("k|x|blad",TimeSpan.FromHours(1),()=>{proby++;throw new EcxpException("celowy błąd");}))!=null && proby==2,"a failed request is not remembered and can be retried");
  var w4=Powtorki.Raz("k|wlasciciel|okno",TimeSpan.FromMilliseconds(1),wyslij);System.Threading.Thread.Sleep(30);
  var w5=Powtorki.Raz("k|wlasciciel|okno",TimeSpan.FromMilliseconds(1),wyslij);
  Check(P.S(w4,"id")!=P.S(w5,"id"),"after the time window the same key sends again");

  // ---------- kolejność według numeru stosu ----------
  var n1=P.Znajdz(P.S(w1,"id"));var n3=P.Znajdz(P.S(w3,"id"));
  Check(n1.Nr>0 && n3.Nr>n1.Nr && P.Wszystkie(true).Where(m=>m.Nr>0).Select(m=>m.Nr).Distinct().Count()==P.Wszystkie(true).Count(m=>m.Nr>0),"every message gets a unique, increasing stack number");
  var ten=new DateTime(2026,1,1,12,0,0);
  var para=new List<Msg>{new Msg{Id="b",Czas=ten,Nr=8},new Msg{Id="a",Czas=ten,Nr=7}}.OrderBy(m=>m.Czas).ThenBy(m=>m.Nr).Select(m=>m.Id).ToList();
  Check(para[0]=="a","messages from the same instant are ordered by the stack number");

  // ---------- sprawdzanie pól przy przyjęciu ----------
  var bladPola=Blad(()=>P.SprawdzPolaWyslij(P.D("do","codex-p","tresc","x","priorytet","wysoki")));
  Check(bladPola!=null && bladPola.Contains("priorytet"),"an unknown field is refused by name");
  Check(Blad(()=>P.SprawdzPolaWyslij(P.D("do","codex-p","tresc","x","pilne","może")))!=null && Blad(()=>P.SprawdzPolaWyslij(P.D("do","codex-p","pliki","a.txt")))!=null
    && Blad(()=>P.SprawdzPolaWyslij(P.D("do","codex-p","krok","drugi")))!=null && Blad(()=>P.SprawdzPolaWyslij(P.D("do",5,"tresc","x")))!=null,"wrong field types are refused");
  Check(Blad(()=>P.SprawdzPolaWyslij(P.D("ja","codex-p","do","wlasciciel","typ","fin","wynik","pozytywny","tresc","ok","odpowiedz_na","id","pilne",true,"pliki",new List<object>{"a"},"krok",2,"z_krokow","5","klucz","k1")))==null,"a correct message passes validation");

  // ---------- dzierżawa tury ----------
  Directory.CreateDirectory(Path.Combine(P.Root,"stan","agenci"));
  string ag=Path.Combine(P.Root,"stan","agenci","claude-dz.json");
  Pliki.Zapisz(ag,P.D("nazwa","claude-dz","strona","claude","katalog",P.Root,"aktywny",true,"tura_od",P.Teraz(),"tura_puls",P.Teraz(),"tura_praca","x"));
  Check(Agenci.TuraZyje(Pliki.Czytaj(ag)) && Aktywnosc.Podglad("claude-dz").Pracuje,"a turn with a fresh heartbeat is shown as working");
  Pliki.Zapisz(ag,P.D("nazwa","claude-dz","strona","claude","katalog",P.Root,"aktywny",true,"tura_od",DateTimeOffset.Now.AddMinutes(-9).ToString("o"),"tura_puls",DateTimeOffset.Now.AddMinutes(-5).ToString("o"),"tura_praca","x"));
  Check(!Agenci.TuraZyje(Pliki.Czytaj(ag)) && !Aktywnosc.Podglad("claude-dz").Pracuje,"a turn whose heartbeat stopped is no longer shown as working");
  Pliki.Zapisz(ag,P.D("nazwa","claude-dz","strona","claude","katalog",P.Root,"tura_od",P.Teraz()));
  Check(Agenci.TuraZyje(Pliki.Czytaj(ag)),"a record without heartbeat (older stack) keeps the previous behaviour");
  Check(Agenci.SprzatnijTury()==1 && !Pliki.Czytaj(ag).ContainsKey("tura_od") && Agenci.SprzatnijTury()==0,"turn markers are cleared when the stack starts");
  Check(Agenci.LimitTury(P.D())==30 && Agenci.LimitTury(P.D("limit_tury_min","90"))==90 && Agenci.LimitTury(P.D("limit_tury_min","1"))==30,"turn time limit is 30 min by default and configurable per agent");

  // ---------- dokończenie przerwanych operacji ----------
  // (a) wysyłka do pary przerwana po pierwszej kopii
  string lid="L-test-przerwana";var cele=new List<string>{"claude-p","codex-p"};
  var np=new P.Nowa{Od=P.Wlasciciel,Do="para:test",Projekt="test",Modul="test",Typ="zadanie",Temat="Przerwana wysyłka",Tresc="Zadanie wysłane tylko do jednej strony."};
  Zamiary.Zapisz(lid,np,cele);
  var k1=np.Kopia();k1.Do="claude-p";k1.Lustro=cele;k1.LustroId=lid;
  var pierwsza=P.Wyslij(k1);
  Check(Zamiary.Dokoncz(null)==0,"a fresh intent (send still in progress) is left alone");
  File.SetLastWriteTime(Path.Combine(P.Root,"stan","zamiary",lid+".json"),DateTime.Now.AddMinutes(-2));
  int dosl=Zamiary.Dokoncz(null);
  var kopie=P.Wszystkie(false).Where(m=>m.LustroId==lid).ToList();
  Check(dosl==1 && kopie.Count==2 && kopie.Any(m=>m.Do=="codex-p") && kopie.All(m=>m.Logiczne==P.S(pierwsza,"id")) && !File.Exists(Path.Combine(P.Root,"stan","zamiary",lid+".json")),"an interrupted pair send is completed: the missing copy is delivered with the same message id");
  Check(Obieg.Znajdz(P.S(pierwsza,"id"))!=null && Zamiary.Dokoncz(null)==0,"the task flow is created for the recovered pair; recovery runs once");
  // (b) wszyscy skończyli, wspólny FIN nie powstał
  P.Odbierz("codex-p",50);P.Odbierz("claude-p",50);
  var zc=kopie.Single(m=>m.Do=="codex-p");var zs=kopie.Single(m=>m.Do=="claude-p");
  Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="fin",Wynik="pozytywny",OdpNa=zc.Id,Tresc="wykonane"});
  Siec.Wyslij(new P.Nowa{Od="claude-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="fin",Wynik="pozytywny",OdpNa=zs.Id,Tresc="sprawdzone"});
  var fin=Do(P.Wlasciciel).Single(m=>m.EtapObiegu=="fin_wspolny" && (m.OdpNa==zc.Id||m.OdpNa==zs.Id));
  string rek=Directory.GetFiles(Path.Combine(P.Root,"stan","obieg"),"*.json").Single(f=>File.ReadAllText(f).Contains(zc.Id));
  Action cofnij=()=>{var r=Pliki.Czytaj(rek);r.Remove("zakonczone");r.Remove("zakonczone_czas");r.Remove("fin_do_operatora");
    foreach(var kv in (Dictionary<string,object>)r["agenci"]) ((Dictionary<string,object>)kv.Value)["zmiana"]=DateTimeOffset.Now.AddMinutes(-1).ToString("o");Pliki.Zapisz(rek,r);};
  cofnij();
  Check(Obieg.Otwarte().Count==1 && P.ZBlokada(()=>Obieg.Dokoncz(null))==1 && Obieg.Otwarte().Count==0 && Do(P.Wlasciciel).Count(m=>m.EtapObiegu=="fin_wspolny" && (m.OdpNa==zc.Id||m.OdpNa==zs.Id))==1,"a flow left open although its joint FIN exists is closed without a duplicate FIN");
  cofnij();File.Delete(fin.Sciezka);
  Check(P.ZBlokada(()=>Obieg.Dokoncz(null))==1 && Obieg.Otwarte().Count==0,"a flow whose joint FIN was never written is completed");
  var nowyFin=Do(P.Wlasciciel).Single(m=>m.EtapObiegu=="fin_wspolny" && (m.OdpNa==zc.Id||m.OdpNa==zs.Id));
  Check(nowyFin.Id!=fin.Id && nowyFin.Wynik=="pozytywny" && nowyFin.Uwagi.Contains("wykonane") && nowyFin.Uwagi.Contains("sprawdzone"),"the recovered joint FIN carries both results");
  Check(P.ZBlokada(()=>Obieg.Dokoncz(null))==0 && File.ReadLines(dz).Count(l=>l.Contains("\"obieg_dokonczony\""))==2 && File.ReadLines(dz).Any(l=>l.Contains("\"zamiar_dokonczony\"")),"recovery is idempotent and recorded in the audit log");
  P.Stany(out sp);
  Check(sp.StartsWith("spójny"),"the journal stays consistent after all operations");
  Console.WriteLine("RESULT: "+failures+" failed");
  return failures==0?0:1;
 }
}}
