(() => {
  'use strict';
  const prefix = 'belem-bulk:';
  const prepared = document.querySelector('[data-bulk-prepared-key]');
  if (prepared) {
    try { sessionStorage.removeItem(prefix + prepared.dataset.bulkPreparedKey); } catch { /* Storage is optional. */ }
  }
  const busy = document.querySelector('[data-bulk-loading]');
  document.querySelectorAll('[data-bulk-pagination] a').forEach(link => link.addEventListener('click', event => {
    if (event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    if (busy) busy.textContent = 'Carregando a página…';
  }));
  const form = document.querySelector('[data-bulk-form]');
  if (!form) return;
  const items = [...form.querySelectorAll('input[name=ids]')];
  const all = form.querySelector('input[name=allFiltered]');
  const count = form.querySelector('[data-bulk-count]');
  const hidden = form.querySelector('[data-bulk-hidden]');
  const note = form.querySelector('[data-bulk-persist-note]');
  const submit = form.querySelector('button[type=submit]');
  const key = prefix + form.dataset.selectionKey;
  const limit = Math.max(1, Math.min(2000, Number(form.dataset.batchLimit) || 1000));
  const visible = new Set(items.map(item => item.value));
  let selected = new Set();
  let storageAvailable = true;
  const restore = () => {
    selected = new Set(); all.checked = false;
    try {
      const stored = JSON.parse(sessionStorage.getItem(key) || 'null');
      if (Array.isArray(stored?.ids)) selected = new Set(stored.ids.filter(id => typeof id === 'string' && /^[1-9][0-9]{0,19}$/.test(id)).slice(0, limit));
      all.checked = stored?.all === true;
    } catch { storageAvailable = false; }
  };
  restore();
  const update = () => {
    items.forEach(item => {
      item.checked = selected.has(item.value);
      item.disabled = all.checked;
      item.closest('[data-bulk-row]')?.classList.toggle('is-selected', item.checked || all.checked);
    });
    hidden.replaceChildren();
    if (!all.checked) selected.forEach(id => {
      if (visible.has(id)) return;
      const input = document.createElement('input');
      input.type = 'hidden'; input.name = 'ids'; input.value = id;
      hidden.appendChild(input);
    });
    const offPage = [...selected].filter(id => !visible.has(id)).length;
    count.textContent = all.checked
      ? `Próximo lote do filtro: até ${form.dataset.filterCount} cadastros.`
      : `${selected.size} selecionados${offPage ? ` · ${offPage} em outras páginas` : ''}.`;
    if (submit) submit.disabled = !all.checked && selected.size === 0;
    try { sessionStorage.setItem(key, JSON.stringify({ ids: [...selected], all: all.checked })); }
    catch { storageAvailable = false; }
    note.hidden = false;
    note.textContent = storageAvailable
      ? 'Sua seleção acompanha as páginas deste filtro enquanto esta aba estiver aberta.'
      : 'O navegador não permitiu salvar a seleção entre páginas. Conclua a revisão desta página antes de navegar.';
  };
  items.forEach(item => item.addEventListener('change', () => {
    if (item.checked && selected.size >= limit && !selected.has(item.value)) {
      item.checked = false; count.textContent = `Limite de ${limit} cadastros atingido. Revise este lote antes de selecionar mais.`; return;
    }
    item.checked ? selected.add(item.value) : selected.delete(item.value);
    update();
  }));
  all.addEventListener('change', update);
  form.querySelector('[data-bulk-page]').addEventListener('click', () => {
    all.checked = false;
    items.forEach(item => { if (selected.size < limit) selected.add(item.value); });
    update();
  });
  form.querySelector('[data-bulk-clear]').addEventListener('click', () => { all.checked = false; selected.clear(); update(); });
  form.addEventListener('submit', () => {
    form.setAttribute('aria-busy', 'true');
    if (submit) submit.disabled = true;
    count.textContent = 'Preparando a revisão. Aguarde antes de enviar novamente.';
  });
  window.addEventListener('pageshow', event => { if(event.persisted){location.reload();return;} restore(); form.removeAttribute('aria-busy'); if (busy) busy.textContent = ''; update(); });
  update();
})();
