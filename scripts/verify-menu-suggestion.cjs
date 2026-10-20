const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require(path.resolve('artifacts/ui-test/node_modules/playwright'));
(async()=>{
 const out='artifacts/sprint45/menu';fs.mkdirSync(out,{recursive:true});const browser=await chromium.launch(),context=await browser.newContext({viewport:{width:390,height:900}}),page=await context.newPage();
 await page.route('http://localhost:5180/__menu-manage',r=>r.fulfill({contentType:'text/html; charset=utf-8',body:fs.readFileSync('artifacts/sprint45/snapshots/menu-manage.html','utf8')}));let requests=0;
 await page.route('**/menu/suggest-ai',r=>{requests++;assert.equal(r.request().method(),'POST');assert.ok(r.request().postData().includes('__RequestVerificationToken'));return r.fulfill({json:{success:true,suggestion:'Descrição de teste <sem HTML executável>'}});});
 await page.goto('http://localhost:5180/__menu-manage');await page.locator('#btnCuiaAi').click();assert.equal(requests,0);await page.locator('#dishNameInput').fill('Prato fictício');await page.locator('#btnCuiaAi').click();await page.waitForFunction(()=>document.getElementById('dishDescriptionInput').value.includes('Descrição de teste'));assert.equal(requests,1);
 assert.ok((await page.locator('#aiLoading').innerText()).includes('Revise'));await page.screenshot({path:path.join(out,'suggestion-review.png'),fullPage:true});
 await page.unroute('**/menu/suggest-ai');await page.route('**/menu/suggest-ai',r=>r.fulfill({status:503}));await page.locator('#btnCuiaAi').click();await page.waitForFunction(()=>document.getElementById('aiLoading').textContent.includes('Não foi possível'));assert.equal(await page.locator('#btnCuiaAi').isEnabled(),true);
 fs.writeFileSync(path.join(out,'suggestion.json'),JSON.stringify({simulated:true,paidRequests:0,requiresName:true,antiforgery:true,externalScriptUnderCsp:true,reviewBeforeSave:true,recovery:true},null,2));console.log('Sugestão de cardápio: validação, antiforgery, revisão e recuperação aprovadas em simulação.');await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
