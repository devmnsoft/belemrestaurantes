(() => {
  'use strict';
  const toggle = document.querySelector('.para-menu-toggle');
  const nav = document.getElementById('public-navigation');
  toggle?.addEventListener('click', () => {
    const open = toggle.getAttribute('aria-expanded') !== 'true';
    toggle.setAttribute('aria-expanded', String(open));
    nav?.classList.toggle('is-open', open);
  });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && toggle?.getAttribute('aria-expanded') === 'true') {
      toggle.setAttribute('aria-expanded', 'false');
      nav?.classList.remove('is-open');
      toggle.focus();
    }
  });
  document.querySelectorAll('.admin-nav-item-v3').forEach(link => {
    if (link.pathname === location.pathname) link.setAttribute('aria-current', 'page');
  });
  const input = document.getElementById('heroQuery');
  const dropdown = document.getElementById('heroAutocomplete');
  const status = document.getElementById('search-status');
  if (!input || !dropdown || !status) return;
  let timer, request;
  let version = 0;
  const close = () => { dropdown.hidden = true; input.setAttribute('aria-expanded', 'false'); };
  input.addEventListener('input', () => {
    clearTimeout(timer);
    request?.abort();
    const current = ++version;
    close();
    dropdown.replaceChildren();
    const query = input.value.trim();
    status.textContent = '';
    if (query.length < 2) return;
    timer = setTimeout(async () => {
      request = new AbortController();
      status.textContent = 'Procurando sugestões…';
      try {
        const response = await fetch('/api/search/autocomplete?q=' + encodeURIComponent(query), { signal: request.signal });
        if (!response.ok) throw new Error('autocomplete unavailable');
        const results = await response.json();
        if (version !== current) return;
        const fragment = document.createDocumentFragment();
        for (const item of Array.isArray(results) ? results.slice(0, 8) : []) {
          // Somente caminhos locais: a resposta não pode injetar HTML ou URLs externas.
          if (typeof item.url !== 'string' || !item.url.startsWith('/') || item.url.startsWith('//') || item.url.includes('\\')) continue;
          const target = new URL(item.url, location.origin);
          if (target.origin !== location.origin) continue;
          const link = document.createElement('a');
          link.setAttribute('role', 'option');
          link.setAttribute('aria-selected', 'false');
          link.tabIndex = -1;
          link.href = target.pathname + target.search + target.hash;
          const title = document.createElement('strong'); title.textContent = item.title ?? '';
          const detail = document.createElement('small'); detail.textContent = item.subtitle ?? '';
          link.append(title, detail); fragment.append(link);
        }
        dropdown.replaceChildren(fragment);
        const count = dropdown.children.length;
        status.textContent = count ? `${count} sugestões. Use a seta para baixo para navegar.` : 'Nenhuma sugestão. Você ainda pode buscar pelo termo digitado.';
        dropdown.hidden = count === 0;
        input.setAttribute('aria-expanded', String(count > 0));
      } catch (error) {
        if (error.name !== 'AbortError' && version === current) {
          close(); status.textContent = 'Sugestões indisponíveis. Digite o termo e use Bora encontrar.';
        }
      }
    }, 280);
  });
  input.addEventListener('keydown', event => {
    if (event.key === 'ArrowDown' && !dropdown.hidden) { event.preventDefault(); dropdown.querySelector('a')?.focus(); }
    if (event.key === 'Escape') { ++version; request?.abort(); clearTimeout(timer); close(); }
  });
  dropdown.addEventListener('keydown', event => {
    const links = [...dropdown.querySelectorAll('a')];
    const index = links.indexOf(document.activeElement);
    if (event.key === 'ArrowDown') { event.preventDefault(); links[(index + 1) % links.length]?.focus(); }
    if (event.key === 'ArrowUp') { event.preventDefault(); if (index === 0) input.focus(); else links[index - 1]?.focus(); }
    if (event.key === 'Escape') { close(); input.focus(); }
  });
  dropdown.addEventListener('focusin', event => {
    dropdown.querySelectorAll('[role=option]').forEach(option => option.setAttribute('aria-selected', String(option === event.target.closest('[role=option]'))));
  });
  document.addEventListener('click', event => { if (!event.target.closest('.para-search-query')) close(); });
  document.addEventListener('focusin', event => { if (!event.target.closest('.para-search-query')) close(); });
})();
(() => {
  document.addEventListener('submit', event => {
    const message = event.submitter?.dataset.confirm || event.target.dataset.confirm;
    if (message && !window.confirm(message)) event.preventDefault();
  });
  document.addEventListener('click', async event => {
    const button = event.target.closest('[data-copy-target],[data-copy-url]');
    if (!button) return;
    const value = button.dataset.copyTarget ? document.getElementById(button.dataset.copyTarget)?.value : location.href;
    if (!value) return;
    try { await navigator.clipboard.writeText(value); button.textContent = 'Copiado!'; }
    catch { button.textContent = 'Não foi possível copiar. Selecione e copie o texto.'; }
  });
})();
