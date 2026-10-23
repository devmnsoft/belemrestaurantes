const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require(path.resolve('artifacts/ui-test/node_modules/playwright'));
const Axe=require(path.resolve('artifacts/ui-test/node_modules/@axe-core/playwright')).default;
const base=process.env.VISUAL_TEST_BASE_URL||'http://localhost:5180',out='artifacts/sprint50/ui';fs.mkdirSync(out,{recursive:true});
(async()=>{
 const browser=await chromium.launch(),cases=[];let errors=[];
 async function inspect(page,name,width){
  const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2);
  const violations=(await new Axe({page}).withTags(['wcag2a','wcag2aa','wcag22aa']).analyze()).violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}));
  cases.push({name,width,overflow,violations,errors:[...errors]});errors=[];fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(cases,null,2));await page.screenshot({path:path.join(out,name+'-'+width+'.png')});
  assert.equal(overflow,false);assert.deepEqual(violations,[]);assert.deepEqual(cases.at(-1).errors,[]);
 }
 try{
  for(const width of [320,390,768,1440]){
   const context=await browser.newContext({viewport:{width,height:900}}),page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));
   for(const route of ['/','/travessias','/roteiros','/roteiros/combu-rio-e-cacau','/roteiros/mosqueiro-vila-e-paisagem','/meu-passeio','/busca?q=fixture50-no-such-place']){
    const response=await page.goto(base+route);assert.equal(response.status(),200);
    if(route==='/'){assert.equal(await page.locator('.hero-documental img').getAttribute('fetchpriority'),'high');await page.locator('.hero-documental img').evaluate(e=>e.decode());assert.ok((await page.locator('.atlas-hero-art figcaption').innerText()).includes('Cayambe'));}
    if(route==='/travessias')assert.ok(await page.locator('.editorial-grid .editorial-card').count()>=7);
    await inspect(page,route.replace(/[^a-z0-9]/gi,'_'),width);
   }
   if(width===320){
    await page.evaluate(()=>localStorage.setItem('belem-reading',JSON.stringify({size:'200'})));
    for(const route of ['/','/travessias','/roteiros/combu-rio-e-cacau']){await page.goto(base+route);await inspect(page,'text200'+route.replaceAll('/','_'),width);}
   }
   await context.close();
  }
  const context=await browser.newContext({viewport:{width:390,height:844}}),page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));
  await page.goto(base+'/roteiros/combu-rio-e-cacau');await page.locator('[data-save-trail]').click();await page.getByText('Etapas guardadas.',{exact:false}).waitFor();await page.locator('[data-save-trail]').click();assert.equal(await page.evaluate(()=>JSON.parse(localStorage.getItem('belem-discoveries-v1')).length),3);
  await page.goto(base+'/meu-passeio');await page.locator('.discovery-stop').nth(2).waitFor();const name=await page.locator('.discovery-stop h2').nth(1).textContent();await page.locator('[data-move=up]').nth(1).click();assert.ok((await page.locator('.discovery-stop h2').first().textContent()).includes(name.replace(/^\d+\. /,'')));await page.locator('[data-remove]').first().click();await page.reload();assert.equal(await page.locator('.discovery-stop').count(),2);await inspect(page,'saved-trail-edited',390);
  await page.evaluate(()=>localStorage.removeItem('belem-discoveries-v1'));
  let payload={answerMarkdown:'### Plano do dia\n1.\n**Continuar nesta aba**\n1. Conhecer o mercado\n2. Escolher um prato\n<script>alert(1)</script>',answerKind:'catalogo',generationStatus:'evidence_limited',searchStatus:'empty',sources:[]};
  await page.route('**/api/cuia-ia/ask',r=>r.fulfill({contentType:'application/json',body:JSON.stringify(payload)}));await page.goto(base+'/cuia-ia');
  for(const status of ['not_configured','empty','unavailable','quota_exhausted']){
   payload.searchStatus=status;await page.locator('#ai-prompt').fill('Pratos do Restaurante de Teste');await page.locator('#ai-submit').click();await page.locator('#ai-results .cuia-search-state').waitFor();assert.equal(await page.locator('#ai-results ol:empty,#ai-results ul:empty,#ai-results li:empty').count(),0);assert.equal(await page.locator('#ai-results script').count(),0);assert.equal(await page.locator('#ai-results h3').filter({hasText:/Fontes/}).count(),0);await inspect(page,'search-'+status,390);
  }
  payload={...payload,searchStatus:'public_sources_cached',sources:[{title:'',url:'https://example.org'},{title:'Unsafe',url:'javascript:alert(1)'},{title:'Referência pública',url:'https://pt.wikipedia.org/?curid=42',retrievedAt:'2026-10-20T12:00:00Z'}]};await page.locator('#ai-submit').click();await page.locator('#ai-results').getByText('Fontes consultadas',{exact:true}).waitFor();assert.equal(await page.locator('#ai-results a[href^="javascript:"]').count(),0);await inspect(page,'valid-source-and-markdown',390);
  await page.goto(base+'/descobrir');await page.locator('.cuia-widget-toggle').click();await page.locator('#cuia-widget-prompt').fill('Uma ideia de passeio no Pará');await page.locator('.cuia-widget-submit').click();await page.locator('.cuia-widget-results').getByText('Fontes consultadas',{exact:true}).waitFor();await inspect(page,'widget-source-markdown',390);await context.close();
  // Replay only authorized integration snapshots; the production app has no test login bypass.
  const snap='artifacts/sprint50/private-snapshots',privateContext=await browser.newContext({viewport:{width:320,height:900}}),privatePage=await privateContext.newPage();privatePage.on('pageerror',e=>errors.push(e.message));
  for(const file of fs.readdirSync(snap).filter(n=>/^admin-roteiros(?:-\d+)?\.html$/.test(n))){const route='/'+file.replace('.html','').replace('admin-roteiros','admin/roteiros').replace(/-(\d+)$/,'/$1');await privatePage.route(base+route,r=>r.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(snap,file),'utf8')}));await privatePage.goto(base+route);await inspect(privatePage,file.replace('.html',''),320);}await privateContext.close();
  console.log(JSON.stringify({cases:cases.length,failures:cases.filter(c=>c.overflow||c.violations.length||c.errors.length)}));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
