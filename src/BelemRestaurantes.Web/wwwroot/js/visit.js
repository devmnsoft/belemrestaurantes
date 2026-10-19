(() => {
  'use strict';
  const origin = document.querySelector('#visit-origin');
  document.querySelector('#visit-location')?.addEventListener('click', () => {
    const status = document.querySelector('#visit-location-status');
    if (!navigator.geolocation) { status.textContent = 'Localização indisponível. Informe uma referência.'; return; }
    status.textContent = 'Aguardando sua permissão para localizar…';
    navigator.geolocation.getCurrentPosition(position => {
      origin.value = `${position.coords.latitude.toFixed(4)},${position.coords.longitude.toFixed(4)}`;
      status.textContent = 'Localização preenchida. Consulte o trajeto para enviar ao provedor.';
    }, () => { status.textContent = 'Não foi possível obter a localização. Digite a origem.'; }, { enableHighAccuracy: false, timeout: 10000, maximumAge: 60000 });
  });
  const key = 'cuia-visit-plans-v1';
  function validPath(path) {
    if (typeof path !== 'string' || path.length > 300 || !path.startsWith('/cuia-rotas?')) return false;
    const url = new URL(path, location.origin), keys = [...url.searchParams.keys()];
    return url.origin === location.origin && url.pathname === '/cuia-rotas' && !url.hash
      && keys.every(x => ['restaurantId', 'mode', 'localTime'].includes(x)) && new Set(keys).size === keys.length
      && /^\d+$/.test(url.searchParams.get('restaurantId'))
      && ['transit', 'walking', 'bicycling', 'driving', 'boat'].includes(url.searchParams.get('mode'))
      && (!url.searchParams.has('localTime') || /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(url.searchParams.get('localTime')));
  }
  function read() { try { const items = JSON.parse(localStorage.getItem(key) || '[]'); return Array.isArray(items) ? items.filter(validPath).slice(0, 20) : []; } catch { return []; } }
  function render() {
    const list = document.querySelector('#visit-saved-list'); if (!list) return;
    list.replaceChildren();
    read().forEach((path, index) => {
      const item = document.createElement('li'); const link = document.createElement('a');
      link.href = path; const url = new URL(path, location.origin);
      link.textContent = `Destino #${url.searchParams.get('restaurantId')} · ${url.searchParams.get('localTime')?.replace('T', ' ') || 'Horário a escolher'}`;
      const button = document.createElement('button'); button.type = 'button'; button.className = 'visit-text-button'; button.textContent = 'Remover';
      button.setAttribute('aria-label', `Remover plano ${index + 1}`);
      button.addEventListener('click', () => { try { localStorage.setItem(key, JSON.stringify(read().filter(x => x !== path))); render(); } catch {} });
      item.append(link, button); list.append(item);
    });
  }
  document.querySelector('#visit-save')?.addEventListener('click', event => {
    try { const path = event.currentTarget.dataset.path; if (!validPath(path)) { document.querySelector('#visit-save-status').textContent = 'Não foi possível salvar este plano.'; return; } localStorage.setItem(key, JSON.stringify([path, ...read().filter(x => x !== path)].slice(0, 20))); document.querySelector('#visit-save-status').textContent = 'Plano salvo sem sua origem.'; render(); }
    catch { document.querySelector('#visit-save-status').textContent = 'O navegador não permitiu salvar. Use o compartilhamento.'; }
  });
  document.querySelector('[data-load-map]')?.addEventListener('click', event => {
    const button=event.currentTarget, host=document.querySelector('#visit-map');
    if(button.disabled)return;
    const iframe=document.createElement('iframe'), status=document.createElement('p');
    status.setAttribute('role','status'); status.textContent='Carregando mapa…';
    iframe.title='Localização do estabelecimento no OpenStreetMap'; iframe.referrerPolicy='no-referrer';
    button.disabled=true; const start=performance.now();
    const fail=()=>{iframe.remove();status.textContent='Mapa indisponível. Abra o mapa no provedor ou tente novamente.';button.disabled=false;};
    const timeout=setTimeout(fail,12000);
    iframe.addEventListener('load',()=>{clearTimeout(timeout);status.textContent='Mapa externo carregado. Se não aparecer, use o link do provedor.';performance.measure('visit-iframe',{start,duration:performance.now()-start});button.disabled=false;},{once:true});
    iframe.addEventListener('error',()=>{clearTimeout(timeout);fail();},{once:true});
    iframe.src=button.dataset.loadMap;host.replaceChildren(status,iframe);
  });
  document.querySelector('#visit-form')?.addEventListener('submit', event => {
    const button = event.currentTarget.querySelector('[type=submit]'); button.textContent = 'Consultando o caminho…'; button.disabled = true;
  });
  render();
})();
