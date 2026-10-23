const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require(path.resolve('artifacts/ui-test/node_modules/playwright'));
const Axe=require(path.resolve('artifacts/ui-test/node_modules/@axe-core/playwright')).default;
const base=process.env.VISUAL_TEST_BASE_URL||'http://localhost:5180',out='artifacts/sprint49/ui';
fs.mkdirSync(out,{recursive:true});
(async()=>{
 const browser=await chromium.launch(),cases=[];let errors=[];
 async function inspect(page,name,width){
  const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2);
  const violations=(await new Axe({page}).withTags(['wcag2a','wcag2aa','wcag22aa']).analyze()).violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}));
  cases.push({name,width,overflow,violations,errors:[...errors]});errors=[];
  fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(cases,null,2));
  await page.screenshot({path:path.join(out,name+'-'+width+'.png')});
  assert.equal(overflow,false);assert.deepEqual(violations,[]);assert.deepEqual(cases.at(-1).errors,[]);
 }
 try{
  for(const width of [320,390,768,1440]){
   const context=await browser.newContext({viewport:{width,height:900}}),page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));
   await page.goto(base+'/account/register');await inspect(page,'register',width);
   if(width===320){
    await page.locator('[name=FullName]').fill('Synthetic Browser');await page.locator('[name=Email]').fill('fixture-'+Date.now()+'@example.invalid');await page.locator('[name=Cpf]').fill('00000000000');
    await page.locator('[name=Password]').fill('ReliableTest123!');await page.locator('[name=ConfirmPassword]').fill('DifferentTest123!');await page.locator('[name=AcceptTerms][type=checkbox]').check();
    await page.locator('form[action="/account/register"] button[type=submit]').click();await page.locator('.validation-summary-errors').waitFor();
    assert.ok((await page.locator('.validation-summary-errors').textContent()).includes('CPF inválido'));
    assert.ok((await page.locator('[data-valmsg-for=ConfirmPassword]').textContent()).includes('igual'));
    assert.equal(await page.locator('[name=Password]').inputValue(),'');assert.equal(await page.locator('[name=FullName]').inputValue(),'Synthetic Browser');
    assert.equal(await page.locator('[data-registration-errors]').evaluate(e=>e===document.activeElement),true);
    await inspect(page,'register-errors',width);
    await page.locator('#cuia-acessivel summary').click();await page.locator('#reading-size').selectOption('200');await page.locator('#cuia-acessivel summary').click();
    assert.equal(await page.locator('[name=ConfirmPassword]').getAttribute('aria-invalid'),'true');
    assert.ok((await page.locator('[name=ConfirmPassword]').getAttribute('aria-describedby')).includes('registration-error-ConfirmPassword'));
    await inspect(page,'register-errors-text200',width);
    await page.locator('#cuia-acessivel summary').click();await page.locator('#reading-size').selectOption('100');await page.locator('#cuia-acessivel summary').click();
   }
   await page.goto(base+'/descobrir');for(const img of await page.locator('.discovery-photo-grid img').all()){await img.scrollIntoViewIfNeeded();await img.evaluate(e=>e.decode());}
   await inspect(page,'photo-stories',width);
   if(width===320){await page.evaluate(()=>localStorage.setItem('belem-reading',JSON.stringify({size:'200'})));await page.reload();await inspect(page,'photos-text200',width);await page.evaluate(()=>localStorage.removeItem('belem-reading'));}
   await context.close();
  }
  // Same tab: state corruption and blocked storage must not prevent the next question.
  for(const widget of [false,true]){
   const context=await browser.newContext({viewport:{width:390,height:844}}),page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));
   await page.addInitScript(()=>{sessionStorage.setItem('cuia-questions','{}');sessionStorage.setItem('belem-cuia-places','{}');Storage.prototype.setItem=()=>{throw new DOMException('Blocked','SecurityError')};});
   let mode='ok',calls=[],held;
   await page.route('**/api/cuia-ia/ask',async route=>{
    calls.push(route.request().postDataJSON());
    if(mode==='timeout'){held=route;return;}
    if(mode==='network')return route.abort();
    if(mode==='invalid')return route.fulfill({contentType:'application/json',body:'{"recommendations":{}}'});
    if(mode==='badjson')return route.fulfill({contentType:'application/json',body:'{'});
    if(mode==='429')return route.fulfill({status:429,contentType:'application/json',body:JSON.stringify({code:'rate_limited',retryable:true})});
    return route.fulfill({contentType:'application/json',body:JSON.stringify({answerMarkdown:'Resposta de teste '+calls.length,answerKind:'servico',generationStatus:'ok',suggestedFollowUps:['Outra opção?']})});
   });
   await page.goto(base+(widget?'/':'/cuia-ia'));if(widget)await page.locator('.cuia-widget-toggle').click();
   const prompt=page.locator(widget?'#cuia-widget-prompt':'#ai-prompt'),button=page.locator(widget?'.cuia-widget-submit':'#ai-submit'),result=page.locator(widget?'.cuia-widget-results':'#ai-results');
   for(let i=0;i<4;i++){await prompt.fill(i===1?'Uma opção mais barata':'Onde comer em Belém? '+i);await button.click();await result.getByText('Resposta de teste '+(i+1),{exact:true}).waitFor();assert.equal(await button.isDisabled(),false);}
   assert.equal(calls[1].previousQuestions.length,1);assert.equal(calls[3].previousQuestions.length,3);
   for(const failure of ['429','invalid','badjson','network']){
    mode=failure;await prompt.fill('Quero conhecer a culinária do Pará');await button.click();await result.locator('[role=alert]').waitFor();assert.equal(await button.isDisabled(),false);
    mode='ok';await button.click();await result.getByText('Resposta de teste '+calls.length,{exact:true}).waitFor();
   }
   await page.clock.install();mode='timeout';await button.click();await page.waitForFunction(()=>document.querySelector('.cuia-widget-submit:disabled,#ai-submit:disabled')!==null);await page.clock.fastForward(51000);await result.locator('[role=alert]').waitFor();assert.equal(await button.isDisabled(),false);await held?.abort().catch(()=>{});
   mode='ok';await button.click();await result.getByText('Resposta de teste '+calls.length,{exact:true}).waitFor();
   if(!widget){await page.locator('#cuia-clear-session').click();await button.click();await result.getByText('Resposta de teste '+calls.length,{exact:true}).waitFor();assert.equal(calls.at(-1).previousQuestions.length,0);}
   await inspect(page,widget?'widget-multi-turn':'cuia-multi-turn',390);await context.close();
  }
  const context=await browser.newContext({viewport:{width:390,height:844}}),page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));
  await page.goto(base+'/');await page.route('https://example.invalid/**',r=>r.fulfill({status:404,body:''}));
  await page.evaluate(()=>{const img=new Image();img.width=320;img.height=220;img.alt='Fotografia de teste';img.src='https://example.invalid/broken.webp';document.querySelector('main').prepend(img);});
  await page.locator('img[data-image-fallback]').evaluate(e=>e.decode());assert.equal(await page.locator('img[data-image-fallback]').getAttribute('src'),'/images/image-unavailable.svg');
  await inspect(page,'image-fallback',390);
  for(const route of ['/favicon.ico','/favicon.svg','/images/image-unavailable.svg']){const response=await context.request.get(base+route);assert.equal(response.status(),200);assert.ok(!response.headers()['content-type'].includes('text/html'));}
  assert.equal((await context.request.get(base+'/images/missing49.webp',{maxRedirects:0})).status(),404);
  // Actual reviewed external reference, no provider simulation on these two requests.
  await page.goto(base+'/cuia-ia');await page.locator('#ai-prompt').fill('Me diga os pratos do restaurante Na Maré');await page.locator('#ai-submit').click();await page.locator('#ai-results').getByText('Égua mano',{exact:false}).first().waitFor();
  await page.locator('#ai-prompt').fill('E como posso confirmar o cardápio atual?');await page.locator('#ai-submit').click();await page.locator('#ai-results').getByText('canal oficial',{exact:false}).first().waitFor();assert.ok((await page.locator('#ai-results').textContent()).includes('Na Maré'));await inspect(page,'real-menu-continuation',390);
  await context.close();console.log(JSON.stringify({cases:cases.length,failures:cases.filter(c=>c.overflow||c.violations.length||c.errors.length)}));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1)});
