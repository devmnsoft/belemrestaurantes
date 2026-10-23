// Backend provider replies are mocked here; this verifies the real page and JavaScript contract.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require(path.resolve('artifacts/ui-test/node_modules/playwright'));
const Axe=require(path.resolve('artifacts/ui-test/node_modules/@axe-core/playwright')).default;
(async()=>{
 const out='artifacts/sprint46/search-ui';fs.mkdirSync(out,{recursive:true});const browser=await chromium.launch(),cases=[];
 for(const width of [320,390,768,1440]){
  const context=await browser.newContext({viewport:{width,height:900}}),page=await context.newPage(),requests=[];
  await page.goto('http://localhost:5180/cuia-ia');
  assert.equal(await page.locator('#ai-search-web').isChecked(),false);
  await page.route('**/api/cuia-ia/ask',r=>{
   const body=r.request().postDataJSON();requests.push(body);
   return r.fulfill({contentType:'application/json',body:JSON.stringify({answerMarkdown:'Informação de teste, confirme com a fonte [1].',answerKind:body.searchWeb?'pesquisa':'catalogo',recommendations:[],directoryMatches:[],recipes:[],regionalCuriosities:[],suggestedFollowUps:[],searchStatus:body.searchWeb?'ok':'not_requested',sources:body.searchWeb?[{title:'Fonte externa de teste',url:'https://example.org/fonte',retrievedAt:'2026-10-20T12:00:00Z'}]:[]})});
  });
  await page.locator('#ai-prompt').fill('Explique o tacacá');await page.locator('#ai-submit').click();
  await page.getByText('Informação de teste, confirme com a fonte [1].',{exact:true}).waitFor();
  assert.equal(requests[0].searchWeb,false);
  await page.locator('#ai-search-web').check();await page.locator('#ai-prompt').fill('Qual ônibus para chegar?');await page.locator('#ai-submit').click();
  await page.getByRole('link',{name:'Fonte externa de teste'}).waitFor();assert.equal(requests[1].searchWeb,true);
  assert.equal(await page.getByRole('link',{name:'Fonte externa de teste'}).getAttribute('href'),'https://example.org/fonte');
  const violations=width===320?(await new Axe({page}).withTags(['wcag2a','wcag2aa','wcag22aa']).analyze()).violations:[];
  await page.locator('#cuia-acessivel summary').click();await page.locator('#reading-size').selectOption('200');
  const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2);
  await page.screenshot({path:path.join(out,width+'.png'),fullPage:true});
  cases.push({width,overflow,violations:violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>n.target)})),requestFlags:requests.map(r=>r.searchWeb)});
  assert.equal(overflow,false);assert.equal(violations.length,0);await context.close();
 }
 fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(cases,null,2));await browser.close();console.log(JSON.stringify(cases));
})().catch(e=>{console.error(e);process.exit(1)});
