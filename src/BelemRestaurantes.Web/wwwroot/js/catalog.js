(() => {
  'use strict';
  if ('scrollRestoration' in history) history.scrollRestoration = 'manual';
  const panel = document.querySelector('.para-filter-panel');
  const openButton = document.querySelector('[data-filter-open]');
  const mobile = matchMedia('(max-width:760px)');
  let lockedScroll = 0;
  const lock = () => {
    lockedScroll = window.scrollY;
    document.body.classList.add('para-scroll-lock');
    document.body.style.top = '-' + lockedScroll + 'px';
  };
  const jump = y => {
    const root = document.documentElement;
    const previous = root.style.scrollBehavior;
    root.style.scrollBehavior = 'auto';
    window.scrollTo(0, y);
    root.style.scrollBehavior = previous;
  };
  const unlock = () => {
    document.body.classList.remove('para-scroll-lock');
    document.body.style.top = '';
    jump(lockedScroll);
  };
  const closePanel = () => {
    if (!panel?.classList.contains('is-open')) return;
    panel.classList.remove('is-open');
    panel.removeAttribute('role');
    panel.removeAttribute('aria-modal');
    document.querySelectorAll('[data-filter-inert]').forEach(e=>{e.inert=false;e.removeAttribute('data-filter-inert');});
    openButton?.setAttribute('aria-expanded', 'false');
    unlock();
    openButton?.focus();
  };
  const openPanel = () => {
    if (!panel || !mobile.matches) return;
    panel.classList.add('is-open');
    panel.setAttribute('role', 'dialog');
    panel.setAttribute('aria-modal', 'true');
    openButton?.setAttribute('aria-expanded', 'true');
    lock();
    document.querySelectorAll('body > header, body > footer, .para-results, #search-related').forEach(e=>{e.inert=true;e.setAttribute('data-filter-inert','');});
    panel.querySelector('input,select,button')?.focus();
  };
  openButton?.addEventListener('click', openPanel);
  panel?.querySelectorAll('[data-filter-close]').forEach(button => button.addEventListener('click', closePanel));
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape') closePanel();
    if (event.key !== 'Tab' || !panel?.classList.contains('is-open')) return;
    const items=[...panel.querySelectorAll('button,a,input,select,textarea')].filter(e=>!e.disabled && e.getClientRects().length);
    const first=items[0],last=items[items.length-1];
    if(event.shiftKey && document.activeElement===first){event.preventDefault();last?.focus();}
    else if(!event.shiftKey && document.activeElement===last){event.preventDefault();first?.focus();}
  });
  mobile.addEventListener('change', () => { if (!mobile.matches) closePanel(); });

  const setView = view => {
    document.getElementById('catalog-results')?.classList.toggle('is-list', view === 'list');
    document.querySelectorAll('[data-catalog-view]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.catalogView === view)));
  };
  try { setView(localStorage.getItem('belem:catalog-view') || 'grid'); } catch { setView('grid'); }
  document.addEventListener('click', event => {
    const button = event.target.closest('[data-catalog-view]');
    if (!button) return;
    setView(button.dataset.catalogView);
    try { localStorage.setItem('belem:catalog-view', button.dataset.catalogView); } catch { /* A visualização continua sem armazenamento. */ }
  });

  const form = document.getElementById('catalog-filters');
  const rememberScroll = () => {
    try { sessionStorage.setItem('belem:busca-scroll', JSON.stringify({ url: location.pathname + location.search, y: window.scrollY })); } catch { /* A volta da ficha segue sem memória. */ }
  };
  const restoreScroll = () => {
    try {
      const saved = JSON.parse(sessionStorage.getItem('belem:busca-scroll') || 'null');
      if (saved && saved.url === location.pathname + location.search && Number.isFinite(saved.y)) {
        jump(saved.y);
        setTimeout(() => jump(saved.y), 0);
        setTimeout(() => jump(saved.y), 120);
      }
    } catch { /* Ignora memória inválida. */ }
  };
  const rememberIfResult = event => {
    if (event.target.closest('#catalog-results a, .para-restaurant-grid a')) rememberScroll();
  };
  document.addEventListener('pointerdown', rememberIfResult);
  document.addEventListener('click', rememberIfResult);
  window.addEventListener('pageshow', restoreScroll);

  let activeRequest, requestId=0;
  const update = async (url, push=true) => {
    activeRequest?.abort();
    const controller=new AbortController();activeRequest=controller;
    const id=++requestId;
    const timer=setTimeout(()=>controller.abort(),25000);
    form?.setAttribute('aria-busy','true');
    try {
      const response=await fetch(url,{headers:{'X-Requested-With':'fetch'},signal:controller.signal});
      if(!response.ok)throw new Error('search');
      const doc=new DOMParser().parseFromString(await response.text(),'text/html');
      if(id!==requestId)return;
      const next=doc.querySelector('.para-results'), current=document.querySelector('.para-results');
      if(!next||!current)throw new Error('markup');
      closePanel();current.replaceWith(next);
      for(const control of form?.elements||[]) {
        if(!control.name)continue;
        const original=doc.getElementById(control.id);
        if(original){if(control.type==='checkbox')control.checked=original.checked;else control.value=original.value;}
      }
      if(push)history.pushState(null,'',url);
      try {setView(localStorage.getItem('belem:catalog-view')||'grid');} catch {setView('grid');}
      document.dispatchEvent(new CustomEvent('catalog:updated'));
      document.querySelector('.para-results')?.scrollIntoView({block:'start',behavior:'instant'});
    } catch(error) {
      if(id!==requestId)return;
      if(error.name==='AbortError' && activeRequest!==controller)return;
      location.assign(url);
    } finally {clearTimeout(timer);if(id===requestId)form?.removeAttribute('aria-busy');}
  };
  form?.addEventListener('submit',event=>{
    event.preventDefault();
    const params=new URLSearchParams(new FormData(form));params.delete('lat');params.delete('lng');
    for(const [key,value]of [...params.entries()])if(!value)params.delete(key);
    update('/busca?'+params);
  });
  window.addEventListener('popstate',()=>update(location.pathname+location.search,false));

  const nearby = document.querySelector('[data-nearby]');
  const status = document.getElementById('nearby-status');
  const nearbyForm = document.getElementById('nearby-form');
  nearby?.addEventListener('click', () => {
    if (!navigator.geolocation || !nearbyForm) { status.textContent = nearby.dataset.locationUnavailable || 'Localização indisponível neste navegador. Use os filtros de cidade e bairro.'; return; }
    nearby.disabled = true; status.textContent = nearby.dataset.locationLoading || 'Solicitando sua localização ao navegador…';
    navigator.geolocation.getCurrentPosition(position => {
      nearbyForm.lat.value = position.coords.latitude.toFixed(5);
      nearbyForm.lng.value = position.coords.longitude.toFixed(5);
      nearbyForm.requestSubmit();
    }, () => { nearby.disabled = false; status.textContent = nearby.dataset.locationError || 'Não foi possível obter a localização. Você pode buscar por cidade e bairro.'; }, { timeout: 10000, enableHighAccuracy: false, maximumAge: 300000 });
  });
})();
