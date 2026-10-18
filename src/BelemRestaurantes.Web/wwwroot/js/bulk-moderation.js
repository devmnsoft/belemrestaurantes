(() => {
  const form = document.querySelector('[data-bulk-form]');
  if (!form) return;
  const items = [...form.querySelectorAll('input[name=ids]')];
  const all = form.querySelector('input[name=allFiltered]');
  const count = form.querySelector('[data-bulk-count]');
  const update = () => { count.textContent = all.checked ? 'Todos os resultados do filtro serão conferidos na próxima etapa.' : `${items.filter(item => item.checked).length} cadastros selecionados nesta página.`; };
  form.addEventListener('change', update);
  form.querySelector('[data-bulk-page]').addEventListener('click', () => { all.checked = false; items.forEach(item => { item.checked = true; }); update(); });
  form.querySelector('[data-bulk-clear]').addEventListener('click', () => { all.checked = false; items.forEach(item => { item.checked = false; }); update(); });
})();
