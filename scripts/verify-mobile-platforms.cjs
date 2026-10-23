const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium,webkit,devices}=require(path.resolve('artifacts/ui-test/node_modules/playwright'));
(async()=>{
 const out=process.env.PLATFORM_OUTPUT||'artifacts/sprint47/platforms';fs.mkdirSync(out,{recursive:true});const report=[];
 for(const [name,type,device] of [['android-chromium',chromium,'Pixel 7'],['ios-webkit',webkit,'iPhone 13']]){
  if(!fs.existsSync(type.executablePath())){report.push({name,status:'not_available'});continue;}
  const browser=await type.launch(),{defaultBrowserType,...profile}=devices[device];
  const context=await browser.newContext(profile),page=await context.newPage();
  for(const landscape of [false,true]){
   await page.setViewportSize(landscape?{width:profile.viewport.height,height:profile.viewport.width}:profile.viewport);
   for(const route of ['/descobrir','/meu-passeio','/cuia-ia','/mapa','/account/register','/admin/login','/','/travessias','/roteiros/combu-rio-e-cacau']){
    const errors=[];page.removeAllListeners('pageerror');page.on('pageerror',e=>errors.push(e.message));
    const response=await page.goto('http://localhost:5180'+route);assert.equal(response.status(),200);
    if(route==='/cuia-ia')await page.locator('#ai-prompt').fill('Quero conhecer os sabores do Pará');
    if(route==='/descobrir'){await page.locator('.cuia-widget-toggle').click();await page.locator('#cuia-widget-prompt').fill('Uma ideia de passeio');assert.equal(await page.locator('#cuia-widget-panel').isVisible(),true);await page.locator('.cuia-widget-close').click();}
    if(route==='/account/register'||route==='/admin/login'){const toggle=page.locator('.password-toggle').first();const target=await toggle.getAttribute('aria-controls');const field=page.locator('[id="'+target+'"]');await toggle.click();assert.equal(await field.getAttribute('type'),'text');await toggle.click();assert.equal(await field.getAttribute('type'),'password');}
    const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2);
    const buttons=await page.locator('.para-save,.cuia-widget-toggle,.cuia-widget-close').evaluateAll(es=>es.filter(e=>e.getBoundingClientRect().height>0).map(e=>({width:e.getBoundingClientRect().width,height:e.getBoundingClientRect().height})));
    await page.screenshot({path:path.join(out,name+'-'+(landscape?'landscape':'portrait')+route.replaceAll('/','_')+'.png'),fullPage:false});
    report.push({name,emulatedDevice:device,landscape,route,status:response.status(),overflow,errors,minTouchHeight:buttons.length?Math.min(...buttons.map(b=>b.height)):null});
    assert.equal(overflow,false);assert.equal(errors.length,0);assert.ok(buttons.every(b=>b.height>=44));
   }
  }await browser.close();
 }
 fs.writeFileSync(path.join(out,'report.json'),JSON.stringify({cases:report,note:'Emulated viewport/user agent/touch with Chromium and Playwright WebKit on Windows. Not physical Android/iOS or native Safari certification.'},null,2));console.log(JSON.stringify(report));
})().catch(e=>{console.error(e);process.exit(1)});
