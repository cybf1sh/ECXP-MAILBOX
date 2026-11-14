// Test edits against a real exported report, with current embedded report code.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {JSDOM,VirtualConsole}=require(path.resolve(process.argv[2],'node_modules/jsdom'));
const original=fs.readFileSync(process.argv[3],'utf8');
const start=original.indexOf('<style>\nbody{max-width:1440px')>=0?original.indexOf('<style>\nbody{max-width:1440px'):original.indexOf('<style>\r\nbody{max-width:1440px');
assert(start>=0,'embedded report resource found');
const html=original.slice(0,start)+fs.readFileSync(path.join(__dirname,'obserwatorium-interakcje.html'),'utf8')+'</body></html>';
if(process.argv[4])fs.writeFileSync(process.argv[4],html);
const errors=[],vc=new VirtualConsole();vc.on('jsdomError',e=>errors.push(e.message));
const dom=new JSDOM(html,{runScripts:'dangerously',url:'https://local-test.invalid/report',virtualConsole:vc,beforeParse(w){w.HTMLElement.prototype.scrollIntoView=function(){};}}),w=dom.window,d=w.document;
let checks=0;function check(v,msg){assert.ok(v,msg);checks++;console.log('PASS '+msg);}
function click(parent,label){const b=Array.from(parent.querySelectorAll('button')).find(n=>n.textContent===label);assert(b,label);b.click();}
const thread=d.querySelector('.thread'),originalEvidence=thread.querySelector('.evidence').textContent;
click(thread,'Edytuj wariant grafu');const box=thread.querySelector('.variant-editor');
const field=label=>box.querySelector('[aria-label="'+label+'"]');
check(!box.hidden&&box.querySelector('svg'),'editor opens and draws variant');
const originalCount=box.querySelectorAll('svg [role=button]').length;
click(box,'Dodaj krok / alternatywę');field('Opis kroku').value='<img src=x onerror=alert(1)> Alternatywna metoda';field('Rodzaj').value='test';field('Uzasadnienie, metoda, oczekiwany wynik i kryterium testu').value='Sprawdź wynik i porównaj z kryterium.';
click(box,'Zastosuj zmiany węzła');
check(box.querySelectorAll('svg [role=button]').length===originalCount+1&&!box.querySelector('img'),'node added, edited, and markup treated as text');
field('Połączenie do').value=field('Węzeł do edycji').value;field('Etykieta połączenia (np. TAK / NIE)').value='NIE';click(box,'Dodaj połączenie');
check(box.querySelector('svg').textContent.includes('NIE'),'alternative edge rendered');
click(box,'Zapisz w tej przeglądarce');const key=w.localStorage.key(0),saved=JSON.parse(w.localStorage.getItem(key));
check(saved.nodes.length===originalCount+1&&saved.edges.some(e=>e.label==='NIE'),'complete graph saved');
click(box,'Usuń wybrany węzeł');check(box.querySelectorAll('svg [role=button]').length===originalCount,'node removed');
click(box,'Wczytaj zapis lokalny');check(box.querySelectorAll('svg [role=button]').length===originalCount+1,'saved graph restored');
const before=box.querySelector('svg').textContent;saved.edges.push({from:'missing',to:'missing',label:'TAK'});w.localStorage.setItem(key,JSON.stringify(saved));click(box,'Wczytaj zapis lokalny');
check(box.querySelector('.variant-status').textContent.includes('Nieprawidłowe połączenie')&&box.querySelector('svg').textContent===before,'invalid import leaves existing graph intact');
check(thread.querySelector('.evidence').textContent===originalEvidence,'original evidence unchanged');
check(errors.length===0,'no JavaScript errors');console.log('RESULT '+checks+' PASS');dom.window.close();
