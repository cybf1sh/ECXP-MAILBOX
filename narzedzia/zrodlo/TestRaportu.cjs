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
if(d.getElementById('tab-techniki')){
 click(d.getElementById('tab-techniki'));check(d.querySelector('.tabpane.active').id==='panel-techniki','techniques tab is available');
 const entries=Array.from(d.querySelectorAll('.observation'));
 for(const r of ['pozytywny','negatywny','niewykonane','brak']){filter.value=r;filter.dispatchEvent(new w.Event('change'));check(visible('.observation').length===entries.filter(n=>n.getAttribute('data-results')===r).length,'technique records filtered by recorded FIN '+r);}
 filter.value='';filter.dispatchEvent(new w.Event('change'));search.value='ZZZ_NO_TECHNIQUE_389456';search.dispatchEvent(new w.Event('input'));check(visible('.observation').length===0,'technique search excludes unmatched records');
 search.value='';search.dispatchEvent(new w.Event('input'));
 const detail=d.querySelector('.tech-field');if(detail){const before=detail.hasAttribute('open');click(detail.querySelector('summary'));check(detail.hasAttribute('open')!==before,'evidence fields expand interactively');}
 const fileLink=d.querySelector('.observation a[href^="#dowod-"]');if(fileLink){click(fileLink);const file=d.getElementById(fileLink.getAttribute('href').slice(1));check(file.hasAttribute('open')&&w.scrolledTo===file.id,'artifact opens with full original text');}
 const ref=d.querySelector('.observation a[href^="#m"]');if(ref){click(ref);check(d.querySelector('.tabpane.active').id==='panel-zrodla'&&d.getElementById(ref.getAttribute('href').slice(1)).className.includes('open'),'technique source opens exact message');}
}
const broken=Array.from(d.querySelectorAll('a[href^="#"]')).filter(a=>!d.getElementById(a.getAttribute('href').slice(1)));
if(d.getElementById('tab-profile')){
 click(d.getElementById('tab-profile'));check(d.querySelector('.tabpane.active').id==='panel-profile','configuration and scripts tab opens');
 const rows=d.querySelectorAll('.profile-record').length;filter.value='pozytywny';filter.dispatchEvent(new w.Event('change'));check(visible('.profile-record').length===rows,'FIN filter does not classify static source as execution');
 search.value='ZZZ_NO_PROFILE_18942';search.dispatchEvent(new w.Event('input'));check(visible('.profile-record').length===0,'profile search filters static source');search.value='';filter.value='';search.dispatchEvent(new w.Event('input'));
 const sourceLink=d.querySelector('.profile-record a[href^="#profil-source-"]');if(sourceLink){click(sourceLink);check(d.getElementById(sourceLink.getAttribute('href').slice(1)).hasAttribute('open'),'static extraction links to full source');}
}
check(broken.length===0,'all HTML and SVG source links resolve');check(errors.length===0,'no JavaScript errors after interactions');
console.log('RESULT '+count+' PASS; messages='+allMessages+' threads='+allThreads);dom.window.close();
