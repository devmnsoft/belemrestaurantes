(() => {
  'use strict';

  const tokenInput = document.querySelector('#cuia-widget-token input[name="__RequestVerificationToken"]');
  const token = tokenInput ? tokenInput.value : '';

  const escapeHtml = value => String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');

  const widget = document.createElement('div');
  widget.className = 'cuia-widget';
  widget.innerHTML = `
    <button type="button" class="cuia-widget-toggle" aria-expanded="false" aria-controls="cuia-widget-panel" aria-label="Abrir concierge Cuia IA">
      <span aria-hidden="true">&#10024;</span> Cuia IA
    </button>
    <section id="cuia-widget-panel" class="cuia-widget-panel" hidden aria-labelledby="cuia-widget-title">
      <header class="cuia-widget-header">
        <strong id="cuia-widget-title">Cuia IA · Concierge do Pará</strong>
        <button type="button" class="cuia-widget-close" aria-label="Fechar concierge">×</button>
      </header>
      <form class="cuia-widget-form">
        <label for="cuia-widget-prompt" class="cuia-widget-label">O que você quer comer ou visitar?</label>
        <textarea id="cuia-widget-prompt" rows="3" maxlength="500" required placeholder="Ex.: Onde tomar tacacá em Belém?"></textarea>
        <label class="cuia-widget-web"><input type="checkbox" id="cuia-widget-web" /> Pesquisar também fora do catálogo</label>
        <button type="submit" class="cuia-widget-submit">Consultar</button>
      </form>
      <div class="cuia-widget-loading" hidden role="status" aria-live="polite">A Cuia está pesquisando…</div>
      <div class="cuia-widget-results" aria-live="polite"></div>
      <p><a href="/cuia-ia">Continuar na página da Cuia ↗</a></p>
    </section>
  `;
  document.querySelector('.para-header')?.after(widget);

  const toggle = widget.querySelector('.cuia-widget-toggle');
  const panel = widget.querySelector('#cuia-widget-panel');
  const closeBtn = widget.querySelector('.cuia-widget-close');
  const form = widget.querySelector('.cuia-widget-form');
  const promptInput = widget.querySelector('#cuia-widget-prompt');
  const loadingEl = widget.querySelector('.cuia-widget-loading');
  const resultsEl = widget.querySelector('.cuia-widget-results');
  const webInput=widget.querySelector('#cuia-widget-web');
  webInput.checked=window.CuiaUi.state.read('cuia-search-web',true)===true;
  webInput.addEventListener('change',()=>window.CuiaUi.state.write('cuia-search-web',webInput.checked));
  let active,sequence=0;
  const resize=()=>{const view=window.visualViewport;panel.style.setProperty('--cuia-visual-height',(view?.height||innerHeight)+'px');panel.style.setProperty('--cuia-visual-top',(view?.offsetTop||0)+'px');};
  window.visualViewport?.addEventListener('resize',resize);window.visualViewport?.addEventListener('scroll',resize);window.addEventListener('resize',resize);resize();

  const open = () => {
    panel.hidden = false;
    toggle.setAttribute('aria-expanded', 'true');
    promptInput.focus();
  };
  const close = () => {
    sequence++;active?.abort();form.querySelector('.cuia-widget-submit').disabled=false;loadingEl.hidden=true;
    panel.hidden = true;
    toggle.setAttribute('aria-expanded', 'false');
    toggle.focus();
  };

  toggle.addEventListener('click', () => {
    if (panel.hidden) open(); else close();
  });
  closeBtn.addEventListener('click', close);

  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && !panel.hidden) close();
  });
  document.addEventListener('click', event => {
    if (!widget.contains(event.target) && !panel.hidden) close();
  });

  form.addEventListener('submit', async event => {
    event.preventDefault();
    const prompt = promptInput.value.trim();
    if (!prompt) {
      promptInput.focus();
      return;
    }
    loadingEl.hidden = false;
    resultsEl.hidden = true;
    const submitBtn = form.querySelector('.cuia-widget-submit');
    submitBtn.disabled = true;
    const id=++sequence;active?.abort();const controller=new AbortController();active=controller;
    const timer=setTimeout(()=>controller.abort(),75000);
    try {
      const response = await fetch('/api/cuia-ia/ask', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Accept': 'application/json',
          'X-CSRF-TOKEN': token
        },
        body: JSON.stringify({ prompt, city: null, occasion: null, dietary: null, searchWeb:webInput.checked,
          previousQuestions:(window.CuiaUi.state.read('cuia-questions',[])||[]).slice(-3),
          placeIds:(window.CuiaUi.state.read('belem-cuia-places',[])||[]).filter(p=>Number.isSafeInteger(p.id)&&p.id>0).slice(0,4).map(p=>p.id) }),
        signal:controller.signal,
        redirect: 'manual'
      });
      if (response.type === 'opaqueredirect' || response.redirected || response.status === 0 || [301, 302, 303, 307, 308].includes(response.status)) {
        throw Object.assign(new Error('Não conseguimos concluir sua pesquisa agora. Tente novamente.'), { cuiaPublic: true });
      }
      const body = await response.text();
      let data = null;
      try { data = body.trim() ? JSON.parse(body) : null; } catch { data = null; }
      const html = (response.headers.get('content-type') || '').includes('text/html') || body.trim().startsWith('<');
      if (html || !response.ok || !data) {
        const detail = data && typeof data.detail === 'string' && data.code && !/[<>]|DOCTYPE|Exception|SQL/i.test(data.detail) ? data.detail : '';
        const message = response.status === 429
          ? 'Houve muitas consultas em pouco tempo. Espere alguns minutos e tente novamente.'
          : response.status === 504 || response.status === 408
            ? 'A consulta demorou mais do que o esperado.'
            : response.status === 503 || response.status === 502
              ? 'O serviço de recomendações está temporariamente indisponível.'
              : detail || 'Não conseguimos concluir sua pesquisa agora. Tente novamente.';
        throw Object.assign(new Error(message), { cuiaPublic: true });
      }
      if(id!==sequence)return;
      data=window.CuiaUi.normalize(data);
      if(data.answerKind!=='limite')window.CuiaUi.state.write('cuia-questions',window.CuiaUi.rememberQuestion(window.CuiaUi.state.read('cuia-questions',[]),prompt));
      renderResults(data);
    } catch (err) {
      if(id!==sequence)return;
      loadingEl.hidden = true;
      const message = err && err.cuiaPublic ? err.message : 'Não conseguimos concluir sua pesquisa agora. Tente novamente.';
      resultsEl.innerHTML = `<p class="cuia-widget-error" role="alert">${escapeHtml(message)}</p>`;
      resultsEl.hidden = false;
    } finally {
      clearTimeout(timer);
      if(id===sequence){submitBtn.disabled=false;loadingEl.hidden=true;active=null;}
    }
  });

  function renderResults(data) {
    const recommendations = Array.isArray(data.recommendations) ? data.recommendations : [];
    let html = `<p class="cuia-ai-kind">${escapeHtml(window.CuiaUi.kind(data.answerKind))}</p>${window.CuiaUi.notice(data)}<div class="cuia-widget-answer">${window.CuiaUi.answer(data)}</div>${window.CuiaUi.sources(data)+window.CuiaUi.external(data)+window.CuiaUi.trails(data)}`;
    if (recommendations.length) {
      html += '<ul class="cuia-widget-list">';
      for (const item of recommendations.slice(0, 8)) {
        html += `<li><a href="/estabelecimento/${encodeURIComponent(String(item.id))}">${escapeHtml(item.name || '')}</a><span>${escapeHtml(item.city || '')}</span></li>`;
      }
      html += '</ul>';
    }
    resultsEl.innerHTML = html;
    window.dispatchEvent(new Event('cuia-rendered'));
    resultsEl.hidden = false;
  }
})();
