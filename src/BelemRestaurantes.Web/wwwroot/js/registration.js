(() => {
  const summary = document.querySelector('[data-registration-errors].validation-summary-errors');
  if (!summary) return;
  const company = document.querySelector('input[name="Cnpj"].input-validation-error,input[name="OrganizationName"].input-validation-error');
  if (company) company.closest('details').open = true;
  for (const field of document.querySelectorAll('.input-validation-error')) field.setAttribute('aria-invalid', 'true');
  for (const message of document.querySelectorAll('[data-valmsg-for].field-validation-error')) {
    const field = [...document.querySelectorAll('input')].find(input => input.name === message.dataset.valmsgFor && input.type !== 'hidden');
    if (!field) continue;
    message.id = 'registration-error-' + field.name;
    field.setAttribute('aria-invalid', 'true');
    field.setAttribute('aria-describedby', [field.getAttribute('aria-describedby'), message.id].filter(Boolean).join(' '));
  }
  summary.focus();
})();
