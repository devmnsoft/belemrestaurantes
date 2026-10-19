(() => {
  'use strict';

  const input = document.getElementById('photoInput');
  const preview = document.getElementById('previewContainer');
  const maxPhotos = 5;
  const maxBytes = 10 * 1024 * 1024;
  const allowedTypes = new Set(['image/jpeg', 'image/png', 'image/webp']);

  let selectedFiles = [];

  function syncInput() {
    if (!input) return;
    const transfer = new DataTransfer();
    selectedFiles.forEach(file => transfer.items.add(file));
    input.files = transfer.files;
  }

  function renderPreviews() {
    if (!preview) return;
    preview.replaceChildren();

    selectedFiles.forEach((file, index) => {
      const wrapper = document.createElement('div');
      wrapper.style.position = 'relative';
      wrapper.style.width = '112px';
      wrapper.style.height = '92px';
      wrapper.style.borderRadius = '10px';
      wrapper.style.overflow = 'hidden';
      wrapper.style.border = '1px solid #cddccf';
      wrapper.style.background = '#f7f2e7';

      const image = document.createElement('img');
      image.className = 'para-image-preview';
      image.alt = `Prévia da foto selecionada: ${file.name}`;
      image.style.width = '100%';
      image.style.height = '100%';
      image.style.objectFit = 'cover';
      const reader = new FileReader();
      reader.addEventListener('load', () => { image.src = String(reader.result || ''); });
      reader.readAsDataURL(file);

      const remove = document.createElement('button');
      remove.type = 'button';
      remove.textContent = '×';
      remove.setAttribute('aria-label', `Remover ${file.name}`);
      remove.style.position = 'absolute';
      remove.style.top = '4px';
      remove.style.right = '4px';
      remove.style.width = '28px';
      remove.style.height = '28px';
      remove.style.border = '0';
      remove.style.borderRadius = '999px';
      remove.style.background = 'rgba(20,63,50,.85)';
      remove.style.color = '#fff6df';
      remove.style.cursor = 'pointer';
      remove.style.fontWeight = '700';
      remove.addEventListener('click', () => {
        selectedFiles.splice(index, 1);
        syncInput();
        renderPreviews();
        if (status) status.textContent = `${selectedFiles.length} de ${maxPhotos} fotos selecionadas.`;
      });

      wrapper.append(image, remove);
      preview.append(wrapper);
    });

    const status = document.createElement('p');
    status.style.flexBasis = '100%';
    status.style.margin = '0';
    status.style.fontSize = '13px';
    status.style.color = '#526359';
    status.textContent = selectedFiles.length
      ? `${selectedFiles.length} de ${maxPhotos} fotos selecionadas. Envie apenas imagens nítidas e autorizadas.`
      : 'Nenhuma foto selecionada ainda.';
    preview.append(status);
  }

  if (input && preview) {
    input.addEventListener('change', () => {
      const incoming = [...(input.files || [])];
      let invalid = 0;
      let ignoredForLimit = 0;

      for (const file of incoming) {
        if (!allowedTypes.has(file.type) || file.size <= 0 || file.size > maxBytes) {
          invalid += 1;
          continue;
        }
        if (selectedFiles.length >= maxPhotos) {
          ignoredForLimit += 1;
          continue;
        }
        selectedFiles.push(file);
      }

      syncInput();
      renderPreviews();

      const status = preview.querySelector('p');
      const messages = [];
      if (invalid) messages.push(`${invalid} arquivo(s) ignorado(s) por formato ou tamanho.`);
      if (ignoredForLimit) messages.push(`Limite de ${maxPhotos} fotos atingido; os demais foram ignorados.`);
      if (messages.length && status) status.textContent = `${status.textContent} ${messages.join(' ')}`;
    });

    renderPreviews();
  }

  const copy = document.getElementById('copyShareBtn');
  copy?.addEventListener('click', async () => {
    try { await navigator.clipboard.writeText(copy.dataset.shareUrl || location.href); copy.textContent = copy.dataset.copied || 'Link copiado'; }
    catch { copy.textContent = 'Selecione e copie o endereço desta página.'; }
  });
  const share = document.getElementById('nativeShareBtn');
  if (share && navigator.share) {
    share.style.display = 'inline-block';
    share.addEventListener('click', () => navigator.share({title:document.title,text:share.dataset.shareText,url:share.dataset.shareUrl || location.href}).catch(() => {}));
  }
})();
