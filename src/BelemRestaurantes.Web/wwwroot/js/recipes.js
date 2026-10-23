(() => {
  'use strict';
  const recipe = document.querySelector('[data-recipe]');
  if (!recipe) return;
  const checks = [...recipe.querySelectorAll('[data-recipe-check]')];
  const steps = checks.filter(input => input.dataset.recipeCheck === 'step');
  const status = document.getElementById('recipe-progress');
  const update = () => { status.textContent = `${steps.filter(input => input.checked).length} de ${steps.length} etapas marcadas. As marcações ficam somente nesta página.`; };
  checks.forEach(input => input.addEventListener('change', update));
  recipe.querySelector('[data-recipe-reset]')?.addEventListener('click', () => { checks.forEach(input => { input.checked = false; }); update(); });
  recipe.querySelector('[data-recipe-print]')?.addEventListener('click', () => window.print());
  const kitchen = recipe.querySelector('[data-recipe-kitchen]');
  kitchen?.addEventListener('click', () => {
    const active = document.documentElement.classList.toggle('recipe-kitchen');
    kitchen.setAttribute('aria-pressed', String(active));
  });
  const shopping = recipe.querySelector('[data-shopping]');
  const portion = shopping?.querySelector('[data-portion-input]');
  const baseServings = Number(shopping?.dataset.servings || 0);
  const originals = [...(shopping?.querySelectorAll('[data-ingredient]') ?? [])].map(node => node.textContent || '');
  const scaleLine = (line, factor) => line.replace(/\d+(?:[.,]\d+)?/, match => {
    const value = Number(match.replace(',', '.'));
    if (!Number.isFinite(value)) return match;
    const scaled = Math.round(value * factor * 100) / 100;
    return String(scaled).replace('.', ',');
  });
  const shopKey = shopping ? 'belem:shopping:' + (shopping.dataset.recipeSlug || location.pathname) : '';
  const shopBoxes = [...(shopping?.querySelectorAll('[data-shop-item]') ?? [])];
  const applyScale = () => {
    const next = Number(portion?.value);
    if (!shopping || !Number.isFinite(next) || next < 1 || baseServings < 1) return;
    const factor = next / baseServings;
    shopping.querySelectorAll('[data-ingredient]').forEach((node, index) => { node.textContent = scaleLine(originals[index], factor); });
  };
  const persistShopping = () => {
    if (!shopping) return;
    try { localStorage.setItem(shopKey, JSON.stringify({ checks: shopBoxes.map(box => box.checked), portions: portion ? Number(portion.value) : null })); } catch { /* A lista continua editável nesta visita. */ }
  };
  try {
    const saved = JSON.parse(localStorage.getItem(shopKey) || 'null');
    if (saved && Array.isArray(saved.checks)) shopBoxes.forEach((box, index) => { box.checked = !!saved.checks[index]; });
    if (saved && portion && Number.isFinite(saved.portions) && saved.portions >= 1) portion.value = String(saved.portions);
  } catch { /* Memória inválida não altera a lista. */ }
  applyScale();
  portion?.addEventListener('input', () => { applyScale(); persistShopping(); });
  shopBoxes.forEach(box => box.addEventListener('change', persistShopping));
  recipe.querySelector('[data-recipe-reset]')?.addEventListener('click', () => {
    shopBoxes.forEach(box => { box.checked = false; });
    if (portion) portion.value = String(baseServings || 1);
    applyScale();
    try { localStorage.removeItem(shopKey); } catch { /* A limpeza visual permanece. */ }
  });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && document.documentElement.classList.contains('recipe-kitchen')) {
      document.documentElement.classList.remove('recipe-kitchen'); kitchen?.setAttribute('aria-pressed', 'false'); kitchen?.focus();
    }
  });
})();
