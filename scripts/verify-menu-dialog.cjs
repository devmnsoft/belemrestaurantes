const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require(path.resolve('artifacts/ui-test/node_modules/playwright'));
const Axe=require(path.resolve('artifacts/ui-test/node_modules/@axe-core/playwright')).default;
(async()=>{
 const out='artifacts/sprint45/menu';fs.mkdirSync(out,{recursive:true});const browser=await chromium.launch(),context=await browser.newContext({viewport:{width:320,height:900}}),page=await context.newPage();let images=0;
 await page.route('https://api.qrserver.com/**',r=>{images++;return r.fulfill({contentType:'image/svg+xml',body:'<svg xmlns="http://www.w3.org/2000/svg" width="220" height="220"><rect width="220" height="220" fill="white"/><text x="15" y="110">IMAGEM DE TESTE</text></svg>'});});
 await page.goto('http://localhost:5180/cardapio/174');assert.equal(images,0);assert.equal(await page.locator('dialog').isVisible(),false);
 const triggerColors=await page.locator('[data-menu-code-open]').evaluate(e=>({color:getComputedStyle(e).color,background:getComputedStyle(e).backgroundColor}));assert.notEqual(triggerColors.color,triggerColors.background);
 await page.locator('[data-menu-code-open]').click();await page.locator('dialog[open]').waitFor();await page.waitForTimeout(200);assert.equal(images,1);
 const violations=(await new Axe({page}).withTags(['wcag2a','wcag2aa','wcag22aa']).analyze()).violations;
 await page.screenshot({path:path.join(out,'dialog-320.png'),fullPage:true});await page.keyboard.press('Escape');assert.equal(await page.locator('dialog').isVisible(),false);assert.equal(await page.locator('[data-menu-code-open]').evaluate(e=>e===document.activeElement),true);
 await page.locator('#cuia-acessivel summary').click();await page.locator('#reading-size').selectOption('200');await page.locator('[data-menu-code-open]').click();const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2);await page.screenshot({path:path.join(out,'dialog-text200.png'),fullPage:true});
 fs.writeFileSync(path.join(out,'report.json'),JSON.stringify({images,overflow,triggerColors,violations:violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})),escape:true,focusRestored:true},null,2));console.log(JSON.stringify({overflow,violations:violations.map(v=>v.id)}));assert.equal(overflow,false);assert.equal(violations.length,0);await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
