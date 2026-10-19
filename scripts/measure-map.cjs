// Local Chromium, cold browser context per sample. No network throttling unless SLOW=1.
const fs = require('node:fs'), path = require('node:path');
const {chromium} = require(path.resolve('artifacts/ui-test/node_modules/playwright'));
(async () => {
 const output = process.env.MAP_OUTPUT || 'artifacts/sprint44/before'; fs.mkdirSync(output,{recursive:true});
 const browser = await chromium.launch(); const results=[];
 for(let i=0;i<5;i++) {
  const context=await browser.newContext({viewport:{width:390,height:844}}), page=await context.newPage();
  if(process.env.SLOW) { const cdp=await context.newCDPSession(page); await cdp.send('Network.enable'); await cdp.send('Network.emulateNetworkConditions',{offline:false,latency:150,downloadThroughput:200000,uploadThroughput:90000}); }
  await page.addInitScript(()=>{window.observed={lcp:0,cls:0,longTasks:0,interaction:0}; for(const type of ['largest-contentful-paint','layout-shift','longtask','event'])try{new PerformanceObserver(list=>{for(const e of list.getEntries()){if(type==='largest-contentful-paint')observed.lcp=e.startTime;if(type==='layout-shift'&&!e.hadRecentInput)observed.cls+=e.value;if(type==='longtask')observed.longTasks+=e.duration;if(type==='event'&&e.interactionId)observed.interaction=Math.max(observed.interaction,e.duration);}}).observe({type,buffered:true,durationThreshold:16});}catch{}});
  const response=await page.goto((process.env.VISUAL_TEST_BASE_URL||'http://localhost:5180')+'/mapa'); await page.waitForTimeout(1800);
  const metrics=await page.evaluate(()=>{const n=performance.getEntriesByType('navigation')[0], r=performance.getEntriesByType('resource');return {...observed,ttfb:n.responseStart-n.requestStart,htmlBytes:n.decodedBodySize,transferred:n.transferSize+r.reduce((a,e)=>a+e.transferSize,0),requests:1+r.length,memory:performance.memory?.usedJSHeapSize,points:document.querySelectorAll('.catalog-map circle').length,resources:r.map(e=>({name:e.name,duration:e.duration,size:e.transferSize})),navigationMs:n.duration};});
  results.push({status:response.status(),serverTiming:response.headers()['server-timing'],...metrics}); if(i===0)await page.screenshot({path:path.join(output,'mapa-390.png'),fullPage:true}); await context.close();
 }
 fs.writeFileSync(path.join(output,'metrics.json'),JSON.stringify({network:process.env.SLOW?'150ms / 1.6Mbps':'local, unthrottled',viewport:390,samples:results},null,2));console.log(JSON.stringify(results.map(({resources,...r})=>r)));await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
