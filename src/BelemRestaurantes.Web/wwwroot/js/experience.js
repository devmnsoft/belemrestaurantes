(() => {
  const input = document.getElementById('photoInput');
  const preview = document.getElementById('previewContainer');
  input?.addEventListener('change', () => {
    preview?.replaceChildren();
    for (const file of [...input.files].slice(0, 5)) {
      if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type) || file.size > 10 * 1024 * 1024) continue;
      const reader = new FileReader();
      reader.addEventListener('load', () => {
        const image = document.createElement('img'); image.src = reader.result;
        image.className = 'para-image-preview'; image.alt = 'Prévia da foto selecionada'; preview?.append(image);
      });
      reader.readAsDataURL(file);
    }
  });
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
