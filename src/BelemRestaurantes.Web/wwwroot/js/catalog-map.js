(() => {
 'use strict';
 const host = document.querySelector('#interactive-map'); if (!host) return;
 const status = document.querySelector('#map-status'), retry = document.querySelector('#map-retry');
 const liveList = document.querySelector('#map-live-list');
 let map, layers, controller, timer, requestId = 0, lastKey, loading;
 const cache = new Map();
 const link = (text, url) => { const a = document.createElement('a'); a.textContent = text; a.href = url; return a; };
 async function refresh() {
  const id = ++requestId; controller?.abort(); controller = new AbortController();
  const bounds = map.getBounds();
  const params = new URLSearchParams(location.search); params.delete('after');
  params.set('bbox', [Math.max(-180,bounds.getWest()),Math.max(-90,bounds.getSouth()),Math.min(180,bounds.getEast()),Math.min(90,bounds.getNorth())].map(n=>n.toFixed(5)).join(',')); params.set('limit','200');
  const key = params.toString(); if (lastKey === key && Date.now()-(cache.get(key)?.time || 0)<15000) { liveList.hidden=false;document.querySelector('#map-results').hidden=true;return; }
  status.textContent = 'Carregando lugares nesta área…'; retry.hidden = true;
  try {
   let result = cache.get(key);
   if (!result || Date.now()-result.time>15000) {
    const response = await fetch('/api/mapa/pontos?'+key,{signal:controller.signal});
    if (!response.ok) throw Error('map'); result = {data:await response.json(),time:Date.now()};
    if(cache.size>=20)cache.delete(cache.keys().next().value); cache.set(key,result);
   }
   if(id!==requestId)return; lastKey=key; layers.clearLayers(); liveList.replaceChildren();
   const groups = [];
   for(const point of result.data.points) {
    const pixel=map.project([point.latitude,point.longitude],map.getZoom());
    const group=groups.find(g=>g.pixel.distanceTo(pixel)<64);
    if(group)group.points.push(point);else groups.push({pixel,points:[point]});
    const item=document.createElement('li'); item.append(link(point.name+' · '+point.city+' · '+(point.category||'Sem categoria')+' · '+point.catalogTier,'/establishment/'+point.id),link('Como chegar','/cuia-rotas?restaurantId='+point.id)); liveList.append(item);
   }
   for(const {points} of groups) {
    const p=points[0], grouped=points.length>1;
    const marker=L.marker([p.latitude,p.longitude],{icon:L.divIcon({className:'map-pin',html:grouped?String(points.length):'●',iconSize:[44,44]}),title:grouped?points.length+' lugares: ampliar':p.name});
    if(grouped) {
     const detail=document.createElement('div');
     for(const place of points.slice(0,10)){detail.append(link(place.name,'/establishment/'+place.id),document.createElement('br'));}
     const zoom=document.createElement('button');zoom.type='button';zoom.textContent='Ampliar grupo';zoom.addEventListener('click',()=>map.setView([p.latitude,p.longitude],Math.min(19,map.getZoom()+2)));detail.append(zoom);marker.bindPopup(detail);
    }
    else { const detail=document.createElement('div'); detail.append(link(p.name,'/establishment/'+p.id),document.createElement('br'),link('Como chegar','/cuia-rotas?restaurantId='+p.id));marker.bindPopup(detail); }
    marker.addTo(layers);
   }
   liveList.hidden=false;document.querySelector('#map-results').hidden=true;
   status.textContent=(!host.dataset.tiles?'Sem base de ruas configurada. ':'')+result.data.points.length+' lugares nesta área.'+(result.data.next?' Há mais resultados: amplie o mapa ou use os filtros.':'');
   performance.mark('map-useful');
  } catch(error) { if(error.name==='AbortError')return; status.textContent='Mapa indisponível. A lista de fichas continua disponível.';document.querySelector('#map-results').hidden=false; retry.hidden=false; }
 }
 async function open() {
  if(loading)return; loading=true;
  try {
   host.hidden=false;
   if(!map) {
    if(!window.L)await new Promise((resolve,reject)=>{const css=document.createElement('link');css.rel='stylesheet';css.href='/vendor/leaflet/leaflet.css';document.head.append(css);const script=document.createElement('script');script.src='/vendor/leaflet/leaflet.js';script.onload=resolve;script.onerror=reject;document.head.append(script);});
    map=L.map(host,{scrollWheelZoom:false,maxZoom:19,minZoom:3}).setView([Number(host.dataset.lat),Number(host.dataset.lng)],12);
    if(host.dataset.tiles){const tiles=L.tileLayer(host.dataset.tiles,{maxZoom:19,attribution:''}).addTo(map);const attribution=document.createElement('span');attribution.textContent=host.dataset.attribution;map.attributionControl.getContainer().append(attribution);tiles.on('tileerror',()=>{status.textContent='Base de ruas indisponível. Os pontos e a lista continuam disponíveis.';});}
    else {host.setAttribute('data-no-basemap','true');status.textContent='Base de ruas não configurada. Exibindo posições e lista de fichas.';}
    layers=L.layerGroup().addTo(map);
    map.on('movestart',()=>{requestId++;controller?.abort();clearTimeout(timer);});
    map.on('moveend',()=>{clearTimeout(timer);timer=setTimeout(refresh,250);});
   }
   map.invalidateSize(); await refresh();
  } catch {status.textContent='Não foi possível abrir o mapa. Use a lista ou tente novamente.';retry.hidden=false;}
  finally {loading=false;}
 }
 document.querySelector('#map-open').addEventListener('click',open);
 retry.addEventListener('click',()=>{lastKey=null;open();});
 document.querySelector('#map-list-toggle').addEventListener('click',()=>{host.hidden=true;liveList.hidden=true;document.querySelector('#map-results').hidden=false;document.querySelector('#map-results').scrollIntoView({block:'start'});});
})();
