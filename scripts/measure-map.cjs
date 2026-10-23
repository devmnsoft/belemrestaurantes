const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(path.resolve('artifacts/ui-test/node_modules/playwright'));
(async()=>{
 const out=process.env.MAP_MEASURE_OUTPUT||'artifacts/sprint47/map-before.json';
 const browser=await chromium.launch(),samples=[];
 for(let i=0;i<5;i++){
  const context=await browser.newContext({viewport:{width:390,height:844}}),page=await context.newPage();
  if(process.env.MAP_SCRIPT_FILE)await page.route('**/js/catalog-map.js*',r=>r.fulfill({contentType:'application/javascript',body:fs.readFileSync(process.env.MAP_SCRIPT_FILE,'utf8')}));
  const responses=[];page.on('response',r=>{if(r.url().includes('/api/mapa/pontos'))responses.push(r);});
  await page.goto('http://localhost:5180/mapa');
  const start=Date.now();await page.locator('#map-open').click();
  await page.waitForFunction(()=>performance.getEntriesByName('map-useful').length>0);
  const elapsed=Date.now()-start;
  const timing=await page.evaluate(()=>({lcp:performance.getEntriesByType('largest-contentful-paint').at(-1)?.startTime||null,resources:performance.getEntriesByType('resource').filter(r=>r.name.includes('leaflet')||r.name.includes('/api/mapa/pontos')).map(r=>({name:r.name.split('/').at(-1),bytes:r.transferSize,duration:r.duration}))}));
  const response=responses.at(-1);const body=response?await response.text():'';
  samples.push({clickToUsefulMs:elapsed,requests:responses.length,pointPayloadBytes:Buffer.byteLength(body),...timing});await context.close();
 }
 await fetch('http://localhost:5180/api/mapa/pontos?bbox=-48.6,-1.6,-48.3,-1.3&limit=200');
 const api=[];
 for(let i=0;i<15;i++){const started=performance.now(),r=await fetch('http://localhost:5180/api/mapa/pontos?bbox=-48.6,-1.6,-48.3,-1.3&limit=200');if(!r.ok)throw Error('HTTP '+r.status);const text=await r.text();api.push({ms:performance.now()-started,bytes:Buffer.byteLength(text)});}
 const sorted=api.map(r=>r.ms).sort((a,b)=>a-b),clicks=samples.map(r=>r.clickToUsefulMs).sort((a,b)=>a-b);
 const report={samples,api,medianClickToUsefulMs:clicks[2],apiP50Ms:sorted[7],apiP95Ms:sorted[14],note:'Local Chromium, 390x844, published local catalogue; optional JS baseline override; warmed API connection; no configured street tiles, no external routing calls.'};
 fs.mkdirSync(path.dirname(out),{recursive:true});fs.writeFileSync(out,JSON.stringify(report,null,2));console.log(JSON.stringify({medianClickToUsefulMs:report.medianClickToUsefulMs,apiP50Ms:report.apiP50Ms,apiP95Ms:report.apiP95Ms}));await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
