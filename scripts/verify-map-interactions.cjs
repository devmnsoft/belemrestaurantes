const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require(path.resolve('artifacts/ui-test/node_modules/playwright'));
const AxeBuilder=require(path.resolve('artifacts/ui-test/node_modules/@axe-core/playwright')).default;
(async()=>{
 const out='artifacts/sprint44/interactions';fs.mkdirSync(out,{recursive:true});
 const browser=await chromium.launch(),context=await browser.newContext({viewport:{width:390,height:844}}),page=await context.newPage(),errors=[];
 page.on('pageerror',e=>errors.push(e.message));let requests=0;
 page.on('request',r=>{if(r.url().includes('/api/mapa/pontos'))requests++});
 await page.goto('http://localhost:5180/mapa');assert.equal(requests,0);assert.equal(await page.locator('.leaflet-container').count(),0);
 const start=Date.now();await page.locator('#map-open').click();await page.locator('#map-live-list:not([hidden])').waitFor();
 const usefulMs=Date.now()-start;assert.ok(await page.locator('.map-pin').count());const initial=requests;
 await page.locator('#map-open').click();await page.waitForTimeout(400);assert.equal(requests,initial);
 await page.getByRole('button',{name:'Zoom in',exact:true}).click();await page.waitForTimeout(700);assert.ok(requests>initial);
 await page.screenshot({path:path.join(out,'interactive-390.png'),fullPage:true});
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2),false);
 const mapAxe=(await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag22aa']).analyze()).violations;
 await page.route('**/api/mapa/pontos?**',route=>route.fulfill({status:503}));
 await page.getByRole('button',{name:'Zoom in',exact:true}).click();await page.locator('#map-retry:not([hidden])').waitFor();
 assert.ok(await page.locator('#map-results a').count());assert.equal(await page.locator('#map-results').isVisible(),true);
 await page.unroute('**/api/mapa/pontos?**');await page.locator('#map-retry').click();await page.locator('#map-retry[hidden]').waitFor();
 const api=await page.request.get('http://localhost:5180/api/mapa/pontos?bbox=NaN,0,1,2');assert.equal(api.status(),400);
 const reading=[];
 for(const size of ['100','125','150','175','200']){
  await page.goto('http://localhost:5180/mapa');await page.locator('#cuia-acessivel summary').click();await page.locator('#reading-size').selectOption(size);
  const dimensions=await page.evaluate(()=>({root:parseFloat(getComputedStyle(document.documentElement).fontSize),overflow:document.documentElement.scrollWidth>innerWidth+2}));
  reading.push({size,...dimensions});assert.equal(dimensions.root,18*Number(size)/100);
  await page.screenshot({path:path.join(out,'reading-'+size+'.png'),fullPage:true});
 }
 await page.locator('[data-reading-contrast]').check();
 const contrast=(await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag22aa']).analyze()).violations;
 await page.screenshot({path:path.join(out,'contrast-200.png'),fullPage:true});
 await page.reload();assert.equal(await page.locator('html').getAttribute('data-text-size'),'200');
 await page.locator('#reading-size').focus();await page.keyboard.press('Escape');assert.equal(await page.locator('#cuia-acessivel').getAttribute('open'),null);
 assert.equal(reading.some(r=>r.overflow),false);assert.equal(mapAxe.length,0);assert.equal(contrast.length,0);assert.equal(errors.length,0);
 await page.emulateMedia({forcedColors:'active'});await page.screenshot({path:path.join(out,'forced-colors.png'),fullPage:true});
 fs.writeFileSync(path.join(out,'report.json'),JSON.stringify({usefulMs,requests,errors,reading,mapAxe,contrast},null,2));
 console.log(JSON.stringify({usefulMs,errors,reading,mapAxe:mapAxe.map(v=>({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})),contrast:contrast.map(v=>v.id)},null,2));await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
