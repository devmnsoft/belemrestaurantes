(() => {
  'use strict';
  let current;
  const run=()=>{
  current?.abort();
  const root = document.querySelector('[data-search-evidence]'); if (!root) return;
  const status = root.querySelector('[data-search-status]'), list = root.querySelector('[data-search-sources]');
  const controller = new AbortController(); current=controller; const timer = setTimeout(() => controller.abort(), 45000);
  window.addEventListener('pagehide', () => controller.abort(), {once:true});
  fetch('/api/busca/fontes?' + new URLSearchParams({q:root.dataset.query, city:root.dataset.city || ''}), {signal:controller.signal})
    .then(async response => { if(!response.ok) throw new Error('search'); return response.json(); })
    .then(data => {
      if(!root.isConnected || current!==controller)return;
      for(const item of data.culinary || []) {
        if(document.querySelector('[data-culinary-id="'+Number(item.id)+'"]'))continue;
        const article=document.createElement('article'), heading=document.createElement('h3'),link=document.createElement('a');
        link.href='/culinaria/referencia/'+item.id;link.textContent=item.title;heading.append(link);article.append(heading);
        const type=document.createElement('p');type.className='culinary-type';type.textContent=item.kind==='dish_evidence'?'Onde comer · menção documentada':'Receita de autor · ingredientes e preparo na fonte';article.append(type);
        const text=document.createElement('p');text.textContent=item.summary;article.append(text);
        const author=document.createElement('p');author.textContent='Fonte: '+item.author;article.append(author);
        if(item.videoUrl){const a=document.createElement('a');a.href=item.videoUrl;a.rel='noopener noreferrer';a.target='_blank';a.textContent='Assistir ao vídeo do autor ↗';article.append(a);}
        list.append(article);
      }
      const known=new Set((data.culinary||[]).map(x=>x.url));
      for(const source of data.sources || []) {
        if(known.has(source.url))continue;
        let url; try {url = new URL(source.url);} catch {continue;}
        if(url.protocol !== 'https:' || url.username || url.password) continue;
        const article = document.createElement('article'), heading = document.createElement('h3'), link = document.createElement('a');
        link.href=url.href;link.rel='noopener noreferrer';link.target='_blank';link.textContent=source.title;heading.append(link);article.append(heading);
        const origin=document.createElement('p');origin.textContent=(source.publisher || url.hostname)+' · '+(source.sourceKind==='collected_reference'?'Referência salva':source.sourceKind==='encyclopedia'?'Conhecimento enciclopédico':source.sourceKind==='ai_discovery'?'Link descoberto por pesquisa assistida; página ainda não verificada':'Resultado externo')+' · Consultado em '+new Date(source.retrievedAt).toLocaleDateString('pt-BR');article.append(origin);
        const snippet=document.createElement('p');snippet.textContent=source.snippet;article.append(snippet);list.append(article);
      }
      status.textContent = list.children.length ? list.children.length+(list.children.length===1?' referência encontrada. Consulte':' referências encontradas. Consulte')+' a fonte original para confirmar cardápios e horários.' : 'Nenhuma referência adicional disponível agora. Tente o nome do lugar e a cidade.';
      if(['not_configured','unavailable','quota_exhausted','credential_error'].includes(data.status)) status.textContent += ' A pesquisa externa está indisponível; referências salvas continuam acessíveis.';
    }).catch(() => {if(!root.isConnected || current!==controller)return;status.textContent='Não foi possível concluir a pesquisa externa. O catálogo e as referências salvas continuam disponíveis.'; const a=document.createElement('a');a.href='/referencias?'+new URLSearchParams({q:root.dataset.query});a.textContent='Ver referências salvas';list.append(a);})
    .finally(()=>clearTimeout(timer));
  };
  document.addEventListener('catalog:updated',run);run();
})();
