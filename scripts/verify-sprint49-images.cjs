const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require(path.resolve('artifacts/ui-test/node_modules/playwright'));
(async()=>{
 const browser=await chromium.launch(),context=await browser.newContext({viewport:{width:390,height:844}}),page=await context.newPage(),report={assets:[],pages:[],failureStates:[]};
 try{
  const local=new Set();
  for(const route of ['/','/busca','/descobrir','/establishment/174','/pratos/manicoba','/receitas']){
   const response=await page.goto('http://localhost:5180'+route);assert.equal(response.status(),200);
   const urls=await page.locator('img').evaluateAll(imgs=>imgs.flatMap(i=>[i.getAttribute('src'),...(i.getAttribute('srcset')||'').split(',').map(s=>s.trim().split(/\s+/)[0])]).filter(s=>s?.startsWith('/images/')));
   urls.forEach(u=>local.add(u));report.pages.push({route,localImageCandidates:urls.length});
  }
  for(const url of local){
   const response=await context.request.get('http://localhost:5180'+url);assert.equal(response.status(),200);assert.ok(response.headers()['content-type'].startsWith('image/'));
   const bytes=await response.body();await page.evaluate(async({base64,type})=>{const img=new Image();img.src='data:'+type+';base64,'+base64;await img.decode();if(!img.naturalWidth)throw Error('decode');}, {base64:bytes.toString('base64'),type:response.headers()['content-type']});
   report.assets.push({url,status:200,bytes:bytes.length,decoded:true});
  }
  for(const failure of ['404','invalid','timeout','fallback-404']){
   const p=await context.newPage();await p.goto('http://localhost:5180/');let held,fallbackRequests=0;
   await p.route('https://example.invalid/**',r=>failure==='timeout'?(held=r):r.fulfill({status:failure==='invalid'?200:404,contentType:'image/webp',body:'not an image'}));
   if(failure==='fallback-404')await p.route('**/images/image-unavailable.svg',r=>{fallbackRequests++;return r.fulfill({status:404,body:''});});
   if(failure==='timeout')await p.clock.install();
   await p.evaluate(()=>{const img=new Image();img.width=320;img.height=220;img.dataset.probe='true';img.src='https://example.invalid/failure.webp';img.srcset='https://example.invalid/failure-large.webp 960w';document.querySelector('main').prepend(img);});
   if(failure==='timeout'){await p.waitForTimeout(150);await p.clock.fastForward(13000);}
   const img=p.locator('img[data-probe]');await p.waitForFunction(()=>document.querySelector('img[data-probe]')?.dataset.imageFallback==='true');assert.equal(await img.getAttribute('srcset'),null);
   if(failure==='fallback-404'){await p.waitForFunction(()=>document.querySelector('img[data-probe]').hidden);assert.equal(fallbackRequests,1);}else await img.evaluate(e=>e.decode());
   assert.ok(await p.locator('.image-fallback-note').first().isVisible());await p.screenshot({path:'artifacts/sprint49/image-'+failure+'.png'});report.failureStates.push({failure,fallbackRequests,handled:true});
   await held?.abort().catch(()=>{});await p.close();
  }
  fs.writeFileSync('artifacts/sprint49/images.json',JSON.stringify(report,null,2));console.log(JSON.stringify({pages:report.pages.length,assets:report.assets.length,failureStates:report.failureStates}));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
