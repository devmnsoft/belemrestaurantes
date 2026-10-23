(() => {
  'use strict';

  const form = document.getElementById('cuia-ai-form');
  if (!form) return;

  const promptInput = document.getElementById('ai-prompt');
  const cityInput = document.getElementById('ai-city');
  const occasionInput = document.getElementById('ai-occasion');
  const dietaryInput = document.getElementById('ai-dietary');
  const submitBtn = document.getElementById('ai-submit');
  const loadingEl = document.getElementById('ai-loading');
  const resultsEl = document.getElementById('ai-results');
  const countEl = document.getElementById('ai-prompt-count');
  const filters = document.getElementById('cuia-ai-filters');
  const storageKey = 'belem-cuia-places';

  const tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
  const token = tokenEl ? tokenEl.value : '';
  let busy = false, requestSequence=0, activeRequest;
  const cancelBtn=document.getElementById('ai-cancel');
  cancelBtn?.addEventListener('click',()=>{activeRequest?.abort();requestSequence++;busy=false;submitBtn.disabled=false;loadingEl.hidden=true;cancelBtn.hidden=true;resultsEl.removeAttribute('aria-busy');showLocal('<p>Consulta cancelada. Você pode tentar outra pergunta.</p>','Consulta cancelada.');});
  let conversation = window.CuiaUi.state.read('cuia-questions', []);
  if(!Array.isArray(conversation))conversation=[];
  const webInput = document.getElementById('ai-search-web');
  if(webInput){if(window.CuiaUi.state.read('cuia-search-web',false))webInput.checked=true;if(webInput.checked)window.CuiaUi.state.write('cuia-search-web',true);webInput.addEventListener('change',()=>window.CuiaUi.state.write('cuia-search-web',webInput.checked));}
  try{const initial=JSON.parse(document.getElementById('cuia-initial-question')?.textContent||'null');if(typeof initial==='string'&&initial.trim()&&conversation.at(-1)!==initial){conversation=[...conversation,initial].slice(-3);window.CuiaUi.state.write('cuia-questions',conversation);}}catch{}
  document.getElementById('cuia-clear-session')?.addEventListener('click',()=>{
    activeRequest?.abort();requestSequence++;busy=false;submitBtn.disabled=false;
    conversation=[];window.CuiaUi.state.write('cuia-questions',[]);window.CuiaUi.state.write('cuia-search-web',false);
    window.CuiaUi.state.remove(storageKey);if(webInput)webInput.checked=false;
    showLocal('<p>A conversa e as opções desta aba foram apagadas. Faça uma nova pergunta.</p>','Conversa reiniciada.');promptInput.focus();
  });

  if (filters && window.matchMedia('(min-width: 768px)').matches) filters.open = true;
  if(document.getElementById('cuia-initial-places'))rememberPlaces(readInitialPlaces());

  document.addEventListener('click', (event) => {
    const chip = event.target.closest('.cuia-ai-chip[data-prompt]');
    if (!chip || busy) return;
    promptInput.value = chip.dataset.prompt || '';
    if (chip.dataset.occasion && occasionInput) occasionInput.value = chip.dataset.occasion;
    if (countEl) countEl.textContent = `${promptInput.value.length} / 500`;
    executeAsk();
  });

  form.addEventListener('submit', (event) => {
    event.preventDefault();
    executeAsk();
  });

  promptInput.addEventListener('input', () => {
    if (countEl) countEl.textContent = `${promptInput.value.length} / 500`;
  });

  promptInput.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      executeAsk();
    }
  });

  async function executeAsk() {
    const prompt = (promptInput.value || '').trim();
    if (!prompt) {
      promptInput.focus();
      return;
    }
    if (busy) return;

    const places = loadPlaces();
    const searchWeb = document.getElementById('ai-search-web')?.checked === true;
    if (!searchWeb && isCloserFollowUp(prompt)) {
      if (!places.length) {
        showLocal('<p>A pergunta anterior não deixou restaurantes conferidos nesta aba. Sem essa lista, a Cuia não compara distância.</p>', 'Não foi possível comparar distâncias.');
        return;
      }
      renderCloser(places);
      return;
    }
    if (!searchWeb && isBusFollowUp(prompt)) {
      if (!places.length) {
        showLocal('<p>A pergunta anterior não deixou um restaurante conferido nesta aba. Quando houver uma casa na lista, use Como chegar.</p>', 'Não foi possível indicar o ônibus.');
        return;
      }
      renderBus(places);
      return;
    }

    const id=++requestSequence;
    conversation=window.CuiaUi.state.read('cuia-questions',[]);
    busy = true;
    if(cancelBtn)cancelBtn.hidden=false;
    loadingEl.hidden = false;
    resultsEl.hidden = true;
    resultsEl.setAttribute('aria-busy', 'true');
    submitBtn.disabled = true;
    const abort = new AbortController();activeRequest=abort;
    const timer = setTimeout(() => abort.abort(), 75000);
    let slowTimer;
    try {
      slowTimer = setTimeout(() => {
        if (id !== requestSequence) return;
        const text = loadingEl.querySelector('p');
        if (text) text.textContent = 'Ainda reunindo referências. Com pesquisa na internet a consulta pode levar mais tempo — você pode cancelar quando quiser.';
      }, 25000);
      const payload = {
        prompt: prompt,
        city: cityInput.value || null,
        occasion: occasionInput.value || null,
        dietary: dietaryInput.value || null,
        searchWeb: document.getElementById('ai-search-web')?.checked === true,
        previousQuestions: conversation.slice(-3),
        placeIds: places.filter(p=>Number.isSafeInteger(p.id)&&p.id>0).slice(0,4).map(p=>p.id)
      };

      const response = await fetch('/api/cuia-ia/ask', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Accept': 'application/json',
          'X-CSRF-TOKEN': token
        },
        body: JSON.stringify(payload),
        signal: abort.signal,
        redirect: 'manual'
      });

      const data = await readCuiaPayload(response);
      if(id!==requestSequence)return;
      if(data.answerKind!=='limite'){conversation=window.CuiaUi.rememberQuestion(conversation,prompt);window.CuiaUi.state.write('cuia-questions',conversation);}
      renderResults(data);
    } catch (err) {
      if(id!==requestSequence)return;
      loadingEl.hidden = true;
      showAskError(err);
    } finally {
      clearTimeout(timer);
      clearTimeout(slowTimer);
      if(id===requestSequence){busy=false;submitBtn.disabled=false;loadingEl.hidden=true;if(cancelBtn)cancelBtn.hidden=true;resultsEl.removeAttribute('aria-busy');activeRequest=null;}
    }
  }

  function plain(text) {
    return String(text || '')
      .toLowerCase()
      .replace(/[áàâã]/g, 'a')
      .replace(/[éê]/g, 'e')
      .replace(/í/g, 'i')
      .replace(/[óôõ]/g, 'o')
      .replace(/ú/g, 'u')
      .replace(/[?!.]+$/g, '')
      .trim();
  }

  function isCloserFollowUp(text) {
    const value = plain(text);
    return value === 'qual deles fica mais perto' || value === 'qual fica mais perto';
  }

  function isBusFollowUp(text) {
    const value = plain(text);
    return value === 'como chego de onibus' || value === 'como chegar de onibus';
  }

  function readInitialPlaces() {
    const node = document.getElementById('cuia-initial-places');
    if (!node) return [];
    try { return JSON.parse(node.textContent || '[]'); }
    catch { return []; }
  }

  function loadPlaces() {
    try {
      const parsed = window.CuiaUi.state.read(storageKey, []);
      return Array.isArray(parsed) ? parsed : [];
    } catch {
      return [];
    }
  }

  function rememberPlaces(places) {
    const slim = (Array.isArray(places) ? places : []).slice(0, 4).map((place) => ({
      id: place.id,
      name: place.name || '',
      city: place.city || '',
      neighborhood: place.neighborhood || '',
      latitude: finite(place.latitude),
      longitude: finite(place.longitude),
      address: place.address || ''
    })).filter((place) => place.id);
    try { window.CuiaUi.state.write(storageKey, slim); }
    catch { /* a aba segue sem memória local */ }
  }

  function finite(value) {
    const number = typeof value === 'number' ? value : Number(value);
    return Number.isFinite(number) ? number : null;
  }

  function renderCloser(places) {
    const located = places.filter((place) => place.latitude !== null && place.longitude !== null);
    if (located.length < 2) {
      showLocal(`
        <p>Ainda não há coordenadas conferidas para comparar a distância entre esses lugares.</p>
        <p>A Cuia não estima qual fica mais perto da Estação das Docas nem de outro ponto sem essa informação.</p>
      `, 'Não foi possível comparar distâncias.');
      return;
    }
    if (!navigator.geolocation) {
      showLocal('<p>Este navegador não informou uma forma de ler a sua posição. Sem isso, a Cuia não diz qual lugar fica mais perto.</p>', 'Não foi possível comparar distâncias.');
      return;
    }
    showLocal(`
      <p>Para comparar, a Cuia usa só a sua posição nesta aba e as coordenadas já publicadas. Nada disso fica gravado no portal.</p>
      <button type="button" class="cuia-ai-retry" id="cuia-use-location">Usar minha localização nesta aba</button>
    `, 'A comparação de distância espera a sua posição.');
    const button = resultsEl.querySelector('#cuia-use-location');
    if (!button) return;
    button.addEventListener('click', () => {
      button.disabled = true;
      navigator.geolocation.getCurrentPosition((position) => {
        const originLat = position.coords.latitude;
        const originLng = position.coords.longitude;
        const ranked = located
          .map((place) => ({ place, km: distanceKm(originLat, originLng, place.latitude, place.longitude) }))
          .sort((a, b) => a.km - b.km);
        const items = ranked.map((item) => `
          <li>
            <strong>${escapeHtml(item.place.name)}</strong>
            <p>${escapeHtml(item.place.neighborhood || item.place.city)} · ${item.km.toFixed(1)} km a partir da sua posição nesta aba.</p>
            <a href="/estabelecimento/${encodeURIComponent(String(item.place.id))}">Ver restaurante</a>
            <a href="/cuia-rotas?restaurantId=${encodeURIComponent(String(item.place.id))}">Como chegar</a>
          </li>
        `).join('');
        showLocal(`
          <p>Pesquisa concluída. A ordem usa só as coordenadas conferidas e a posição informada agora.</p>
          <ul class="cuia-ai-local-list">${items}</ul>
        `, `Pesquisa concluída. Encontramos ${ranked.length} estabelecimentos.`);
      }, () => {
        showLocal('<p>Sem a sua posição, a Cuia não calcula qual lugar fica mais perto.</p>', 'Não foi possível comparar distâncias.');
      }, { enableHighAccuracy: false, timeout: 8000, maximumAge: 60000 });
    });
  }

  function renderBus(places) {
    const items = places.map((place) => `
      <li>
        <strong>${escapeHtml(place.name)}</strong>
        <p>${escapeHtml(place.address || place.city || '')}</p>
        <a href="/cuia-rotas?restaurantId=${encodeURIComponent(String(place.id))}">Como chegar</a>
      </li>
    `).join('');
    showLocal(`
      <p>O caminho de ônibus fica no Cuia Rotas, para o restaurante que você escolher. A Cuia não descreve linha, tarifa nem horário que não esteja nessa página.</p>
      <ul class="cuia-ai-local-list">${items}</ul>
    `, `Pesquisa concluída. Encontramos ${places.length} estabelecimentos.`);
  }

  function distanceKm(lat1, lng1, lat2, lng2) {
    const earth = 6371;
    const dLat = (lat2 - lat1) * Math.PI / 180;
    const dLng = (lng2 - lng1) * Math.PI / 180;
    const a = Math.sin(dLat / 2) ** 2
      + Math.cos(lat1 * Math.PI / 180) * Math.cos(lat2 * Math.PI / 180) * Math.sin(dLng / 2) ** 2;
    return 2 * earth * Math.asin(Math.min(1, Math.sqrt(a)));
  }

  function showLocal(body, summary) {
    loadingEl.hidden = true;
    resultsEl.innerHTML = `
      <div class="cuia-ai-meta"><span class="cuia-ai-guarantee">${escapeHtml(summary)}</span></div>
      <div class="cuia-ai-response-body">${body}<p class="cuia-ai-session-note">Esta continuação vale só nesta aba.</p></div>
    `;
    resultsEl.hidden = false;
    revealResults();
  }

  async function readCuiaPayload(response) {
    if (response.type === 'opaqueredirect' || response.redirected || response.status === 0 || [301, 302, 303, 307, 308].includes(response.status)) {
      throw askFailure('Não conseguimos concluir sua pesquisa agora. Tente novamente.', true);
    }
    const contentType = (response.headers.get('content-type') || '').toLowerCase();
    const body = await response.text();
    const trimmed = body.trim();
    let payload = null;
    if (trimmed) {
      try { payload = JSON.parse(trimmed); }
      catch { payload = null; }
    }
    const html = contentType.includes('text/html') || trimmed.startsWith('<');
    if (html || !response.ok || !payload) {
      throw askFailure(messageFor(response.status, payload, html), canRetry(response.status, payload));
    }
    return window.CuiaUi.normalize(payload);
  }

  function askFailure(message, retryable) {
    const error = new Error(message);
    error.cuiaPublic = true;
    error.retryable = retryable;
    return error;
  }

  function messageFor(status, payload, unreadable) {
    const code = payload && typeof payload.code === 'string' ? payload.code : '';
    const detail = payload && typeof payload.detail === 'string' ? payload.detail : '';
    const trusted = /^(prompt_required|prompt_too_long|antiforgery|rate_limited|timeout|catalog_unavailable|provider_unavailable|unexpected|unauthorized|forbidden|invalid_body)$/.test(code)
      && detail && !/[<>]|DOCTYPE|Exception|SQL|stack/i.test(detail);
    if (trusted) return detail;
    if (status === 429) return 'Houve muitas consultas em pouco tempo. Espere alguns minutos e tente novamente.';
    if (status === 408 || status === 504) return 'A consulta demorou mais do que o esperado.';
    if (status === 502 || status === 503) return 'O serviço de recomendações está temporariamente indisponível.';
    if (unreadable) return 'Não conseguimos concluir sua pesquisa agora. Tente novamente.';
    return 'Não conseguimos concluir sua pesquisa agora. Tente novamente.';
  }

  function canRetry(status, payload) {
    if (payload && typeof payload.retryable === 'boolean') return payload.retryable;
    return status === 0 || status === 408 || status === 429 || status >= 500;
  }

  function showAskError(err) {
    const aborted = err && err.name === 'AbortError';
    const offline = err && err.name === 'TypeError';
    const message = aborted
      ? 'A consulta demorou mais do que o esperado.'
      : offline
        ? 'Não conseguimos concluir sua pesquisa agora. Tente novamente.'
        : (err && err.cuiaPublic ? err.message : 'Não conseguimos concluir sua pesquisa agora. Tente novamente.');
    const retryable = aborted || offline || !err || err.retryable !== false;
    resultsEl.innerHTML = `
      <div class="cuia-ai-error" role="alert" tabindex="-1">
        <p>${escapeHtml(message)}</p>
        <p>Não foi possível concluir sua pesquisa. Você pode tentar novamente.</p>
        ${retryable ? '<button type="button" class="cuia-ai-retry">Tentar novamente</button>' : ''}
      </div>
    `;
    resultsEl.hidden = false;
    const retry = resultsEl.querySelector('.cuia-ai-retry');
    if (retry) retry.addEventListener('click', () => executeAsk());
    const alert = resultsEl.querySelector('.cuia-ai-error');
    if (alert) alert.focus();
  }

  function kindLabel(kind) {
    return window.CuiaUi.kind(kind);
  }

  function renderResults(data) {
    const places = Array.isArray(data.recommendations) ? data.recommendations : [];
    const directory=Array.isArray(data.directoryMatches)?data.directoryMatches:[];
    rememberPlaces([...places,...directory]);
    let html = `
      <div class="cuia-ai-meta">
        <span class="cuia-ai-guarantee">${data.externalSuggestions?.length ? `Encontramos ${data.externalSuggestions.length} sugestões externas com fontes.` : places.length ? `Encontramos ${places.length} opções conferidas no portal.` : 'Explore a resposta e as opções disponíveis abaixo.'}</span>
        <span class="cuia-ai-kind">${escapeHtml(kindLabel(data.answerKind))}</span>
      </div>
      ${window.CuiaUi.notice(data)}
      <div class="cuia-ai-response-body">
        ${window.CuiaUi.answer(data)}
      </div>
    `;
    html += window.CuiaUi.external(data)+window.CuiaUi.trails(data);

    html += window.CuiaUi.sources(data);

    if (places.length > 0) {
      html += `
        <div class="cuia-ai-section">
          <h3>Restaurantes com informações conferidas</h3>
          <div class="cuia-ai-grid">
            ${places.map(r => `
              <article class="cuia-ai-reco-card">
                ${safePhoto(r.photoUrl)}
                <div class="cuia-ai-reco-header">
                  <h4><a href="/estabelecimento/${encodeURIComponent(String(r.id))}">${escapeHtml(r.name)}</a></h4>
                </div>
                <p class="cuia-ai-reco-cat">${escapeHtml(r.category || 'Restaurante')} · ${escapeHtml(r.city || 'Pará')} ${r.neighborhood ? `(${escapeHtml(r.neighborhood)})` : ''}</p>
                <p class="cuia-ai-reco-reason">${escapeHtml(r.reason || '')}</p>
                ${r.address ? `<p class="cuia-ai-reco-address">${escapeHtml(r.address)}</p>` : ''}
                <div class="cuia-ai-reco-actions">
                  <button type="button" class="para-save" data-save-kind="catalog" data-save-id="${encodeURIComponent(String(r.id))}" data-save-name="${escapeHtml(r.name)}" data-save-city="${escapeHtml(r.city)}">Guardar para depois</button>
                  <a href="/estabelecimento/${encodeURIComponent(String(r.id))}" class="para-button">Ver restaurante</a>
                  <a href="/cuia-rotas?restaurantId=${encodeURIComponent(String(r.id))}" class="para-text-link">Como chegar</a>
                </div>
              </article>
            `).join('')}
          </div>
        </div>
      `;
    }

    if (data.recipes && data.recipes.length > 0) {
      html += `
        <div class="cuia-ai-section">
          <h3>Receitas do Pará</h3>
          <div class="cuia-ai-recipes-list">
            ${data.recipes.map(rc => `
              <a href="/receitas/${encodeURIComponent(rc.slug)}" class="cuia-ai-recipe-pill">
                <strong>${escapeHtml(rc.title)}</strong>
                <span>${escapeHtml(rc.subtitle || '')} · ${rc.totalMinutes} min · nível ${escapeHtml(rc.difficulty || '')}</span>
              </a>
            `).join('')}
          </div>
        </div>
      `;
    }

    if (data.regionalCuriosities && data.regionalCuriosities.length > 0) {
      html += `
        <div class="cuia-ai-curiosities">
          <h4>Nota cultural, separada do cardápio</h4>
          <ul>
            ${data.regionalCuriosities.map(c => `<li>${escapeHtml(c)}</li>`).join('')}
          </ul>
        </div>
      `;
    }

    if(directory.length)html+=`<section class="cuia-ai-section"><h3>Fichas públicas para explorar</h3><p>Estes cadastros correspondem à busca, mas não são recomendações verificadas. Confirme os dados antes da visita.</p><ul class="cuia-ai-local-list">${directory.map(p=>`<li><a href="/establishment/${encodeURIComponent(String(p.id))}">${escapeHtml(p.name)}</a><p>${escapeHtml(p.city)} · ${escapeHtml(p.category||'Sem categoria')}</p><a href="/cuia-rotas?restaurantId=${encodeURIComponent(String(p.id))}">Como chegar</a></li>`).join('')}</ul></section>`;
    if (data.suggestedFollowUps && data.suggestedFollowUps.length > 0) {
      html += `
        <div class="cuia-ai-followups">
          <span>Continuar nesta aba</span>
          ${data.suggestedFollowUps.map(f => `
            <button type="button" class="cuia-ai-chip" data-prompt="${escapeHtml(f)}">${escapeHtml(f)}</button>
          `).join('')}
        </div>
      `;
    }

    resultsEl.innerHTML = html;
    window.dispatchEvent(new Event('cuia-rendered'));
    resultsEl.hidden = false;
    revealResults();
  }

  function revealResults() {
    if (resultsEl.hidden) resultsEl.hidden = false;
    resultsEl.focus({ preventScroll: true });
    const motion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    resultsEl.scrollIntoView({ behavior: motion ? 'auto' : 'smooth', block: 'nearest' });
  }

  function escapeHtml(text) {
    if (!text) return '';
    return String(text)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  function safePhoto(url) {
    if (typeof url !== 'string') return '';
    if (url.startsWith('/media/') || url.startsWith('https://')) {
      return `<img class="cuia-ai-photo" src="${escapeHtml(url)}" alt="" width="640" height="360" loading="lazy" decoding="async">`;
    }
    return '';
  }

  function formatMarkdown(md) { return window.CuiaUi.markdown(md); }
})();
