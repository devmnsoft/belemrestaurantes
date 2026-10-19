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
      <span aria-hidden="true">✨</span> Cuia IA
    </button>
    <section id="cuia-widget-panel" class="cuia-widget-panel" hidden aria-labelledby="cuia-widget-title">
      <header class="cuia-widget-header">
        <strong id="cuia-widget-title">Cuia IA · Concierge do Pará</strong>
        <button type="button" class="cuia-widget-close" aria-label="Fechar concierge">×</button>
      </header>
      <form class="cuia-widget-form">
        <label for="cuia-widget-prompt" class="cuia-widget-label">O que você quer comer ou visitar?</label>
        <textarea id="cuia-widget-prompt" rows="3" maxlength="500" required placeholder="Ex.: Onde tomar tacacá em Belém?"></textarea>
        <button type="submit" class="cuia-widget-submit">Consultar</button>
      </form>
      <div class="cuia-widget-loading" hidden role="status" aria-live="polite">Consultando a camada Guia…</div>
      <div class="cuia-widget-results" aria-live="polite"></div>
    </section>
  `;
  document.body.append(widget);

  const toggle = widget.querySelector('.cuia-widget-toggle');
  const panel = widget.querySelector('#cuia-widget-panel');
  const closeBtn = widget.querySelector('.cuia-widget-close');
  const form = widget.querySelector('.cuia-widget-form');
  const promptInput = widget.querySelector('#cuia-widget-prompt');
  const loadingEl = widget.querySelector('.cuia-widget-loading');
  const resultsEl = widget.querySelector('.cuia-widget-results');

  const open = () => {
    panel.hidden = false;
    toggle.setAttribute('aria-expanded', 'true');
    promptInput.focus();
  };
  const close = () => {
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
    try {
      const response = await fetch('/api/cuia-ia/ask', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'RequestVerificationToken': token
        },
        body: JSON.stringify({ prompt, city: null, occasion: null, dietary: null })
      });
      if (!response.ok) {
        const errorData = await response.json().catch(() => ({}));
        throw new Error(errorData.error || 'Não foi possível consultar a Cuia IA.');
      }
      const data = await response.json();
      renderResults(data);
    } catch (err) {
      resultsEl.innerHTML = `<p class="cuia-widget-error">${escapeHtml(err.message || 'Erro inesperado.')}</p>`;
      resultsEl.hidden = false;
    } finally {
      loadingEl.hidden = true;
      submitBtn.disabled = false;
    }
  });

  function renderResults(data) {
    const recommendations = Array.isArray(data.recommendations) ? data.recommendations : [];
    let html = `<div class="cuia-widget-answer">${escapeHtml(data.answerMarkdown || '').replace(/\n/g, '<br/>')}</div>`;
    if (recommendations.length) {
      html += '<ul class="cuia-widget-list">';
      for (const item of recommendations.slice(0, 3)) {
        html += `<li><a href="/estabelecimento/${encodeURIComponent(String(item.id))}">${escapeHtml(item.name || '')}</a><span>${escapeHtml(item.city || '')}</span></li>`;
      }
      html += '</ul>';
    }
    resultsEl.innerHTML = html;
    resultsEl.hidden = false;
  }
})();
