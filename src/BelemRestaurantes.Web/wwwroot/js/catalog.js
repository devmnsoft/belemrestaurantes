(() => {
  'use strict';
  const disclosure = document.querySelector('.para-filter-disclosure');
  const mobile = matchMedia('(max-width:760px)');
  if (disclosure) { disclosure.open = !mobile.matches; mobile.addEventListener('change', event => { disclosure.open = !event.matches; }); }
  const catalog = document.getElementById('catalog-results');
  const buttons = document.querySelectorAll('[data-catalog-view]');
  const setView = view => {
    catalog?.classList.toggle('is-list', view === 'list');
    buttons.forEach(button => button.setAttribute('aria-pressed', String(button.dataset.catalogView === view)));
  };
  try { setView(localStorage.getItem('belem:catalog-view') || 'grid'); } catch { setView('grid'); }
  buttons.forEach(button => button.addEventListener('click', () => {
    setView(button.dataset.catalogView);
    try { localStorage.setItem('belem:catalog-view',button.dataset.catalogView); } catch { /* Navegação permanece funcional sem armazenamento. */ }
  }));
  const nearby = document.querySelector('[data-nearby]');
  const status = document.getElementById('nearby-status');
  nearby?.addEventListener('click', () => {
    if (!navigator.geolocation) { status.textContent=nearby.dataset.locationUnavailable || 'Localização indisponível neste navegador. Use os filtros de cidade e bairro.'; return; }
    nearby.disabled=true; status.textContent=nearby.dataset.locationLoading || 'Solicitando sua localização ao navegador…';
    navigator.geolocation.getCurrentPosition(position => {
      const params=new URLSearchParams(location.search);
      params.set('lat',position.coords.latitude.toFixed(5));params.set('lng',position.coords.longitude.toFixed(5));
      params.set('sort','nearby');params.set('radius','10');params.delete('page');params.delete('city');params.delete('neighborhood');
      location.assign('/busca?'+params);
    }, () => { nearby.disabled=false; status.textContent=nearby.dataset.locationError || 'Não foi possível obter a localização. Você pode buscar por cidade e bairro.'; },{timeout:10000,enableHighAccuracy:false,maximumAge:300000});
  });
})();
