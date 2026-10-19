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

  const tokenEl = form.querySelector('input[name="__RequestVerificationToken"]');
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
    submitBtn.disabled = true;

    try {
      const payload = {
        prompt: prompt,
        city: cityInput.value || null,
        occasion: occasionInput.value || null,
        dietary: dietaryInput.value || null
      };

      const response = await fetch('/api/cuia-ai/ask', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'RequestVerificationToken': token
        },
        body: JSON.stringify(payload)
      });

      if (!response.ok) {
        const errorData = await response.json().catch(() => ({}));
        throw new Error(errorData.error || 'Não foi possível processar sua consulta no momento.');
      }

      const data = await response.json();
      renderResults(data);
    } catch (err) {
      resultsEl.innerHTML = `
        <div class="cuia-ai-error" role="alert">
          <strong>Aviso:</strong> ${escapeHtml(err.message || 'Erro inesperado ao consultar a Cuia IA.')}
        </div>
      `;
      resultsEl.hidden = false;
    } finally {
      loadingEl.hidden = true;
      submitBtn.disabled = false;
    }
  }

  function renderResults(data) {
    let html = `
      <div class="cuia-ai-meta">
        <span class="cuia-ai-engine">⚡ Motor: ${escapeHtml(data.engineUsed || 'Cuia IA')}</span>
        <span class="cuia-ai-guarantee">✓ Catálogo oficial verificado</span>
      </div>
      <div class="cuia-ai-response-body">
        ${formatMarkdown(data.answerMarkdown || '')}
      </div>
    `;

    if (data.recommendations && data.recommendations.length > 0) {
      html += `
        <div class="cuia-ai-section">
          <h3>🍽️ Estabelecimentos selecionados para você</h3>
          <div class="cuia-ai-grid">
            ${data.recommendations.map(r => `
              <article class="cuia-ai-reco-card">
                <div class="cuia-ai-reco-header">
                  <h4><a href="/estabelecimento/${r.id}">${escapeHtml(r.name)}</a></h4>
                  ${r.cuiaScore ? `<span class="cuia-ai-score">★ ${Number(r.cuiaScore).toFixed(1)}</span>` : ''}
                </div>
                <p class="cuia-ai-reco-cat">${escapeHtml(r.category || 'Restaurante')} · ${escapeHtml(r.city || 'Pará')} ${r.neighborhood ? `(${escapeHtml(r.neighborhood)})` : ''}</p>
                <p class="cuia-ai-reco-reason">${escapeHtml(r.reason || '')}</p>
                ${r.address ? `<p class="cuia-ai-reco-address">📍 ${escapeHtml(r.address)}</p>` : ''}
                <div class="cuia-ai-reco-actions">
                  <a href="/estabelecimento/${r.id}" class="para-button">Ver detalhes ↗</a>
                  <a href="/cuia-rotas?restaurantId=${r.id}" class="para-text-link">Como chegar (Cuia Rotas) →</a>
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
          <h3>📖 Receitas relacionadas no Caderno do Pará</h3>
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
          <h4>💡 Segredos e Sabedoria da Floresta</h4>
          <ul>
            ${data.regionalCuriosities.map(c => `<li>${escapeHtml(c)}</li>`).join('')}
          </ul>
        </div>
      `;
    }

    if (data.suggestedFollowUps && data.suggestedFollowUps.length > 0) {
      html += `
        <div class="cuia-ai-followups">
          <span>Perguntas relacionadas:</span>
          ${data.suggestedFollowUps.map(f => `
            <button type="button" class="cuia-ai-chip" data-prompt="${escapeHtml(f)}">${escapeHtml(f)}</button>
          `).join('')}
        </div>
      `;
    }

    resultsEl.innerHTML = html;
    resultsEl.hidden = false;

    // Attach click handlers to newly created chips
    resultsEl.querySelectorAll('.cuia-ai-chip').forEach(chip => {
      chip.addEventListener('click', () => {
        promptInput.value = chip.dataset.prompt;
        executeAsk();
      });
    });

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
