(() => {
 'use strict';
 let serial=0;
 document.querySelectorAll('input[type="password"]').forEach(input=>{
  if(input.dataset.passwordControl)return;
  input.dataset.passwordControl='true';
  if(!input.id)input.id='password-field-'+(++serial);
  const wrapper=document.createElement('span');wrapper.className='password-field';
  input.before(wrapper);wrapper.append(input);
  const button=document.createElement('button');button.type='button';button.className='password-toggle';
  button.textContent='Mostrar senha';button.setAttribute('aria-controls',input.id);button.setAttribute('aria-pressed','false');
  button.addEventListener('click',()=>{const visible=input.type==='password';input.type=visible?'text':'password';button.textContent=visible?'Ocultar senha':'Mostrar senha';button.setAttribute('aria-pressed',String(visible));});
  wrapper.append(button);
 });
})();
