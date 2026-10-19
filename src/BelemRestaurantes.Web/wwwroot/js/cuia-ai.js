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

  const tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
  const token = tokenEl ? tokenEl.value : '';

  // Quick suggestion chips
  document.querySelectorAll('.cuia-ai-chip').forEach(chip => {
    chip.addEventListener('click', () => {
      const q = chip.dataset.prompt;
      if (q) {
        promptInput.value = q;
        executeAsk();
      }
    });
  });

  form.addEventListener('submit', (e) => {
    e.preventDefault();
    executeAsk();
  });

  promptInput.addEventListener('input', () => {
    if (countEl) countEl.textContent = `${promptInput.value.length} / 500`;
  });

  promptInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      executeAsk();
    }
  });

  async function executeAsk() {
    const prompt = (promptInput.value || '').trim();
    if (!prompt) {
      promptInput.focus();
      return;
    }

    loadingEl.hidden = false;
    resultsEl.hidden = true;
    resultsEl.setAttribute('aria-busy', 'true');
    submitBtn.disabled = true;
    const abort = new AbortController();
    const timer = setTimeout(() => abort.abort(), 45000);

    try {
      const payload = {
        prompt: prompt,
        city: cityInput.value || null,
        occasion: occasionInput.value || null,
        dietary: dietaryInput.value || null
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
      renderResults(data);
    } catch (err) {
      loadingEl.hidden = true;
      showAskError(err);
    } finally {
      clearTimeout(timer);
      loadingEl.hidden = true;
      resultsEl.removeAttribute('aria-busy');
      submitBtn.disabled = false;
    }
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
    return payload;
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
        ${retryable ? '<button type="button" class="cuia-ai-retry">Tentar novamente</button>' : ''}
      </div>
    `;
    resultsEl.hidden = false;
    const retry = resultsEl.querySelector('.cuia-ai-retry');
    if (retry) retry.addEventListener('click', () => executeAsk());
    const alert = resultsEl.querySelector('.cuia-ai-error');
    if (alert) alert.focus();
  }

  function renderResults(data) {
    const places = Array.isArray(data.recommendations) ? data.recommendations : [];
    let html = `
      <div class="cuia-ai-meta">
        <span class="cuia-ai-guarantee">${places.length ? `Pesquisa concluída. Encontramos ${places.length} estabelecimentos.` : 'Não encontramos restaurantes conferidos para essa busca. Veja outras opções no catálogo.'}</span>
      </div>
      <div class="cuia-ai-response-body">
        ${formatMarkdown(data.answerMarkdown || '')}
      </div>
    `;

    if (data.recommendations && data.recommendations.length > 0) {
      html += `
        <div class="cuia-ai-section">
          <h3>Restaurantes com informações conferidas</h3>
          <div class="cuia-ai-grid">
            ${data.recommendations.map(r => `
              <article class="cuia-ai-reco-card">
                ${safePhoto(r.photoUrl)}
                <div class="cuia-ai-reco-header">
                  <h4><a href="/estabelecimento/${encodeURIComponent(String(r.id))}">${escapeHtml(r.name)}</a></h4>
                </div>
                <p class="cuia-ai-reco-cat">${escapeHtml(r.category || 'Restaurante')} · ${escapeHtml(r.city || 'Pará')} ${r.neighborhood ? `(${escapeHtml(r.neighborhood)})` : ''}</p>
                <p class="cuia-ai-reco-reason">${escapeHtml(r.reason || '')}</p>
                ${r.address ? `<p class="cuia-ai-reco-address">📍 ${escapeHtml(r.address)}</p>` : ''}
                <div class="cuia-ai-reco-actions">
                  <a href="/estabelecimento/${encodeURIComponent(String(r.id))}" class="para-button">Ver restaurante</a>
                  <a href="/cuia-rotas?restaurantId=${r.id}" class="para-text-link">Como chegar</a>
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
          <h3>Receitas do caderno</h3>
          <div class="cuia-ai-recipes-list">
            ${data.recipes.map(rc => `
              <a href="/receitas/${encodeURIComponent(rc.slug)}" class="cuia-ai-recipe-pill">
                <strong>${escapeHtml(rc.title)}</strong>
                <span>${escapeHtml(rc.subtitle || '')} · ${rc.totalMinutes} min · Nível ${escapeHtml(rc.difficulty || '')} ↗</span>
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

    if (data.suggestedFollowUps && data.suggestedFollowUps.length > 0) {
      html += `
        <div class="cuia-ai-followups">
          <span>Continuar a conversa</span>
          ${data.suggestedFollowUps.map(f => `
            <button type="button" class="cuia-ai-chip" data-prompt="${escapeHtml(f)}">${escapeHtml(f)}</button>
          `).join('')}
        </div>
      `;
    }

    resultsEl.innerHTML = html;
    resultsEl.hidden = false;
    resultsEl.focus({ preventScroll: true });

    // Attach click handlers to newly created chips
    resultsEl.querySelectorAll('.cuia-ai-chip').forEach(chip => {
      chip.addEventListener('click', () => {
        promptInput.value = chip.dataset.prompt;
        executeAsk();
      });
    });

    // Quick actions for the generated answer
    const answerActions = document.createElement('div');
    answerActions.className = 'cuia-ai-followups';
    const copyAnswer = document.createElement('button');
    copyAnswer.type = 'button';
    copyAnswer.className = 'cuia-ai-chip';
    copyAnswer.textContent = 'Copiar resposta';
    copyAnswer.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(data.answerMarkdown || '');
        copyAnswer.textContent = 'Resposta copiada';
      } catch {
        copyAnswer.textContent = 'Não foi possível copiar';
      }
    });
    answerActions.append(copyAnswer);
    resultsEl.append(answerActions);

    resultsEl.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
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

  function formatMarkdown(md) {
    if (!md) return '';
    let escaped = escapeHtml(md);

    // Headers
    escaped = escaped.replace(/^### (.*$)/gim, '<h3>$1</h3>');
    escaped = escaped.replace(/^## (.*$)/gim, '<h2>$1</h2>');
    escaped = escaped.replace(/^# (.*$)/gim, '<h1>$1</h1>');

    // Bold
    escaped = escaped.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>');

    // Italic
    escaped = escaped.replace(/\*(.*?)\*/g, '<em>$1</em>');

    // Links [text](url)
    escaped = escaped.replace(/\[(.*?)\]\((.*?)\)/g, (match, text, url) => {
      if (url.startsWith('/') || url.startsWith('https://')) {
        return `<a href="${url}">${text}</a>`;
      }
      return text;
    });

    // Unordered lists
    escaped = escaped.replace(/^\- (.*$)/gim, '<li>$1</li>');
    escaped = escaped.replace(/(<li>.*<\/li>)/s, '<ul>$1</ul>');

    // Line breaks
    escaped = escaped.replace(/\n\n/g, '<br/><br/>');

    return escaped;
  }
})();
