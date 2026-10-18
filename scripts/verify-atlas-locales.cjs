const fs=require('node:fs');
const path=require('node:path');
const root=process.env.VISUAL_TEST_MODULES||path.resolve('artifacts/ui-test/node_modules');
const {chromium}=require(path.join(root,'playwright'));
const AxeBuilder=require(path.join(root,'@axe-core/playwright')).default;
const base=process.env.VISUAL_TEST_BASE_URL||'http://localhost:5180';
const output=path.resolve(process.env.VISUAL_TEST_OUTPUT||'artifacts/sprint15/locales');
let browser;
(async()=>{
 fs.mkdirSync(output,{recursive:true});
 browser=await chromium.launch({headless:true});const results=[];
 for(const locale of ['en','es'])for(const width of [360,390,768,1024,1440]){
  const context=await browser.newContext({locale,viewport:{width,height:1000}});const page=await context.newPage();
  for(const route of ['/','/cidades','/guia','/busca']){
   const errors=[];const handler=e=>errors.push(e.message);page.on('pageerror',handler);
   const response=await page.goto(base+route,{waitUntil:'networkidle'});
   await page.waitForTimeout(1300);
   const language=await page.locator('html').getAttribute('lang');
   const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2);
   const missing=await page.locator('main').innerText().then(t=>/Atlas[A-Z]|Destination[A-Z]|CategoryDescription_/.test(t));
   const violations=width===360?(await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21a','wcag21aa','wcag22aa']).analyze()).violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)})):[];
   const name=(route==='/'?'home':route.slice(1))+'-'+locale+'-'+width;
   await page.screenshot({path:path.join(output,name+'.png'),fullPage:true});
   results.push({route,locale,width,status:response.status(),language,overflow,missing,errors,violations});page.off('pageerror',handler);
  }
  if(width===360){
   await page.goto(base+'/cidades',{waitUntil:'networkidle'});
   await page.locator('#atlas-city-query').fill('santarem');await page.locator('.atlas-city-filter button[type=submit]').click();
   await page.waitForURL('**/cidades?**');
   const matches=await page.locator('.atlas-city-card h3').allTextContents();
   if(!matches.includes('Santarém')||!matches.every(name=>name.normalize('NFD').replace(/[\u0300-\u036f]/g,'').toLowerCase().includes('santarem')))throw new Error('Municipality search failed');
   await page.locator('#atlas-city-query').fill('');await page.locator('#atlas-city-region').selectOption('Marajó');await page.locator('.atlas-city-filter button[type=submit]').click();
   await page.waitForURL('**region=Maraj**');
   if(await page.locator('.atlas-region-heading h2').count()!==1||await page.locator('.atlas-region-heading h2').innerText()!=='Marajó')throw new Error('Regional filter failed');
  }
  await context.close();
 }
 await browser.close();
 const failures=results.filter(r=>r.status!==200||r.language!==r.locale||r.overflow||r.missing||r.errors.length||r.violations.length);
 fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(results,null,2));
 console.log(JSON.stringify({cases:results.length,failures},null,2));if(failures.length)process.exitCode=1;
})().catch(async e=>{console.error(e);await browser?.close();process.exitCode=1});
