const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium,webkit,devices}=require(path.resolve('artifacts/ui-test/node_modules/playwright'));
const Axe=require(path.resolve('artifacts/ui-test/node_modules/@axe-core/playwright')).default;
const output='artifacts/sprint53-ui';fs.mkdirSync(output,{recursive:true});
(async()=>{const report=[];
for(const [name,engine,profile] of [['desktop',chromium,{viewport:{width:1440,height:900}}],['android',chromium,devices['Pixel 7']],['ios',webkit,devices['iPhone 13']]]){
const browser=await engine.launch();try{const {defaultBrowserType,...options}=profile;const context=await browser.newContext(options);const page=await context.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
for(const term of ['tacacá','Na Maré','zzSemReferencias53']){
await page.goto('http://localhost:5180/busca?q='+encodeURIComponent(term));await page.locator('[data-search-status]').filter({hasText:/referência(?:s)? encontrada(?:s)?|Nenhuma referência|Não foi possível/}).waitFor({timeout:50000});
if(term==='Na Maré')assert.equal(await page.locator('[data-search-sources] h3').filter({hasText:'Maré · Wikipédia'}).count(),0);
assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2),false);
const axe=await new Axe({page}).include('.search-evidence').include('.search-topics').withTags(['wcag2a','wcag2aa']).analyze();assert.equal(axe.violations.length,0,JSON.stringify(axe.violations));assert.deepEqual(errors,[]);
report.push({name,term,status:await page.locator('[data-search-status]').innerText(),sources:await page.locator('[data-search-sources] article').count(),violations:axe.violations.length});
await page.screenshot({path:path.join(output,name+'-'+report.length+'.png')});}
await page.goto('http://localhost:5180/cuia-ia');assert.equal(await page.locator('#ai-search-web').isChecked(),true);assert.ok(await page.locator('.search-topic-links a').count()>0);
await context.close();}finally{await browser.close();}}
fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));})().catch(e=>{console.error(e);process.exit(1)});
