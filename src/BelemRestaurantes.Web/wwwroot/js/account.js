"use strict";
for (const form of document.querySelectorAll('[data-token-form]')) {
  const field = form.querySelector('[data-account-token]');
  const token = new URLSearchParams(window.location.hash.slice(1)).get('token');
  if (token && /^[A-Fa-f0-9]{64}$/.test(token)) field.value = token;
  if (window.location.hash) window.history.replaceState(null, '', window.location.pathname + window.location.search);
  form.querySelector('button[type="submit"]').disabled = !/^[A-Fa-f0-9]{64}$/.test(field.value);
}
