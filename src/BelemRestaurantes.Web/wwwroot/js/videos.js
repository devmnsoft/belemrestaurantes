(() => {
  document.querySelectorAll('[data-video-embed]').forEach(button => button.addEventListener('click', () => {
    let url;
    try { url = new URL(button.dataset.videoEmbed); }
    catch { return; }
    if (url.origin !== 'https://www.youtube-nocookie.com' || !/^\/embed\/[A-Za-z0-9_-]{11}$/.test(url.pathname)) return;
    const frame = document.createElement('iframe');
    frame.src = url.href;
    frame.title = button.dataset.videoTitle || 'Vídeo do restaurante';
    frame.allowFullscreen = true;
    frame.loading = 'lazy';
    frame.referrerPolicy = 'strict-origin-when-cross-origin';
    frame.allow = 'encrypted-media; picture-in-picture; fullscreen';
    frame.tabIndex = 0;
    button.replaceWith(frame);
    frame.focus();
  }));
})();
