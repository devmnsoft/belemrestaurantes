const fs=require('node:fs'),path=require('node:path');
const {chromium}=require(path.resolve('artifacts/ui-test/node_modules/playwright'));
const AxeBuilder=require(path.resolve('artifacts/ui-test/node_modules/@axe-core/playwright')).default;
const base=process.env.VISUAL_TEST_BASE_URL||'http://localhost:5180',out=process.env.MAP_OUTPUT||'artifacts/sprint44/audit';
(async()=>{
 fs.mkdirSync(out,{recursive:true});const browser=await chromium.launch(),results=[];
 const routes=(process.env.AUDIT_ROUTES||'/,/busca,/mapa,/cidades,/categorias,/cuia-rotas,/cuia-ia,/receitas,/community,/roteiros,/account/login,/account/register,/admin/login,/establishment/174').split(',');
 for(const width of (process.env.AUDIT_WIDTHS||'320,360,390,768,1024,1440').split(',').map(Number)){
  const context=await browser.newContext({viewport:{width,height:900}}),page=await context.newPage();
  if(process.env.AUDIT_TEXT_SIZE)await page.addInitScript(size=>localStorage.setItem('belem-reading',JSON.stringify({size})),process.env.AUDIT_TEXT_SIZE);
  if(process.env.AUDIT_SNAPSHOT_DIR)for(const route of routes){const file=path.join(process.env.AUDIT_SNAPSHOT_DIR,route.slice(1).replaceAll('/','-')+'.html');if(fs.existsSync(file))await page.route(base+route,r=>r.fulfill({contentType:'text/html; charset=utf-8',body:fs.readFileSync(file,'utf8')}));}
  for(const route of routes){
   const errors=[];page.removeAllListeners('pageerror');page.on('pageerror',e=>errors.push(e.message));
   let response=await page.goto(base+route);for(let attempt=0;response.status()===429&&attempt<2;attempt++){await page.waitForTimeout(60000);response=await page.goto(base+route);}await page.waitForTimeout(180);
   const axe=width===320?(await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations:[];
   const rendered=await page.evaluate(()=>({overflow:document.documentElement.scrollWidth>innerWidth+2,small:[...document.querySelectorAll('main p,main label,main input,main button,main td,main small')].filter(e=>e.getBoundingClientRect().height&&parseFloat(getComputedStyle(e).fontSize)<16).map(e=>({tag:e.tagName,class:e.className,size:getComputedStyle(e).fontSize})).slice(0,12)}));
   results.push({route,width,status:response.status(),errors,...rendered,violations:axe.map(v=>({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))}))});
   if(width===320||width===1440)await page.screenshot({path:path.join(out,route.replace(/[^a-zA-Z0-9_-]/g,'_')+'-'+width+'.png'),fullPage:true});
  }
  await context.close();
 }
 fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(results,null,2));
 const failures=results.filter(r=>r.status>=400||r.overflow||r.errors.length||r.violations.length||r.small.length);console.log(JSON.stringify(failures,null,2));if(failures.length)process.exitCode=1;await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
