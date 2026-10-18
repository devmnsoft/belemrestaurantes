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
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && document.documentElement.classList.contains('recipe-kitchen')) {
      document.documentElement.classList.remove('recipe-kitchen'); kitchen?.setAttribute('aria-pressed', 'false'); kitchen?.focus();
    }
  });
})();
