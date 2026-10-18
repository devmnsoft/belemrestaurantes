const fs=require('node:fs'),path=require('node:path');
const modules=process.env.VISUAL_TEST_MODULES||path.resolve('artifacts/ui-test/node_modules');
const {chromium}=require(path.join(modules,'playwright'));
const AxeBuilder=require(path.join(modules,'@axe-core/playwright')).default;
const base=process.env.VISUAL_TEST_BASE_URL||'http://localhost:5180';
const output=path.resolve(process.env.VISUAL_TEST_OUTPUT||'artifacts/sprint19/recipes');
const snapshots=path.resolve(process.env.VISUAL_TEST_SNAPSHOTS||'artifacts/sprint19/snapshots');
let browser;
(async()=>{
 fs.mkdirSync(output,{recursive:true});browser=await chromium.launch({headless:true});const results=[];
 for(const width of [360,390,768,1024,1440]){
  const context=await browser.newContext({viewport:{width,height:1000}}),page=await context.newPage();
  await page.goto(base+'/receitas',{waitUntil:'networkidle'});await page.waitForTimeout(1300);
  await page.locator('#recipe-query').fill('cupuacu');await page.locator('input[name=Quick]').check();await page.locator('.recipe-filters button').click();await page.waitForURL('**/receitas?**');
  const titles=await page.locator('.recipe-card h3').allTextContents();if(titles.length!==1||titles[0]!=='Suco de cupuaçu')throw Error('Ingredient search, accent tolerance or total waiting time filter failed');
  await page.waitForTimeout(1300);await page.goto(base+'/receitas/tacaca-em-casa',{waitUntil:'networkidle'});
  const schema=JSON.parse(await page.locator('script[type="application/ld+json"]').textContent());
  if(schema['@type']!=='Recipe'||!schema.recipeIngredient.length||schema.aggregateRating)throw Error('Recipe structured data does not match editorial content');
  if(await page.locator('iframe').count())throw Error('External recipe media must not load before a user decision');
  await page.locator('[data-recipe-check=ingredient]').first().check();await page.locator('[data-recipe-check=step]').first().check();
  if(!(await page.locator('#recipe-progress').innerText()).startsWith('1 de '))throw Error('Cooking checklist progress failed');
  await page.locator('[data-recipe-kitchen]').click();if(await page.locator('[data-recipe-kitchen]').getAttribute('aria-pressed')!=='true')throw Error('Kitchen mode failed');
  await page.keyboard.press('Escape');if(await page.locator('[data-recipe-kitchen]').getAttribute('aria-pressed')!=='false')throw Error('Kitchen mode Escape failed');
  await page.locator('[data-recipe-reset]').click();if(await page.locator('[data-recipe-check]:checked').count())throw Error('Checklist reset failed');
  await page.evaluate(()=>{window.print=()=>{window.__recipePrinted=true;};});await page.locator('[data-recipe-print]').click();if(!await page.evaluate(()=>window.__recipePrinted))throw Error('Print action failed');
  const violations=(await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}));
  const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2);if(violations.length||overflow)throw Error(JSON.stringify({width,overflow,violations}));
  await page.screenshot({path:path.join(output,'recipe-'+width+'.png'),fullPage:true});
  await page.emulateMedia({media:'print'});if(await page.locator('.recipe-tools').isVisible())throw Error('Print layout exposes interactive controls');
  results.push({width,search:true,structuredData:true,checklist:true,kitchenMode:true,print:true,externalMedia:'link-only',violations:0});await context.close();
 }
 for(const width of [360,1440]){
  const context=await browser.newContext({viewport:{width,height:1000}}),page=await context.newPage();
  const html=fs.readFileSync(path.join(snapshots,'admin-receitas.html'),'utf8');await page.route(base+'/admin/receitas',route=>route.fulfill({status:200,contentType:'text/html; charset=utf-8',body:html}));
  await page.goto(base+'/admin/receitas',{waitUntil:'networkidle'});const menu=page.locator('.admin-menu-disclosure');
  if(width===360){if(await menu.getAttribute('open')!==null)throw Error('Mobile admin navigation should start collapsed');await menu.locator('summary').focus();await page.keyboard.press('Enter');if(await menu.getAttribute('open')===null)throw Error('Keyboard cannot open admin navigation');await page.keyboard.press('Enter');}
  else if(await menu.getAttribute('open')===null)throw Error('Desktop admin navigation must remain available');
  results.push({width,adminMenuKeyboard:true});await context.close();
 }
 fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(results,null,2));console.log(JSON.stringify({cases:results.length,failures:0}));await browser.close();
})().catch(async error=>{console.error(error);await browser?.close();process.exitCode=1});
