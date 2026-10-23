(() => {
 'use strict';
 for(const [buttonId,targetId,field,path] of [['btn-ask-sommelier','ai-restaurant-advice','advice','/api/cuia-ia/dica-restaurante/'],['btn-recipe-ai','ai-recipe-substitutions','tips','/api/cuia-ia/dica-receita/']]){
  const button=document.getElementById(buttonId),target=document.getElementById(targetId);if(!button||!target)continue;
  target.setAttribute('aria-live','polite');
  button.addEventListener('click',async()=>{
   const identity=button.dataset.restaurantId||button.dataset.recipeSlug;if(!identity||button.disabled)return;
   button.disabled=true;target.setAttribute('aria-busy','true');button.textContent='Carregando orientação local…';
   const controller=new AbortController(),timer=setTimeout(()=>controller.abort(),8000);
   try{const response=await fetch(path+encodeURIComponent(identity),{signal:controller.signal});if(!response.ok)throw Error('http');const data=await response.json();if(typeof data[field]!=='string')throw Error('schema');
    const note=document.createElement('p');note.className='cuia-advice-response';note.textContent='Orientação local do portal, sem geração por IA nesta consulta.\n\n'+data[field];target.replaceChildren(note);button.textContent='Atualizar orientação local';
   }catch{const note=document.createElement('p');note.className='cuia-advice-error';note.setAttribute('role','alert');note.textContent='Não foi possível carregar agora. Tente novamente.';target.replaceChildren(note);button.textContent='Tentar carregar novamente';}
   finally{clearTimeout(timer);button.disabled=false;target.removeAttribute('aria-busy');}
  });
 }
})();
