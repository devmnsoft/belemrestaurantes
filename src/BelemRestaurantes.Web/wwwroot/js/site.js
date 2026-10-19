"use strict";
const el = id => document.getElementById(id);
let places = [];
function safeUrl(value) {
  try { const url = new URL(value); return ['https:', 'http:'].includes(url.protocol) ? url.href : null; }
  catch { return null; }
}
function elem(tag, klass, content) {
  const node = document.createElement(tag);
  if (klass) node.className = klass;
  if (content !== undefined) node.textContent = content;
  return node;
}
if (el('go') && el('query') && el('client-text')) {
  const text = el('client-text').dataset;
  let activeRequest;
  let totalCount = 0;
  async function load() {
    activeRequest?.abort();
    const request = new AbortController();
    activeRequest = request;
    el('status').textContent = text.loading;
    try {
      const params = new URLSearchParams({ search: el('query').value, city: el('city').value, pageSize: '60' });
      const response = await fetch('/api/restaurants?' + params, { signal: request.signal });
      if (!response.ok) throw new Error();
      const result = await response.json();
      if (!Array.isArray(result.items)) throw new Error();
      places = result.items;
      totalCount = result.totalCount;
      render();
      populateAutosuggest(places);
    } catch (error) {
      if (error.name === 'AbortError') return;
      places = [];
      el('cards').replaceChildren();
      el('destination')?.replaceChildren(new Option(text.choose, ''));
      el('autosuggest')?.replaceChildren();
      el('status').textContent = text.unavailable;
    }
  }
  function render() {
    const cards = el('cards'), destination = el('destination');
    cards.replaceChildren();
    destination?.replaceChildren(new Option(text.choose, ''));
    el('status').textContent = text.results.replace('{0}', new Intl.NumberFormat(document.documentElement.lang).format(totalCount));
    places.forEach(place => {
      const card = elem('article', 'card'), photo = elem('div', 'photo');
      const photoUrl = safeUrl(place.photoUrl);
      if (place.photoUrl && photoUrl) {
        const image = elem('img');
        image.src = photoUrl;
        image.alt = text.photo.replace('{0}', place.name);
        image.loading = 'lazy'; image.referrerPolicy = 'no-referrer';
        image.onerror = () => photo.replaceChildren(elem('span', '', text.noPhoto));
        photo.append(image);
      } else photo.append(elem('span', '', text.noPhoto));
      const inside = elem('div', 'inside'), title = elem('h3', '');
      const link = elem('a', '', place.name);
      link.href = '/establishment/' + encodeURIComponent(String(place.id));
      title.append(link);
      inside.append(title, elem('p', '', [place.category, place.city, place.neighborhood].filter(Boolean).join(' · ')),
        elem('p', '', place.address || text.address));
      for (const [name, value] of [[text.website, place.website], ['Instagram', place.instagram], ['Facebook', place.facebook], ['TikTok', place.tiktok], ['YouTube', place.youtube]]) {
        const url = safeUrl(value);
        if (!url) continue;
        const anchor = elem('a', '', name + ' ↗');
        anchor.href = url; anchor.target = '_blank'; anchor.rel = 'noopener noreferrer';
        inside.append(anchor, document.createTextNode('  '));
      }
      card.append(photo, inside); cards.append(card);
      destination?.add(new Option(place.name + ' — ' + place.city, place.id));
    });
  }
  el('go').addEventListener('click', load);
  el('city').addEventListener('change', load);
  el('query').addEventListener('keydown', event => { if (event.key === 'Enter') load(); });
  let timeout;
  el('query').addEventListener('input', () => { clearTimeout(timeout); timeout = setTimeout(load, 350); });
  el('route')?.addEventListener('click', () => {
    const place = places.find(item => String(item.id) === el('destination').value);
    if (!place) return window.alert(text.choose);
    const url = new URL('https://www.google.com/maps/dir/');
    url.searchParams.set('api', '1');
    url.searchParams.set('destination', [place.address, place.city, 'Pará Brasil'].filter(Boolean).join(', '));
    const origin = el('start').value.trim();
    if (origin) url.searchParams.set('origin', origin);
    url.searchParams.set('travelmode', 'transit');
    window.open(url.href, '_blank', 'noopener,noreferrer');
  });
  el('copyPix')?.addEventListener('click', async () => {
    try { await navigator.clipboard.writeText('18160057000113'); window.alert(text.pixCopied); }
    catch { window.alert('PIX: 18.160.057/0001-13'); }
  });
  el('suggestForm')?.addEventListener('submit', async event => {
    event.preventDefault();
    const form = event.currentTarget, status = el('suggestStatus'), submit = form.querySelector('button[type="submit"]');
    submit.disabled = true; status.textContent = text.sending;
    try {
      const response = await fetch('/api/restaurants/suggest', { method: 'POST', body: new FormData(form) });
      if (!response.ok) throw new Error();
      status.textContent = text.received; form.reset();
    } catch { status.textContent = text.sendFailed; }
    finally { submit.disabled = false; }
  });
  function populateAutosuggest(items) {
    const list = el('autosuggest');
    if (!list) return;
    list.replaceChildren(...items.slice(0, 12).map(place => new Option(place.name, place.name)));
  }
  load();
}

// Cuia IA Sommelier Widget (Estabelecimentos)
document.addEventListener('DOMContentLoaded', () => {
  const sommelierBtn = document.getElementById('btn-ask-sommelier');
  if (sommelierBtn) {
    sommelierBtn.addEventListener('click', async () => {
      const restId = sommelierBtn.dataset.restaurantId;
      const adviceEl = document.getElementById('ai-restaurant-advice');
      if (!restId || !adviceEl) return;
      sommelierBtn.disabled = true;
      const originalText = sommelierBtn.innerHTML;
      sommelierBtn.innerHTML = '<span>Consultando Chef Cuia IA... ⏳</span>';
      try {
        const res = await fetch(`/api/cuia-ia/dica-restaurante/${encodeURIComponent(restId)}`);
        if (!res.ok) throw new Error('Não foi possível obter a dica no momento.');
        const data = await res.json();
        adviceEl.innerHTML = `
          <div style="padding:14px;background:rgba(255,255,255,0.08);border-radius:10px;margin-top:10px;">
            <p style="margin:0 0 8px;font-size:1.05rem;">${data.advice.replace(/\n/g, '<br/>')}</p>
            <small style="opacity:0.8;color:#ffe2a0;">⚡ Fonte: ${data.engine} · Pará à Mesa</small>
          </div>
        `;
        sommelierBtn.innerHTML = '<span>Dica Atualizada ✨</span>';
      } catch (err) {
        adviceEl.innerHTML = `<p style="color:#ffb3a0;">${err.message || 'Erro ao consultar dica.'}</p>`;
        sommelierBtn.innerHTML = originalText;
      } finally {
        sommelierBtn.disabled = false;
      }
    });
  }

  // Cuia IA Chef Widget (Receitas)
  const recipeChefBtn = document.getElementById('btn-ask-recipe-chef');
  if (recipeChefBtn) {
    recipeChefBtn.addEventListener('click', async () => {
      const slug = recipeChefBtn.dataset.recipeSlug;
      const adviceEl = document.getElementById('ai-recipe-advice');
      if (!slug || !adviceEl) return;
      recipeChefBtn.disabled = true;
      const originalText = recipeChefBtn.innerHTML;
      recipeChefBtn.innerHTML = '<span>Consultando Chef Cuia IA... ⏳</span>';
      try {
        const res = await fetch(`/api/cuia-ia/dica-receita/${encodeURIComponent(slug)}`);
        if (!res.ok) throw new Error('Não foi possível obter a dica no momento.');
        const data = await res.json();
        adviceEl.innerHTML = `
          <div style="padding:14px;background:rgba(255,255,255,0.08);border-radius:10px;margin-top:10px;">
            <p style="margin:0 0 8px;font-size:1.05rem;">${data.advice.replace(/\n/g, '<br/>')}</p>
            <small style="opacity:0.8;color:#ffe2a0;">⚡ Fonte: ${data.engine} · Caderno do Pará</small>
          </div>
        `;
        recipeChefBtn.innerHTML = '<span>Dica do Chef Pronta ✨</span>';
      } catch (err) {
        adviceEl.innerHTML = `<p style="color:#ffb3a0;">${err.message || 'Erro ao consultar dica.'}</p>`;
        recipeChefBtn.innerHTML = originalText;
      } finally {
        recipeChefBtn.disabled = false;
      }
    });
  }

  // Quick Share Button Handler
  document.querySelectorAll('[data-copy-url="true"]').forEach(btn => {
    btn.addEventListener('click', async () => {
      const url = window.location.href;
      if (navigator.share) {
        try {
          await navigator.share({ title: document.title, url });
          return;
        } catch (_) {}
      }
      try {
        await navigator.clipboard.writeText(url);
        const originalText = btn.textContent;
        btn.textContent = '✓ Link copiado!';
        setTimeout(() => { btn.textContent = originalText; }, 2500);
      } catch {
        window.prompt('Copie o link:', url);
      }
    });
  });
});
