(() => {
  'use strict';
  const root = document.documentElement;
  const tools = document.querySelector('.reading-tools');
  if (!tools) return;

  const steps = ['100', '125', '150', '175', '200'];
  const defaults = {
    size: '100',
    contrast: false,
    lineSpacing: false,
    letterSpacing: false,
    links: false,
    motion: false,
    distraction: false
  };

  let preferences = { ...defaults };

  try {
    const saved = JSON.parse(localStorage.getItem('belem-reading') || 'null');
    if (saved && steps.includes(String(saved.size))) preferences.size = String(saved.size);
    if (String(saved?.size) === '115') preferences.size = '125';
    for (const key of ['contrast', 'lineSpacing', 'letterSpacing', 'links', 'motion', 'distraction']) {
      preferences[key] = saved?.[key] === true;
    }
  } catch {
    /* Graceful fallback when local storage is disabled or private */
  }

  const sizeSelect = tools.querySelector('[data-reading-size]');
  const btnDecrease = tools.querySelector('[data-reading-decrease]');
  const btnIncrease = tools.querySelector('[data-reading-increase]');
  const statusEl = tools.querySelector('[data-reading-status]');
  const btnReset = tools.querySelector('[data-reading-reset]');
  const summaryEl = tools.querySelector('summary');

  const boolKeys = [
    { key: 'contrast', attr: 'contrast' },
    { key: 'lineSpacing', attr: 'line-spacing' },
    { key: 'letterSpacing', attr: 'letter-spacing' },
    { key: 'links', attr: 'links' },
    { key: 'motion', attr: 'motion' },
    { key: 'distraction', attr: 'distraction' }
  ];

  const apply = (message = null) => {
    root.dataset.textSize = preferences.size;
    root.classList.toggle('reading-contrast', preferences.contrast);
    root.classList.toggle('reading-line-spacing', preferences.lineSpacing);
    root.classList.toggle('reading-letter-spacing', preferences.letterSpacing);
    root.classList.toggle('reading-links', preferences.links);
    root.classList.toggle('reading-motion', preferences.motion);
    root.classList.toggle('reading-distraction', preferences.distraction);

    if (sizeSelect) sizeSelect.value = preferences.size;

    boolKeys.forEach(({ key, attr }) => {
      const input = tools.querySelector(`[data-reading-${attr}]`);
      if (input) input.checked = preferences[key];
    });

    try {
      localStorage.setItem('belem-reading', JSON.stringify(preferences));
    } catch {
      /* Non-essential storage fallback */
    }

    if (message && statusEl) {
      statusEl.textContent = message;
    }
  };

  if (sizeSelect) {
    sizeSelect.addEventListener('change', () => {
      preferences.size = sizeSelect.value;
      apply(`Tamanho do texto ajustado para ${preferences.size}%.`);
    });
  }

  if (btnDecrease) {
    btnDecrease.addEventListener('click', () => {
      const idx = steps.indexOf(preferences.size);
      if (idx > 0) {
        preferences.size = steps[idx - 1];
        apply(`Tamanho do texto reduzido para ${preferences.size}%.`);
      }
    });
  }

  if (btnIncrease) {
    btnIncrease.addEventListener('click', () => {
      const idx = steps.indexOf(preferences.size);
      if (idx < steps.length - 1) {
        preferences.size = steps[idx + 1];
        apply(`Tamanho do texto aumentado para ${preferences.size}%.`);
      }
    });
  }

  boolKeys.forEach(({ key, attr }) => {
    const input = tools.querySelector(`[data-reading-${attr}]`);
    if (input) {
      input.addEventListener('change', (e) => {
        preferences[key] = e.target.checked;
        const label = input.closest('label')?.textContent?.trim() || key;
        apply(`Preferência alterada: ${label} está ${preferences[key] ? 'ativado' : 'desativado'}.`);
      });
    }
  });

  if (btnReset) {
    btnReset.addEventListener('click', () => {
      preferences = { ...defaults };
      apply('Todas as preferências de leitura foram restauradas ao padrão.');
    });
  }

  // Keyboard navigation: Escape key closes panel and restores focus to summary
  tools.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && tools.open) {
      tools.open = false;
      if (summaryEl) summaryEl.focus();
    }
  });

  apply();
})();
