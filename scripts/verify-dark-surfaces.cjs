const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require(path.resolve('artifacts/ui-test/node_modules/playwright'));
const Axe=require(path.resolve('artifacts/ui-test/node_modules/@axe-core/playwright')).default;
(async()=>{
 const out='artifacts/sprint45/dark-surfaces';fs.mkdirSync(out,{recursive:true});const browser=await chromium.launch(),results=[];
 for(const before of [true,false]){
  const context=await browser.newContext({viewport:{width:390,height:900}}),page=await context.newPage();
  if(before)await page.route('**/css/para.css?*',r=>r.fulfill({contentType:'text/css',body:fs.readFileSync('artifacts/sprint45/para-before.css','utf8')}));
  const response=await page.goto('http://localhost:5180/cardapio/174');assert.equal(response.status(),200);
  const colors=await page.locator('.card.bg-forest').evaluateAll(es=>es.map(e=>({text:getComputedStyle(e).color,background:getComputedStyle(e).backgroundColor,heading:getComputedStyle(e.querySelector('h1')).color})));
  const violations=(await new Axe({page}).withTags(['wcag2a','wcag2aa']).analyze()).violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))}));
  await page.screenshot({path:path.join(out,before?'css-before.png':'after.png'),fullPage:true});results.push({beforeCssOnly:before,colors,violations});if(!before)assert.equal(violations.length,0);await context.close();
 }
 fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(results,null,2));console.log(JSON.stringify(results));await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
