const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium,webkit}=require(path.resolve('artifacts/ui-test/node_modules/playwright'));
const Axe=require(path.resolve('artifacts/ui-test/node_modules/@axe-core/playwright')).default;
const base='http://localhost:5180',out='artifacts/sprint57-ui';fs.mkdirSync(out,{recursive:true});
(async()=>{
 const report=[];
 for(const [engine,width,height] of [[chromium,360,780],[webkit,390,844],[chromium,768,900],[chromium,1024,768],[chromium,1440,900],[chromium,740,360]]){
  const browser=await engine.launch();const context=await browser.newContext({viewport:{width,height}});const page=await context.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
  let requests=0;
  await page.route('**/api/busca/fontes?*',async route=>{requests++;const q=new URL(route.request().url()).searchParams.get('q');await route.fulfill({json:{status:'ok',sources:[{title:'Referência para '+q,url:'https://example.org/'+encodeURIComponent(q),snippet:'Contexto específico para '+q,retrievedAt:new Date().toISOString()}]}});});
  const r=await page.goto(base+'/busca?q=pizza&city=Bel%C3%A9m');assert.equal(r.status(),200);
  await page.locator('[data-search-sources] article').waitFor();
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2),false,'overflow '+width);
  const geometry=await page.locator('.para-explore-layout').evaluate(e=>{const a=e.querySelector('aside').getBoundingClientRect(),b=e.querySelector('.para-results').getBoundingClientRect();return{filterX:a.x,resultX:b.x,filterY:a.y,resultY:b.y,open:getComputedStyle(e.querySelector('[data-filter-open]')).display};});
  if(width>760){assert.equal(geometry.open,'none');assert.ok(geometry.resultX>geometry.filterX);assert.ok(Math.abs(geometry.filterY-geometry.resultY)<4);}
  else {
   await page.locator('[data-filter-open]').click();assert.equal(await page.locator('.para-filter-panel').getAttribute('role'),'dialog');
   await page.keyboard.press('Escape');assert.equal(await page.locator('body').evaluate(e=>e.classList.contains('para-scroll-lock')),false);
   await page.locator('[data-filter-open]').click();
  }
  await page.locator('#searchQuery').fill('hamburguer de camaração');
  await page.locator('#catalog-filters button[type=submit]').click();
  await page.waitForURL(/camar/);await page.locator('[data-search-sources] article').filter({hasText:'camarão'}).waitFor();
  assert.ok(requests>=2,'external search restarted');
  assert.equal(await page.locator('#searchQuery').inputValue(),'hamburguer de camarão');
  assert.ok(await page.locator('[data-culinary-id]').count()>0,'collected recipe visible');
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2),false);
  const axe=await new Axe({page}).include('main').withTags(['wcag2a','wcag2aa']).analyze();assert.deepEqual(axe.violations.map(v=>v.id),[]);
  await page.screenshot({path:path.join(out,width+'x'+height+'.png'),fullPage:false});assert.deepEqual(errors,[]);
  report.push({width,height,requests,geometry,accessibility:'passed'});await browser.close();
 }
 // Real HTTP proof: persisted culinary results and public Cuia endpoint, no new paid search call.
 const b=await chromium.launch(),page=await b.newPage();await page.goto(base+'/cuia-ia');
 const token=await page.locator('input[name=__RequestVerificationToken]').first().inputValue();
 const conversation=[];const questions=['Quero hambúrguer de camarão em Belém','Tem receita publicada?','Mostre um vídeo','Onde compro os ingredientes?','Quanto custa preparar quatro porções?'];
 for(const prompt of questions){const response=await page.request.post(base+'/api/cuia-ia/ask',{headers:{'X-CSRF-TOKEN':token},data:{prompt,searchWeb:false,previousQuestions:conversation}});assert.equal(response.status(),200);const data=await response.json();assert.match(data.answerMarkdown,/camarão/i);assert.ok(data.sources.some(s=>s.url.includes('gourmetadois.com')));assert.equal(data.engineUsed,'Cuia');conversation.splice(0,conversation.length,...[questions[0],...conversation.filter(q=>q!==questions[0]),prompt].filter((x,i,a)=>a.indexOf(x)===i).filter((x,i,a)=>i===0||i>=a.length-2));}
 await page.goto(base+'/receitas?Query='+encodeURIComponent('hambúrguer de camarão'));assert.ok(await page.locator('a[href^="/culinaria/referencia/"]').count()>0);await page.locator('a[href^="/culinaria/referencia/"]').first().click();assert.match(await page.locator('main').innerText(),/450 g|450g/);assert.ok(await page.locator('a[href*="youtube.com/watch"]').count()>0);assert.ok(await page.locator('a[href*="amazon.com.br/s"]').count()>0);
 await b.close();fs.writeFileSync(path.join(out,'report.json'),JSON.stringify({report,conversation,realConversation:'5 HTTP requests passed'},null,2));console.log(JSON.stringify({viewports:report.length,conversation:5,failures:[]}));
})().catch(e=>{console.error(e);process.exit(1);});
