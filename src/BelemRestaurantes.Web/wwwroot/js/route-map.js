(() => {
 'use strict';
 let loader;
 const maps=new Map();
 function load(key) {
  if(window.google?.maps?.Map)return Promise.resolve();
  if(loader)return loader;
  loader=new Promise((resolve,reject)=>{
   const script=document.createElement('script');let finished=false;
   const end=(error)=>{if(finished)return;finished=true;clearTimeout(timer);delete window.cuiaRouteMapsReady;if(error){script.remove();loader=null;reject(error);}else resolve();};
   const timer=setTimeout(()=>end(Error('timeout')),12000);
   window.cuiaRouteMapsReady=()=>end();
   window.gm_authFailure=()=>{window.dispatchEvent(new Event('cuia-map-denied'));end(Error('denied'));};
   script.src='https://maps.googleapis.com/maps/api/js?'+new URLSearchParams({key,loading:'async',callback:'cuiaRouteMapsReady',v:'quarterly',language:'pt-BR'});
   script.onerror=()=>end(Error('network'));document.head.append(script);
  });return loader;
 }
 const path=points=>Array.isArray(points)?points.map(p=>({lat:p.Lat,lng:p.Lng})).filter(p=>Number.isFinite(p.lat)&&Number.isFinite(p.lng)&&Math.abs(p.lat)<=90&&Math.abs(p.lng)<=180):[];
 async function open(id,index) {
  const host=document.getElementById(id),status=document.querySelector(`[data-route-status="${id}"]`);
  if(!host||host.dataset.loading==='true')return;
  host.dataset.loading='true';status.textContent='Carregando mapa do trajeto…';
  try {
   await load(host.dataset.googleKey);host.hidden=false;
   let state=maps.get(id);
   if(!state) {
    const data=JSON.parse(host.dataset.route),full=path(data.path);
    const map=new google.maps.Map(host,{center:full[0]||path(data.steps.find(s=>s.Path?.length>1)?.Path)[0],zoom:13,mapTypeControl:false,streetViewControl:false,fullscreenControl:true});
    const all=new google.maps.LatLngBounds(),parts=[];
    const line=(points,walking)=>{
     if(points.length<2)return null;
     points.forEach(p=>all.extend(p));
     const poly=new google.maps.Polyline({map,path:points,strokeColor:walking?'#143f32':'#215f87',strokeWeight:5,strokeOpacity:walking?0:1,icons:walking?[{icon:{path:'M 0,-1 0,1',strokeOpacity:1,scale:3},offset:'0',repeat:'14px'}]:[]});return poly;
    };
    // Full geometry is a neutral outline; only actual step paths receive modal styling.
    if(full.length>1&&!data.steps.some(s=>s.Path?.length>1))line(full,false);
    full.forEach(p=>all.extend(p));
    data.steps.forEach((step,i)=>{
     const points=path(step.Path),poly=line(points,step.Mode==='WALK');parts.push(points);
     poly?.addListener('click',()=>{const number=id.replace('route-map-','');const item=document.getElementById(`route-${number}-step-${i}`);item?.focus();item?.scrollIntoView({block:'center'});});
     for(const [coordinate,title] of [[step.BoardLocation,'Embarque: '+(step.Board||'parada')],[step.AlightLocation,'Desembarque: '+(step.Alight||'parada')]]){
      const point=path(coordinate?[coordinate]:[])[0];if(point)new google.maps.Marker({map,position:point,title});
     }
    });
    const ends=full.length>1?full:parts.flat();
    if(ends.length>1){new google.maps.Marker({map,position:ends[0],title:'Origem do trajeto',label:'A'});new google.maps.Marker({map,position:ends.at(-1),title:'Destino do trajeto',label:'B'});}
    state={map,bounds:all,parts};maps.set(id,state);
   }
   const selected=index!==undefined?state.parts[index]:null;
   if(selected?.length>1){const bounds=new google.maps.LatLngBounds();selected.forEach(p=>bounds.extend(p));state.map.fitBounds(bounds,32);}else state.map.fitBounds(state.bounds,32);
   status.textContent='Traçado fornecido pelo Google Maps. Consulte as etapas textuais para linhas, sentido e horários.';
   if(index!==undefined)host.scrollIntoView({block:'center'});
  }catch{host.hidden=true;status.textContent='Não foi possível carregar o mapa. As etapas continuam disponíveis. Use o botão para tentar novamente ou abra o planejador externo.';}
  finally{host.dataset.loading='false';}
 }
 document.querySelectorAll('[data-route-open]').forEach(button=>button.addEventListener('click',()=>open(button.dataset.routeOpen)));
 document.querySelectorAll('[data-route-step]').forEach(button=>button.addEventListener('click',()=>open(button.dataset.routeTarget,Number(button.dataset.routeStep))));
 window.addEventListener('cuia-map-denied',()=>{for(const [id] of maps){document.getElementById(id).hidden=true;document.querySelector(`[data-route-status="${id}"]`).textContent='Mapa indisponível. Continue pelo itinerário textual ou planejador externo.';}maps.clear();});
})();
