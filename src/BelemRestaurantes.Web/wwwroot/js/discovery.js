(() => {
 'use strict';
 const key='belem-discoveries-v1',max=20,escape=window.CuiaUi.escape;
 const safeUrl=window.CuiaUi.url;
 const valid=p=>p&&typeof p.name==='string'&&p.name.length>0&&p.name.length<=160&&
   (p.kind==='catalog'&&Number.isSafeInteger(p.id)&&p.id>0||p.kind==='external'&&typeof p.url==='string'&&p.url.length<=2000&&safeUrl(p.url));
 const identity=p=>p.kind==='catalog'?'catalog:'+p.id:'external:'+p.url+'|'+p.name;
 function read(){try{const data=JSON.parse(localStorage.getItem(key)||'[]');return Array.isArray(data)?data.filter(valid).slice(0,max):[];}catch{return [];}}
 function write(items){try{localStorage.setItem(key,JSON.stringify(items));window.dispatchEvent(new Event('discovery-changed'));return true;}catch{return false;}}
 function mark(){const saved=new Set(read().map(identity));document.querySelectorAll('[data-save-kind]').forEach(button=>{
   const p=fromButton(button),selected=valid(p)&&saved.has(identity(p));button.setAttribute('aria-pressed',String(selected));button.textContent=selected?'Guardado neste navegador':'Guardar para depois';
 });}
 function fromButton(button){return {kind:button.dataset.saveKind,id:Number(button.dataset.saveId),name:(button.dataset.saveName||'').slice(0,160),city:(button.dataset.saveCity||'').slice(0,100),url:button.dataset.saveUrl};}
 function feedback(button,message){let note=button.parentElement.querySelector('[data-save-feedback]');if(!note){note=document.createElement('p');note.dataset.saveFeedback='';note.setAttribute('role','status');button.after(note);}note.textContent=message;}
 document.addEventListener('click',event=>{
   const button=event.target.closest('[data-save-kind]');if(!button)return;
   const p=fromButton(button);if(!valid(p)){feedback(button,'Esta opção não pôde ser guardada. Abra a ficha ou a fonte.');return;}
   const items=read(),existing=items.findIndex(x=>identity(x)===identity(p));
   if(existing>=0)items.splice(existing,1);else{if(items.length>=max){feedback(button,'Você já guardou vinte opções. Remova uma em Meu passeio para continuar.');return;}items.push(p);}
   if(!write(items)){feedback(button,'O navegador não permitiu guardar. Verifique as permissões de armazenamento e tente novamente.');return;}
   feedback(button,existing>=0?'Opção removida deste navegador.':'Guardado. Organize a visita em Meu passeio.');mark();
 });
 document.addEventListener('click',event=>{
  const button=event.target.closest('[data-save-trail]');if(!button)return;
  try{
   const steps=JSON.parse(document.getElementById('trail-steps').textContent),items=read(),known=new Set(items.map(identity));
   if(!Array.isArray(steps)||!steps.every(valid))throw Error('invalid');
   const fresh=steps.filter(p=>!known.has(identity(p)));
   if(items.length+fresh.length>max){feedback(button,'O passeio comporta vinte etapas. Remova algumas antes de adicionar este roteiro.');return;}
   if(!write([...items,...fresh]))throw Error('storage');
   feedback(button,fresh.length?'Etapas guardadas. Você pode mudar a ordem ou remover lugares em Meu passeio.':'As etapas deste roteiro já estão guardadas.');mark();
  }catch{feedback(button,'Não foi possível guardar o roteiro neste navegador. Você pode consultar as etapas nesta página.');}
 });
 window.addEventListener('discovery-changed',mark);window.addEventListener('storage',event=>{if(event.key===key){mark();render();}});
 window.addEventListener('cuia-rendered',mark);mark();
 const list=document.getElementById('discovery-saved-list'),status=document.getElementById('discovery-status'),retry=document.querySelector('[data-discovery-retry]');
 let run=0,active,cacheKey='',cached,cacheTime=0;
 async function render(focusKey){
   if(!list)return;const id=++run;active?.abort();active=new AbortController();const items=read();
   status.textContent=items.length?'Carregando as fichas publicadas…':'Seu passeio está começando. Guarde uma opção em Descobrir ou na Cuia.';retry.hidden=true;
   const ids=items.filter(p=>p.kind==='catalog').map(p=>p.id).sort((a,b)=>a-b).join(',');let published=new Map();
   try{
    if(ids){let data=cached&&cacheKey===ids&&Date.now()-cacheTime<30000?cached:null;
     if(!data){const controller=active,timeout=setTimeout(()=>controller.abort(),8000);try{const response=await fetch('/api/descobrir/guardados?'+new URLSearchParams({ids}),{signal:controller.signal,cache:'no-store'});if(!response.ok)throw Error('fetch');data=await response.json();}finally{clearTimeout(timeout);}if(id!==run)return;cacheKey=ids;cached=data;cacheTime=Date.now();}
     if(!Array.isArray(data.items))throw Error('schema');published=new Map(data.items.map(p=>[p.id,p]));
    }
    if(id!==run)return;list.replaceChildren();
    items.forEach((p,index)=>{
     const current=p.kind==='catalog'?published.get(p.id):p,li=document.createElement('li');li.dataset.key=identity(p);
     const name=current?.name||'Ficha indisponível',city=current?.city||'';
     li.innerHTML=`<article class="discovery-stop"><span class="para-tier ${p.kind==='external'?'is-external':'is-directory'}">${p.kind==='external'?'Opção externa — confirme na fonte':'Lugar do catálogo'}</span><h2 tabindex="-1">${index+1}. ${escape(name)}</h2>${city?`<p>${escape(city)}</p>`:''}${current?(p.kind==='catalog'?`<p><a href="/estabelecimento/${p.id}">Ver ficha ↗</a> · <a href="/cuia-rotas?restaurantId=${p.id}">Planejar chegada ↗</a></p>`:`<p><a href="${escape(safeUrl(p.url))}" target="_blank" rel="noopener noreferrer">Consultar fonte externa ↗</a></p><p>Funcionamento e deslocamento ainda precisam de confirmação.</p>`):'<p>Esta ficha não está disponível publicamente. Você pode removê-la ou escolher outro lugar.</p>'}<div class="discovery-actions"><button type="button" data-move="up" ${index===0?'disabled':''} aria-label="Mover ${escape(name)} para cima">↑ Subir</button><button type="button" data-move="down" ${index===items.length-1?'disabled':''} aria-label="Mover ${escape(name)} para baixo">↓ Descer</button><button type="button" data-remove aria-label="Remover ${escape(name)} do passeio">Remover</button></div></article>`;
     li.addEventListener('click',event=>{const button=event.target.closest('[data-move],[data-remove]');if(!button)return;const next=read(),at=next.findIndex(x=>identity(x)===identity(p));if(at<0)return;
      if(button.hasAttribute('data-remove'))next.splice(at,1);else{const target=at+(button.dataset.move==='up'?-1:1);if(target<0||target>=next.length)return;[next[at],next[target]]=[next[target],next[at]];}
      if(write(next))render(identity(p));else status.textContent='Não foi possível guardar a alteração neste navegador.';
     });list.append(li);
    });
    status.textContent=items.length?`${items.length} opções na ordem escolhida. Horários e conexões não foram calculados.`:status.textContent;
    if(focusKey){const target=[...list.children].find(li=>li.dataset.key===focusKey)||list.firstElementChild;target?.querySelector('h2')?.focus();}
   }catch(error){if(id!==run)return;status.textContent='Não foi possível atualizar as fichas. Seus lugares continuam guardados; tente novamente.';retry.hidden=false;}
 }
 retry?.addEventListener('click',()=>{cached=null;render();});
 document.querySelector('[data-discovery-clear]')?.addEventListener('click',()=>{
  if(write([])){try{localStorage.removeItem('cuia-visit-plans-v1');}catch{}render();renderVisitPlans();}else status.textContent='O navegador não permitiu apagar os lugares.';
 });
 document.querySelector('[data-discovery-import]')?.addEventListener('click',()=>{
  let favorites=[];try{favorites=JSON.parse(document.getElementById('discovery-account-favorites')?.textContent||'[]');}catch{}
  const items=read(),known=new Set(items.map(identity));for(const p of favorites){const item={kind:'catalog',id:p.id,name:p.name,city:p.city};if(valid(item)&&!known.has(identity(item))&&items.length<max){items.push(item);known.add(identity(item));}}
  if(write(items))render();else status.textContent='O navegador não permitiu adicionar os favoritos.';
 });
 function renderVisitPlans(){const host=document.getElementById('discovery-visit-plans');if(!host)return;host.replaceChildren();let plans=[];try{plans=JSON.parse(localStorage.getItem('cuia-visit-plans-v1')||'[]');}catch{}
  if(!Array.isArray(plans))plans=[];for(const path of plans.slice(0,20)){try{const u=new URL(path,location.origin);if(u.origin!==location.origin||u.pathname!=='/cuia-rotas'||![...u.searchParams.keys()].every(k=>['restaurantId','mode','localTime'].includes(k))||!/^\d+$/.test(u.searchParams.get('restaurantId')))continue;const li=document.createElement('li'),a=document.createElement('a');a.href=u.pathname+u.search;a.textContent='Rever chegada ao destino '+u.searchParams.get('restaurantId');li.append(a);host.append(li);}catch{}}
  if(!host.children.length){const li=document.createElement('li');li.textContent='Nenhum plano de chegada guardado neste navegador.';host.append(li);}
 }
 render();renderVisitPlans();
})();
