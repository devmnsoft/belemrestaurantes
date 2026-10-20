(() => {
 'use strict';
 document.querySelectorAll('[data-menu-notice-close]').forEach(button=>button.addEventListener('click',()=>button.closest('.alert')?.remove()));
 const button=document.getElementById('btnCuiaAi');if(!button)return;
 button.addEventListener('click',async()=>{
  const name=document.getElementById('dishNameInput'),ingredients=document.getElementById('dishIngredientsInput'),description=document.getElementById('dishDescriptionInput'),status=document.getElementById('aiLoading');
  status.classList.remove('d-none');status.setAttribute('role','status');
  if(!name.value.trim()){status.textContent='Informe o nome do prato antes de consultar.';name.focus();return;}
  if(button.disabled)return;button.disabled=true;status.textContent='Consultando a Cuia IA…';
  const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),12000);
  try {
   const body=new FormData();body.append('dishName',name.value.trim());body.append('ingredients',ingredients.value.trim());body.append('__RequestVerificationToken',button.closest('form').querySelector('[name="__RequestVerificationToken"]').value);
   const response=await fetch(button.dataset.suggestUrl,{method:'POST',body,signal:controller.signal});if(!response.ok)throw Error('unavailable');
   const data=await response.json();if(!data.success||typeof data.suggestion!=='string'||!data.suggestion.trim())throw Error('empty');
   description.value=data.suggestion;status.textContent='Sugestão pronta. Revise o texto e as informações antes de salvar.';
  }catch{status.textContent='Não foi possível obter a sugestão. Você pode escrever a descrição ou tentar novamente.';}
  finally{clearTimeout(timeout);button.disabled=false;}
 });
})();
