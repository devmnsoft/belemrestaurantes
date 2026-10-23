(() => {
 'use strict';
 const escape = value => String(value ?? '').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 const url = value => {try {const u=new URL(value);return u.protocol==='https:'&&!u.username&&!u.password?u.href:null;}catch{return null;}};
 const kind = value => value==='referencia'?'Resposta baseada nas fontes identificadas.':value==='esclarecimento'?'Precisamos identificar o lugar correto.':value==='pesquisa'?'Resposta gerada por IA com pesquisa externa. Consulte as fontes.':value==='servico'?'Resposta gerada por IA no contexto do portal.':value==='limite'?'A Cuia atende gastronomia, visitas e deslocamentos no Pará.':'Orientação local do portal, sem geração por IA nesta consulta.';
 const notice = data => data.answerKind==='limite'||data.answerKind==='esclarecimento'||data.generationStatus==='ok'||data.generationStatus==='reference_answer'||data.generationStatus==='evidence_limited'?'':`<p class="cuia-local-notice" role="status">${data.generationStatus==='timeout'?'A IA demorou mais que o esperado. Você pode reenviar a pergunta; abaixo está a orientação local.':data.generationStatus==='credential_error'?'O serviço de IA precisa de ajuste na configuração. A orientação abaixo é local.':data.generationStatus==='model_unavailable'?'O serviço de IA está temporariamente indisponível. A orientação abaixo é local.':data.generationStatus==='session_limit'?'O limite de IA desta sessão foi atingido. Você pode continuar usando as orientações locais e os serviços do portal.':data.generationStatus==='quota_exhausted'?'A cota de IA desta consulta foi atingida. A orientação abaixo é local, sem geração por IA.':'A IA não respondeu nesta consulta. A resposta abaixo usa o catálogo e as referências identificadas.'}</p>`;
 function referenceNotice(data){return data.searchStatus==='indexed_sources'?'<p class="cuia-local-notice" role="status">Fontes externas previamente consultadas e revisadas. Esta consulta não executou uma pesquisa aberta na internet nem confirma um cardápio atual.</p>':'';}
 function referenceLinks(data){if(!['indexed_sources','mixed_sources'].includes(data.searchStatus))return '';return '<section><h4>Consulte as referências</h4><ul>'+(data.sources||[]).filter(s=>url(s.url)).map(s=>`<li><a href="${escape(url(s.url))}" target="_blank" rel="noopener noreferrer">${escape(s.title)} ↗</a></li>`).join('')+'</ul></section>';}
 function external(data){
  const sources=Array.isArray(data.sources)?data.sources:[];
  const cards=(Array.isArray(data.externalSuggestions)?data.externalSuggestions:[]).slice(0,8).map(p=>{
   const source=(p.sourceNumbers||[]).filter(Number.isInteger).map(n=>sources[n-1]).find(s=>s&&url(s.url));if(!source)return '';
   const href=url(source.url),query=new URLSearchParams({name:p.name||'',city:p.city||'',source:href});
   return `<article class="cuia-ai-reco-card cuia-external-card"><span class="para-tier is-external">Opção externa — ainda não cadastrada no portal</span><h4>${escape(p.name)}</h4>${p.city?`<p>${escape(p.city)}${p.neighborhood?' · '+escape(p.neighborhood):''}</p>`:''}<p>${escape(p.evidence)}</p><a href="${escape(href)}" target="_blank" rel="noopener noreferrer">Consultar fonte: ${escape(source.title)} ↗</a><p>Consultada em ${escape(new Date(source.retrievedAt).toLocaleString('pt-BR'))}. Confirme antes de sair.</p><a href="/sugerir?${escape(query)}">Indicar este lugar ao portal</a><button type="button" class="para-save" data-save-kind="external" data-save-name="${escape(p.name)}" data-save-city="${escape(p.city)}" data-save-url="${escape(href)}">Guardar opção externa</button></article>`;
  }).filter(Boolean);
  return cards.length?`<section class="cuia-ai-section"><h3>Descobertas além do catálogo</h3><p>Opções encontradas em fontes públicas; a menção não confirma funcionamento, qualidade ou serviços.</p><div class="cuia-ai-grid">${cards.join('')}</div></section>`:'';
 }
 const searchMessages={
  collected_sources:'Referências coletadas anteriormente pelo robô e pelo acervo do portal. Confira a data e a publicação original; não é uma pesquisa nova ao vivo.',
  irrelevant:'Os resultados externos encontrados não sustentavam a pergunta sobre este estabelecimento e foram descartados.',
  cached:'Resultados reutilizados do cache. A data de consulta original foi preservada.',
  credential_error:'O provedor de pesquisa precisa de ajuste na credencial. As referências disponíveis continuam acessíveis.',
  public_sources:'Pesquisa em conhecimento público da Wikipédia, com cobertura limitada. Não confirma cardápios, preços ou horários atuais.',
  public_sources_cached:'Resultados anteriores da Wikipédia, reutilizados do cache. Confira a data de consulta; não confirmam informações atuais do estabelecimento.',
  indexed_sources:'Referências previamente consultadas e revisadas. Não há resultados novos de pesquisa aberta nesta resposta.',
  mixed_sources:'Referências revisadas e fontes externas. Confira a origem e a data de cada fonte.',
  ok:'Resultados da pesquisa externa. Consulte as fontes antes de planejar sua visita.',
  empty:'A pesquisa foi executada, mas não encontrou fontes utilizáveis para esta pergunta.',
  not_configured:'A pesquisa externa não está habilitada neste ambiente. Você pode explorar os roteiros e as referências já revisadas.',
  quota_exhausted:'O limite de pesquisa externa foi atingido. Os roteiros e o catálogo continuam disponíveis.',
  unavailable:'O serviço de pesquisa externa não respondeu nesta tentativa. Você pode tentar novamente ou explorar os roteiros.'
 };
 function researchLinks(data){
  if(typeof data.researchQuery!=='string'||!data.researchQuery.trim()||data.answerKind==='limite')return '';
  const q=data.researchQuery.slice(0,500),links=[['Google','https://www.google.com/search?q='+encodeURIComponent(q)],['Bing','https://www.bing.com/search?q='+encodeURIComponent(q)],['VEJA e guias','https://www.google.com/search?q='+encodeURIComponent(q+' site:veja.abril.com.br')],['Instagram','https://www.google.com/search?q='+encodeURIComponent(q+' site:instagram.com')],['TikTok','https://www.google.com/search?q='+encodeURIComponent(q+' site:tiktok.com')],['LinkedIn','https://www.google.com/search?q='+encodeURIComponent(q+' site:linkedin.com')]];
  return '<section class="cuia-ai-section cuia-research-links"><h3>Continuar a pesquisa na web</h3><p>Estes links abrem uma pesquisa no seu navegador. O portal não consultou esses serviços por meio destes botões.</p><ul>'+links.map(([name,href])=>`<li><a href="${escape(href)}" target="_blank" rel="noopener noreferrer">Pesquisar em ${escape(name)} ↗</a></li>`).join('')+'</ul></section>';
 }
 function answer(data){return markdown(data.answerMarkdown,data.sources||[]);}
 function sources(data){
  const valid=(data.sources||[]).map((s,i)=>({...s,number:i+1})).filter(s=>typeof s.title==='string'&&s.title.trim()&&url(s.url)).slice(0,8);
  const message=escape(searchMessages[data.searchStatus]||'Confira a origem e a data das informações.');
  const failed=['unavailable','quota_exhausted','not_configured','credential_error','irrelevant'].includes(data.externalSearchStatus)&&data.externalSearchStatus!==data.searchStatus?`<p class="cuia-search-state">${escape(searchMessages[data.externalSearchStatus]||'A busca externa não foi concluída.')}</p>`:'';
  const body=!valid.length?data.searchStatus==='not_requested'?'':`<p class="cuia-search-state" role="status">${message}</p><p class="cuia-next-actions"><a href="/roteiros">Explorar roteiros</a> · <a href="/busca">Ver lugares publicados</a></p>`:`<section class="cuia-ai-section"><h3>${data.searchStatus==='indexed_sources'?'Referências revisadas':'Fontes consultadas'}</h3><p>${message}</p><ul>${valid.map(s=>`<li><a href="${escape(url(s.url))}" target="_blank" rel="noopener noreferrer">[${s.number}] ${escape(s.title)}</a>${Number.isNaN(Date.parse(s.retrievedAt))?'':' · consultada em '+escape(new Date(s.retrievedAt).toLocaleString('pt-BR'))}${s.publishedAt&&!Number.isNaN(Date.parse(s.publishedAt))?' · publicada em '+escape(new Date(s.publishedAt).toLocaleDateString('pt-BR')):''}<p>${escape(s.sourceKind==='structured_site'?'Dados estruturados declarados pelo site; confirme atualidade.':s.sourceKind==='social_index'?'Perfil ou postagem indexada; conteúdo completo não consultado.':s.sourceKind==='collected_reference'?'Referência do acervo do robô; confira a data e a fonte.':s.sourceKind==='reviewed_reference'?'Referência revisada anteriormente.':s.retrievalStatus==='retrieved'?'Página consultada nesta pesquisa ou no cache indicado.':'Trecho do índice de pesquisa; página completa não verificada.')}</p></li>`).join('')}</ul></section>`;
  return failed+body+researchLinks(data);
 }
 function trails(data){
  const items=(Array.isArray(data.trailSuggestions)?data.trailSuggestions:[]).filter(t=>t&&/^[a-z0-9-]{3,80}$/.test(t.slug)&&typeof t.title==='string'&&t.title.trim()).slice(0,3);
  return items.length?`<section class="cuia-ai-section"><h3>Roteiros para continuar explorando</h3><ul>${items.map(t=>`<li><a href="/roteiros/${encodeURIComponent(t.slug)}">${escape(t.title)}</a><p>${escape(t.region)} · etapas editoriais com referências</p></li>`).join('')}</ul></section>`:'';
 }
 function markdown(value,sources=[]){
  const inline=text=>escape(text).replace(/\*\*(.+?)\*\*/g,'<strong>$1</strong>').replace(/(?<!\*)\*(.+?)\*(?!\*)/g,'<em>$1</em>').replace(/\[([^\]]+)\]\(([^\s)]+)\)/g,(token,label,href)=>{
   const target=href.replace(/&amp;/g,'&');
   if(/^\/culinaria\/referencia\/[1-9][0-9]*$/.test(target))return `<a href="${escape(target)}">${label}</a>`;
   const safe=url(target);return safe&&sources.some(s=>url(s.url)===safe)?`<a href="${escape(safe)}" target="_blank" rel="noopener noreferrer">${label}</a>`:label;
  }).replace(/\[(\d+)\]/g,(token,n)=>{const s=sources[Number(n)-1];return s&&url(s.url)?`<a href="${escape(url(s.url))}" target="_blank" rel="noopener noreferrer" aria-label="Fonte ${Number(n)}: ${escape(s.title)}">[${Number(n)}]</a>`:token;});
  let html='',list=null;const close=()=>{if(list){html+=`</${list}>`;list=null;}};
  for(const raw of String(value||'').replace(/\r\n/g,'\n').split('\n')){
   const line=raw.trim();if(!line||/^\d+\.\s*$/.test(line)){close();continue;}
   const heading=line.match(/^#{1,3}\s+(.+)$/),item=line.match(/^(- |\d+\.\s+)(.+)$/);
   if(heading){close();html+='<h3>'+inline(heading[1])+'</h3>';continue;}
   if(item){const tag=item[1]==='- '?'ul':'ol';if(list!==tag){close();list=tag;html+=`<${tag}>`;}html+='<li>'+inline(item[2])+'</li>';continue;}
   close();html+='<p>'+inline(line)+'</p>';
  }close();return html;
 }
 const memory=new Map();
 const state={
  read(key,fallback){
   let value;if(memory.has(key))value=memory.get(key);else try{const raw=sessionStorage.getItem(key);value=raw===null?undefined:JSON.parse(raw);}catch{value=undefined;}
   if(Array.isArray(fallback))return Array.isArray(value)?value:fallback;
   return typeof value===typeof fallback?value:fallback;
  },
  write(key,value){memory.set(key,value);try{sessionStorage.setItem(key,JSON.stringify(value));}catch{/* Keep this tab usable without browser storage. */}},
  remove(key){memory.delete(key);try{sessionStorage.removeItem(key);}catch{}}
 };
 function normalize(data){
  if(!data||typeof data!=='object'||Array.isArray(data)||typeof data.answerMarkdown!=='string'||!data.answerMarkdown.trim())throw new Error('Resposta incompleta da Cuia.');
  const result={...data};
  for(const key of ['recommendations','directoryMatches','recipes','sources','externalSuggestions'])result[key]=Array.isArray(data[key])?data[key].filter(v=>v&&typeof v==='object'&&!Array.isArray(v)):[];
  for(const key of ['regionalCuriosities','suggestedFollowUps'])result[key]=Array.isArray(data[key])?data[key].filter(v=>typeof v==='string'):[];
  result.externalSuggestions=result.externalSuggestions.map(p=>({...p,sourceNumbers:Array.isArray(p.sourceNumbers)?p.sourceNumbers:[]}));
  return result;
 }
 const rememberQuestion=(items,prompt)=>{const valid=Array.isArray(items)?items.filter(x=>typeof x==='string'):[];return valid.length?[valid[0],...valid.slice(1),prompt].filter((x,i,a)=>i===a.indexOf(x)).filter((x,i,a)=>i===0||i>=a.length-2):[prompt];};
 window.CuiaUi={answer,rememberQuestion,escape,url,kind,notice:data=>notice(data),external,referenceLinks,state,normalize,sources,markdown,trails};
})();
