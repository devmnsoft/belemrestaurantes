(() => {
  'use strict';
  const root = document.documentElement;
  const tools = document.querySelector('.reading-tools');
  if (!tools) return;
  const defaults = { size: '100', contrast: false, links: false, motion: false };
  let preferences = { ...defaults };
  try {
    const saved = JSON.parse(localStorage.getItem('belem-reading') || 'null');
    if (saved && ['100', '125', '150'].includes(saved.size)) preferences.size = saved.size;
    for (const key of ['contrast', 'links', 'motion']) preferences[key] = saved?.[key] === true;
  } catch { /* Reading controls still work when browser storage is unavailable. */ }
  const size = tools.querySelector('[data-reading-size]');
  const status = tools.querySelector('[data-reading-status]');
  const apply = (announce = false) => {
    root.dataset.textSize = preferences.size;
    root.classList.toggle('reading-contrast', preferences.contrast);
    root.classList.toggle('reading-links', preferences.links);
    root.classList.toggle('reading-motion', preferences.motion);
    size.value = preferences.size;
    for (const key of ['contrast', 'links', 'motion']) tools.querySelector(`[data-reading-${key}]`).checked = preferences[key];
    try { localStorage.setItem('belem-reading', JSON.stringify(preferences)); } catch { /* Nonessential storage. */ }
    if (announce) status.textContent = 'Preferências de leitura atualizadas.';
  };
  size.addEventListener('change', () => { preferences.size = size.value; apply(true); });
  for (const key of ['contrast', 'links', 'motion']) tools.querySelector(`[data-reading-${key}]`).addEventListener('change', event => { preferences[key] = event.target.checked; apply(true); });
  tools.querySelector('[data-reading-reset]').addEventListener('click', () => { preferences = { ...defaults }; apply(true); });
  apply();
})();
