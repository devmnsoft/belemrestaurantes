"use strict";
const el=id=>document.getElementById(id);let places=[];
function safeUrl(v){try{const u=new URL(v);return ['https:','http:'].includes(u.protocol)?u.href:null;}catch{return null;}}
function elem(tag,klass,content){const n=document.createElement(tag);if(klass)n.className=klass;if(content!==undefined)n.textContent=content;return n;}

// Confirmação de ações destrutivas (funciona em qualquer página, compatível com CSP).
document.addEventListener('submit',e=>{
  const f=e.target;
  if(f instanceof HTMLFormElement && f.dataset.confirm && !window.confirm(f.dataset.confirm)){
    e.preventDefault();
  }
});

// O restante só existe na página inicial (catálogo, rota, PIX, sugestão).
if(el('go')){
async function load(){el('status').textContent='Consultando o catálogo...';try{const params=new URLSearchParams({search:el('query').value,city:el('city').value,limit:'60'});const res=await fetch('/api/restaurants?'+params);if(!res.ok)throw Error('Falha de consulta');places=await res.json();render();populateAutosuggest(places);}catch{el('cards').replaceChildren();el('status').textContent='O catálogo ainda não está disponível. Verifique a conexão com o banco e o worker de descoberta.';}}
function render(){const cards=el('cards'),dest=el('destination');cards.replaceChildren();if(dest)dest.replaceChildren(new Option('Escolha um restaurante',''));el('status').textContent=places.length+' estabelecimento(s) disponível(is).';places.forEach(p=>{const card=elem('article','card'),photo=elem('div','photo');if(p.photoUrl && safeUrl(p.photoUrl)){const img=elem('img');img.src=p.photoUrl;img.alt='Foto fornecida com autorização: '+p.name;img.loading='lazy';img.referrerPolicy='no-referrer';img.onerror=()=>photo.replaceChildren(elem('span','', 'Foto ainda não disponível'));photo.append(img);}else photo.append(elem('span','','Foto ainda não disponível'));const inside=elem('div','inside');const title=elem('h3','');if(p.id){const a=elem('a','',p.name);a.href='/guia/'+encodeURIComponent(String(p.id));title.append(a);}else title.textContent=p.name;inside.append(title,elem('p','',[p.category,p.city,p.neighborhood].filter(Boolean).join(' · ')),elem('p','',p.address||'Endereço em verificação'));for(const [name,url] of [['Site',p.website],['Instagram',p.instagram],['Facebook',p.facebook],['TikTok',p.tiktok],['YouTube',p.youtube]]){if(safeUrl(url)){const a=elem('a','',name+' ↗');a.href=url;a.target='_blank';a.rel='noopener noreferrer';inside.append(a,document.createTextNode('  '));}}card.append(photo,inside);cards.append(card);if(dest)dest.add(new Option(p.name+' — '+p.city,p.id));});}
el('go').addEventListener('click',load);el('city').addEventListener('change',load);el('query').addEventListener('keydown',e=>{if(e.key==='Enter')load()});let timeout;el('query').addEventListener('input',()=>{clearTimeout(timeout);timeout=setTimeout(load,350)});
if(el('route')){el('route').addEventListener('click',()=>{const p=places.find(x=>String(x.id)===el('destination').value);if(!p)return alert('Selecione um restaurante');const dest=[p.address,p.city,'Pará Brasil'].filter(Boolean).join(', ');const origin=el('start').value.trim();const u=new URL('https://www.google.com/maps/dir/');u.searchParams.set('api','1');u.searchParams.set('destination',dest);if(origin)u.searchParams.set('origin',origin);u.searchParams.set('travelmode','transit');window.open(u,'_blank','noopener,noreferrer');});}
if(el('copyPix')){el('copyPix').addEventListener('click',()=>navigator.clipboard.writeText('18160057000113').then(()=>alert('Chave PIX copiada')).catch(()=>alert('PIX: 18.160.057/0001-13')));}
if(el('suggestForm')){el('suggestForm').addEventListener('submit',async e=>{e.preventDefault();const f=e.currentTarget,form=new FormData(f),s=el('suggestStatus');s.textContent='Enviando...';try{const res=await fetch('/api/restaurants/suggest',{method:'POST',body:form});if(!res.ok)throw Error('Não foi possível enviar');s.textContent='Sugestão recebida, aguardando revisão.';f.reset();}catch{s.textContent='Falha ao enviar. Tente novamente.';}});}
load();
function populateAutosuggest(items){var dl=document.getElementById('autosuggest');if(!dl)return;dl.replaceChildren();(items||[]).slice(0,12).forEach(function(p){var o=document.createElement('option');o.value=p.name;dl.append(o);});}
}
