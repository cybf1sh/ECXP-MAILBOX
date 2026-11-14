// DOM tests; no browser, network, or external report resources.
const fs=require('fs'),path=require('path'),assert=require('assert');
const {JSDOM,VirtualConsole}=require(path.resolve(process.argv[2],'node_modules/jsdom'));
const errors=[],vc=new VirtualConsole();vc.on('jsdomError',e=>errors.push(e.message));
const dom=new JSDOM(fs.readFileSync(process.argv[3],'utf8'),{runScripts:'dangerously',virtualConsole:vc,beforeParse(w){w.HTMLElement.prototype.scrollIntoView=function(){w.scrolledTo=this.id;};}});
const w=dom.window,d=w.document;let count=0;
function check(v,n){assert.ok(v,n);count++;console.log('PASS '+n);}
function click(n){n.dispatchEvent(new w.MouseEvent('click',{bubbles:true,cancelable:true}));}
function visible(sel){return Array.from(d.querySelectorAll(sel)).filter(n=>n.style.display!=='none');}
const search=d.getElementById('report-search'),filter=d.getElementById('report-result'),sort=d.getElementById('report-sort');
check(errors.length===0,'scripts initialize without errors');
check(d.querySelectorAll('.tabpane.active').length===1&&d.querySelector('.tabpane.active').id==='panel-grafy','only decision models visible initially');
const allMessages=d.querySelectorAll('.message').length,allThreads=d.querySelectorAll('.thread').length;
for(const r of ['pozytywny','negatywny','niewykonane','brak']){
 filter.value=r;filter.dispatchEvent(new w.Event('change'));
 for(const cls of ['.message','.thread']){
 const expected=Array.from(d.querySelectorAll(cls)).filter(n=>n.getAttribute('data-results').split(' ').includes(r));
 check(visible(cls).length===expected.length&&visible(cls).every(n=>expected.includes(n)),r+' filter exact for '+cls);
 }
}
filter.value='';filter.dispatchEvent(new w.Event('change'));
search.value='NEISTNIEJACY_ZAPIS_492834762';search.dispatchEvent(new w.Event('input'));
check(visible('.thread').length===0&&visible('.message').length===0,'live search displays empty result state');
search.value='';search.dispatchEvent(new w.Event('input'));
check(visible('.message').length===allMessages&&visible('.thread').length===allThreads,'clearing search restores all entries');
for(const s of ['old','new']){sort.value=s;sort.dispatchEvent(new w.Event('change'));for(const cls of ['.thread','.message']){
 const times=Array.from(d.querySelectorAll(cls)).map(n=>n.getAttribute('data-time'));
 check(times.every((v,i)=>i===0||(s==='old'?v>=times[i-1]:v<=times[i-1])),s+' chronological ordering '+cls);
}}
sort.value='result';sort.dispatchEvent(new w.Event('change'));const rank={pozytywny:0,negatywny:1,niewykonane:2,brak:3};
const ranks=Array.from(d.querySelectorAll('.message')).map(n=>rank[n.getAttribute('data-results')]);check(ranks.every((v,i)=>!i||v>=ranks[i-1]),'sort by result groups messages');
const t=d.querySelector('.thread');click(t.querySelector('.toggle'));const svg=t.querySelector('svg');
check(svg&&svg.querySelector('polygon')&&svg.querySelector('path[marker-end]'),'model draws SVG decision diamonds and connected arrows');
check(svg.querySelector('[stroke-dasharray]')&&svg.textContent.includes('TAK')&&svg.textContent.includes('NIE'),'alternatives have dashed paths and yes/no labels');
click(t.querySelectorAll('.graph-tools button')[1]);check(svg.style.width==='1200px','graph zoom increases drawing size');
const source=svg.querySelector('a'),id=source.getAttribute('href').slice(1),target=d.getElementById(id);
search.value='NEISTNIEJACY_ZAPIS_492834762';search.dispatchEvent(new w.Event('input'));check(target.style.display==='none','test target hidden by search');
click(source);check(target.style.display!=='none'&&target.className.includes('open')&&d.querySelector('.tabpane.active').id==='panel-zrodla'&&w.scrolledTo===id,'graph source reveals hidden message and switches tab');
click(target.querySelector('.toggle'));check(!target.className.includes('open'),'source can collapse again');
click(d.getElementById('tab-metody'));check(d.querySelectorAll('.tabpane.active').length===1&&d.querySelector('.tabpane.active').id==='panel-metody','tabs switch without multiple visible panels');
const broken=Array.from(d.querySelectorAll('a[href^="#"]')).filter(a=>!d.getElementById(a.getAttribute('href').slice(1)));
check(broken.length===0,'all HTML and SVG source links resolve');check(errors.length===0,'no JavaScript errors after interactions');
console.log('RESULT '+count+' PASS; messages='+allMessages+' threads='+allThreads);dom.window.close();
