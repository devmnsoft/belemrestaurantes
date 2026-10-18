const fs=require('node:fs'),path=require('node:path');
const root=process.env.VISUAL_TEST_MODULES||path.resolve('artifacts/ui-test/node_modules');
const {chromium}=require(path.join(root,'playwright'));
const AxeBuilder=require(path.join(root,'@axe-core/playwright')).default;
const base=process.env.VISUAL_TEST_BASE_URL||'http://localhost:5180';
const output=path.resolve(process.env.VISUAL_TEST_OUTPUT||'artifacts/sprint19/live-catalog');
const ids=(process.env.VISUAL_TEST_RESTAURANT_IDS||'174,428').split(',').map(Number);
let browser;
(async()=>{
 fs.mkdirSync(output,{recursive:true});browser=await chromium.launch({headless:true});const results=[];
 for(const width of [360,390,768,1024,1440]){
  const context=await browser.newContext({viewport:{width,height:1000}}),page=await context.newPage();
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.route('https://www.openstreetmap.org/**',route=>route.fulfill({contentType:'text/html',body:'<p>Provedor isolado somente neste teste de consentimento.</p>'}));
  for(const id of ids){
   const response=await page.goto(base+'/estabelecimento/'+id,{waitUntil:'networkidle'});await page.waitForTimeout(1300);
   if(response.status()!==200||!await page.locator('h1').innerText())throw Error('Published establishment is not public: '+id);
   if(await page.locator('iframe').count())throw Error('Destination map loaded before a user decision');
   const violations=width===360?(await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations:[];
   const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2);
   if(overflow||violations.length||errors.length)throw Error(JSON.stringify({id,width,overflow,violations,errors}));
   await page.screenshot({path:path.join(output,id+'-'+width+'.png'),fullPage:true});
   const load=page.locator('[data-load-map]');
   if(await load.count()){await load.locator('xpath=..').locator('summary').click();await load.click();if(await page.locator('#visit-map iframe').count()!==1)throw Error('Destination map consent failed');}
   await page.getByRole('link',{name:'Planejar minha visita →',exact:true}).click();await page.waitForURL('**/cuia-rotas?**');
   if(!await page.locator('h1').count()||!await page.getByText('Ainda não há histórico suficiente',{exact:false}).count())throw Error('Honest mobility fallback is missing');
   await page.waitForTimeout(1300);await page.screenshot({path:path.join(output,id+'-visit-'+width+'.png'),fullPage:true});
   results.push({id,width,status:200,public:true,mapConsent:true,mobilityFallback:true,violations:0});
  }
  await context.close();
 }
 fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(results,null,2));console.log(JSON.stringify({cases:results.length,failures:0}));await browser.close();
})().catch(async error=>{console.error(error);await browser?.close();process.exitCode=1;});
