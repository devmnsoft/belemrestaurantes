const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium,webkit,devices}=require(path.resolve('artifacts/ui-test/node_modules/playwright'));const Axe=require(path.resolve('artifacts/ui-test/node_modules/@axe-core/playwright')).default;
const out='artifacts/sprint54-ui';fs.mkdirSync(out,{recursive:true});
(async()=>{const report=[];for(const [name,engine,profile]of[['desktop',chromium,{viewport:{width:1440,height:900}}],['android',chromium,devices['Pixel 7']],['ios',webkit,devices['iPhone 13']]]){
 const browser=await engine.launch();try{const {defaultBrowserType,...options}=profile;const context=await browser.newContext(options);const page=await context.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
 for(const route of ['/admin/login','/admin/forgot','/account/login','/account/register','/cuia-ia?q='+encodeURIComponent('O que era a Terra do Meio?')]){
  const r=await page.goto('http://localhost:5180'+route);assert.equal(r.status(),200);assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+2),false);
  const audit=await new Axe({page}).include('main').withTags(['wcag2a','wcag2aa']).analyze();assert.equal(audit.violations.length,0,JSON.stringify(audit.violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>n.target)}))));assert.deepEqual(errors,[]);
  if(route.startsWith('/cuia-ia?')){await page.locator('#ai-results').filter({hasText:'Terra do Meio'}).waitFor();const text=await page.locator('#ai-results').innerText();assert.match(text,/Marituba/);assert.match(text,/Altamira/);assert.doesNotMatch(text,/drael|gemini|groq|deepseek/i);await page.locator('#ai-results').screenshot({path:path.join(out,name+'-cuia.png')});}
  report.push({name,route,status:r.status(),violations:audit.violations.length});
 }
 if(name==='desktop'){
  await page.goto('http://localhost:5180/cuia-ia');const answers=[];for(const [prompt,previousQuestions]of [['O que era a Terra do Meio?',[]],['Quero saber mais sobre Terra do Meio em Marituba',['O que era a Terra do Meio?']]]){
   const answer=await page.evaluate(async args=>{const token=document.querySelector('input[name="__RequestVerificationToken"]').value;const r=await fetch('/api/cuia-ia/ask',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':token},body:JSON.stringify({...args,searchWeb:true})});return{httpStatus:r.status,...await r.json()};},{prompt,previousQuestions});assert.equal(answer.httpStatus,200);assert.equal(answer.engineUsed,'Cuia');assert.match(answer.answerMarkdown,/Marituba/);assert.doesNotMatch(JSON.stringify(answer),/drael|gemini|groq|deepseek/i);answers.push(answer);
  }assert.doesNotMatch(answers[1].answerMarkdown,/Altamira/);fs.writeFileSync(path.join(out,'conversation.json'),JSON.stringify(answers,null,2));
  assert.equal((await page.request.get('http://localhost:5180/dev/caixa-de-entrada')).status(),200);assert.equal((await page.request.get('http://localhost:5180/dev/caixa-de-entrada',{headers:{Host:'evil.example'}})).status(),404);
 }
 await context.close();}finally{await browser.close();}}
fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({cases:report.length,failures:report.filter(r=>r.violations)}));})().catch(e=>{console.error(e);process.exit(1)});
