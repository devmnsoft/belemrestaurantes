const fs = require('node:fs');
const path = require('node:path');
const moduleRoot = process.env.VISUAL_TEST_MODULES || path.resolve('artifacts/ui-test/node_modules');
const { chromium } = require(path.join(moduleRoot, 'playwright'));
const AxeBuilder = require(path.join(moduleRoot, '@axe-core/playwright')).default;
const base = process.env.VISUAL_TEST_BASE_URL || 'http://localhost:5180';
const output = path.resolve(process.env.VISUAL_TEST_OUTPUT || 'artifacts/test-results/visual');
const onlyAuth = process.env.VISUAL_TEST_SCOPE === 'authenticated';
const changedOnly = process.env.VISUAL_TEST_SCOPE === 'changed';
const finalChanges = process.env.VISUAL_TEST_SCOPE === 'final-changes';
const coverageOnly = process.env.VISUAL_TEST_SCOPE === 'coverage';
const routes = onlyAuth || coverageOnly ? [] : finalChanges ? ['/','/cuia-rotas','/privacidade','/termos'] : changedOnly ? ['/roteiros'] : ['/', '/busca', '/cidades', '/guia', '/community', '/roteiros', '/receitas', '/selo-cuia', '/account/login', '/account/register', '/account/forgot', '/admin/login', '/sugerir', '/cuia-rotas', '/qual-onibus', '/sabores','/privacidade','/termos'];
const widths = [360, 390, 768, 1024, 1440];
let browser;
(async () => {
  fs.mkdirSync(output, {recursive:true});
  browser = await chromium.launch({headless:true});
  const previous = path.join(output, 'report.json');
  const results = (onlyAuth || changedOnly || finalChanges || coverageOnly) && fs.existsSync(previous) ? JSON.parse(fs.readFileSync(previous, 'utf8')).filter(result => coverageOnly ? result.route !== '/admin/cobertura' : finalChanges ? !routes.includes(result.route) && !result.mode : changedOnly ? result.route !== '/roteiros' && result.mode !== 'controlled-fixture-snapshot' : !result.mode) : [];
  for (const width of widths) {
    const context = await browser.newContext({viewport:{width,height:1000}});
    const page = await context.newPage();
    for (const route of routes) {
      const errors = [];
      const handler = error => errors.push(error.message);
      page.on('pageerror', handler);
      const response = await page.goto(base+route, {waitUntil:'networkidle'});
      // Fluxos de conta têm limite próprio. Espaçar os casos preserva as proteções reais.
      await page.waitForTimeout(1300);
      const overflow = await page.evaluate(() => ({width:window.innerWidth,scroll:document.documentElement.scrollWidth,
        elements:[...document.querySelectorAll('main *')].filter(e=>{const r=e.getBoundingClientRect();return r.width && r.right>innerWidth+2}).slice(0,8).map(e=>({tag:e.tagName,cls:e.className}))}));
      const violations = width===360 ? (await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21a','wcag21aa','wcag22aa']).analyze()).violations.map(v=>({id:v.id,impact:v.impact,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})) : [];
      const name = (route==='/'?'home':route.replaceAll('/','-').slice(1))+'-'+width;
      await page.screenshot({path:path.join(output,name+'.png'),fullPage:true});
      results.push({route,width,status:response.status(),overflow,errors,violations});
      page.off('pageerror',handler);
    }
    if (width===390 && routes.includes('/')) {
      await page.goto(base, {waitUntil:'networkidle'});
      const menu=page.getByRole('button',{name:'Menu'});
      await menu.click();
      if (await menu.getAttribute('aria-expanded')!=='true') throw new Error('Mobile menu failed');
      await page.keyboard.press('Escape');
      if (await menu.getAttribute('aria-expanded')!=='false') throw new Error('Mobile menu Escape failed');
      await page.route('**/api/search/autocomplete*', route=>route.fulfill({json:[{url:'/busca?q=teste',title:'<img src=x onerror=alert(1)>',subtitle:'Texto seguro'},{url:'javascript:alert(1)',title:'Unsafe',subtitle:''}]}));
      await page.locator('#heroQuery').fill('teste');
      await page.locator('#heroAutocomplete a').waitFor();
      if (await page.locator('#heroAutocomplete img').count()!==0 || await page.locator('#heroAutocomplete a').count()!==1) throw new Error('Unsafe autocomplete rendering');
      await page.keyboard.press('ArrowDown');
      if (await page.locator('#heroAutocomplete a').evaluate(e=>e!==document.activeElement)) throw new Error('Autocomplete keyboard failed');
    }
    const snapshots = path.resolve(process.env.VISUAL_TEST_SNAPSHOTS || 'artifacts/test-results/admin-snapshots');
    for (const route of changedOnly ? [] : coverageOnly ? ['/admin/cobertura'] : ['/admin/discovery','/admin/crawler','/admin/discovery360','/admin/suggestions','/account','/account/favorites','/partner/dashboard','/admin/moderacao','/admin/operacao','/admin/mobilidade','/admin/editorial','/admin/usuarios','/admin/cobertura']) {
      const filename = path.join(snapshots, route.replaceAll('/','-').slice(1)+'.html');
      if (!fs.existsSync(filename)) continue;
      const html = fs.readFileSync(filename, 'utf8');
      await page.route(base+route, request => request.fulfill({status:200,contentType:'text/html; charset=utf-8',body:html}));
      const errors=[]; const handler=error=>errors.push(error.message); page.on('pageerror',handler);
      await page.goto(base+route,{waitUntil:'networkidle'});
      const overflow=await page.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth}));
      if(route==='/admin/cobertura') {
        const canScroll=await page.locator('.coverage-table-wrap').evaluate(element=>{
          element.scrollLeft=24;const result=element.scrollWidth<=element.clientWidth+2 || element.scrollLeft>0;element.scrollLeft=0;return result;
        });
        if(!canScroll)throw new Error('Coverage table must remain scrollable within its container');
      }
      const violations=width===360 ? (await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21a','wcag21aa','wcag22aa']).analyze()).violations.map(v=>({id:v.id,impact:v.impact,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})) : [];
      await page.screenshot({path:path.join(output,route.replaceAll('/','-').slice(1)+'-'+width+'.png'),fullPage:true});
      results.push({route,width,status:200,mode:'test-authenticated-snapshot',overflow,errors,violations});
      page.off('pageerror',handler);
    }
    for (const [name, route] of coverageOnly ? [] : [['restaurant-sample','/__visual/restaurant'],['catalog-sample','/__visual/catalog'],['visit-destination','/__visual/visit-destination'],['visit-fallback','/__visual/visit-fallback'],['admin-hours-edit','/__visual/admin-hours-edit']]) {
      const filename=path.resolve(process.env.VISUAL_TEST_SNAPSHOTS || 'artifacts/test-results/admin-snapshots',name+'.html');
      if (!fs.existsSync(filename)) continue;
      await page.route(base+route,request=>request.fulfill({status:200,contentType:'text/html; charset=utf-8',body:fs.readFileSync(filename,'utf8')}));
      const errors=[];const handler=error=>errors.push(error.message);page.on('pageerror',handler);
      await page.goto(base+route,{waitUntil:'networkidle'});
      const overflow=await page.evaluate(()=>({width:innerWidth,scroll:document.documentElement.scrollWidth}));
      const violations=width===360 ? (await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21a','wcag21aa','wcag22aa']).analyze()).violations.map(v=>({id:v.id,impact:v.impact,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})) : [];
      await page.screenshot({path:path.join(output,name+'-'+width+'.png'),fullPage:true});
      results.push({route:name,width,status:200,mode:'controlled-fixture-snapshot',overflow,errors,violations});
      page.off('pageerror',handler);
    }
    await context.close();
  }
  await browser.close();
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(results,null,2));
  const failures=results.filter(r=>r.status!==200 || r.overflow.scroll>r.width+2 || r.errors.length || r.violations.length);
  console.log(JSON.stringify({cases:results.length,failures:failures.map(f=>({route:f.route,width:f.width,status:f.status,overflow:f.overflow.scroll,errors:f.errors,violations:f.violations}))},null,2));
  if (failures.length) process.exitCode=1;
})().catch(async error=>{console.error(error);await browser?.close();process.exitCode=1});
