const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require(path.resolve('artifacts/ui-test/node_modules/playwright'));
const Axe=require(path.resolve('artifacts/ui-test/node_modules/@axe-core/playwright')).default;
(async()=>{
 const root='artifacts/sprint48/ui';fs.mkdirSync(root,{recursive:true});const browser=await chromium.launch(),cases=[];
 try{
  for(const width of [320,390,768,1440]){
   const context=await browser.newContext({viewport:{width,height:900}}),page=await context.newPage();let errors=[];
   page.on('pageerror',e=>errors.push(e.message));
   async function inspect(name){
    const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2);
    const violations=(await new Axe({page}).withTags(['wcag2a','wcag2aa','wcag22aa']).analyze()).violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))}));
    await page.screenshot({path:path.join(root,name+'-'+width+'.png')});cases.push({name,width,overflow,violations,errors:[...errors]});errors=[];
    fs.writeFileSync(path.join(root,'report.json'),JSON.stringify(cases,null,2));
   }
   for(const route of ['/account/login','/admin/login','/account/register','/account/forgot','/account/reset']){
    assert.equal((await page.goto('http://localhost:5180'+route)).status(),200);
    const input=page.locator('input[type=password]').first();
    if(await input.count()){
     const value='SenhaDeInterface1!';await input.fill(value);const id=await input.getAttribute('id'),stable=page.locator('[id="'+id+'"]'),toggle=page.locator('button.password-toggle').first();
     assert.equal(await toggle.getAttribute('aria-controls'),id);await toggle.click();assert.equal(await stable.getAttribute('type'),'text');assert.equal(await stable.inputValue(),value);assert.equal(await toggle.getAttribute('aria-pressed'),'true');await toggle.click();assert.equal(await stable.getAttribute('type'),'password');
    }
    await inspect(route.replaceAll('/','_'));
   }
   await page.goto('http://localhost:5180/account/register');await page.locator('[name=FullName]').fill('Teste de interface');await page.locator('[name=Email]').fill('s48-ui-'+Date.now()+'@example.invalid');await page.locator('[name=Cpf]').fill('11111111111');
   await page.locator('[name=Password]').fill('InterfaceTemporaria1!');await page.locator('[name=ConfirmPassword]').fill('InterfaceTemporaria1!');await page.locator('[name=AcceptTerms][type=checkbox]').check();
   await page.locator('form[action="/account/register"] button[type=submit]').click();await page.getByText('CPF inválido. Confira os 11 dígitos do documento.',{exact:true}).waitFor();await inspect('invalid-cpf');
   await page.goto('http://localhost:5180/cuia-ia');await page.locator('#ai-search-web').check();await page.locator('#ai-prompt').fill('Me diga os pratos do restaurante Na Maré');
   const reply=page.waitForResponse(r=>r.url().includes('/api/cuia-ia/ask')&&r.request().method()==='POST');await page.locator('#ai-submit').click();const payload=await(await reply).json();assert.equal(payload.generationStatus,'reference_answer');assert.equal(payload.searchStatus,'indexed_sources');
   await page.getByText('Égua mano',{exact:false}).first().waitFor();await page.locator('#ai-results').scrollIntoViewIfNeeded();await inspect('external-menu');assert.ok(await page.locator('a[href*="veja.abril.com.br"]').count());
   await page.locator('.cuia-external-card [data-save-kind=external]').first().click();await page.goto('http://localhost:5180/meu-passeio');await page.locator('.discovery-stop').waitFor();assert.ok((await page.locator('.discovery-stop').innerText()).includes('Na Maré'));await inspect('saved-external');await page.locator('[data-discovery-clear]').click();
   await page.goto('http://localhost:5180/');await page.locator('.cuia-widget-toggle').click();await page.locator('#cuia-widget-prompt').fill('Me diga os pratos do restaurante Na Maré');await page.locator('#cuia-widget-web').check();await page.locator('.cuia-widget-form button[type=submit]').click();await page.locator('#cuia-widget-panel a[href*="veja.abril.com.br"]').first().waitFor();await inspect('widget-sources');await page.keyboard.press('Escape');
   await page.goto('http://localhost:5180/descobrir');await page.locator('.discovery-photo-grid').scrollIntoViewIfNeeded();await page.waitForTimeout(500);assert.equal(await page.locator('.discovery-photo-grid figure').count(),6);await inspect('real-photo-stories');
   if(width===320){await page.evaluate(()=>localStorage.setItem('belem-reading',JSON.stringify({size:'200'})));await page.goto('http://localhost:5180/account/register');await inspect('register-text200');await page.goto('http://localhost:5180/cuia-ia?q='+encodeURIComponent('Me diga os pratos do restaurante Na Maré'));await page.locator('#ai-results').scrollIntoViewIfNeeded();await inspect('menu-text200');}
   await context.close();
  }
  const failures=cases.filter(c=>c.overflow||c.violations.length||c.errors.length);console.log(JSON.stringify({cases:cases.length,failures},null,2));assert.equal(failures.length,0);
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
