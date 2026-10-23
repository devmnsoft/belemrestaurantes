(() => {
  'use strict';
  const timers=new WeakMap(),watched=new WeakSet();
  function fallback(img){
    if(!(img instanceof HTMLImageElement))return;
    if(img.dataset.imageFallback){
      const note=img.nextElementSibling;
      if(note?.classList.contains('image-fallback-note'))note.style.minHeight=Math.min(img.height||160,360)+'px';
      img.hidden=true;return;
    }
    img.dataset.imageFallback='true';clearTimeout(timers.get(img));
    img.removeAttribute('srcset');img.removeAttribute('sizes');
    img.alt='Ilustração de apoio: imagem original indisponível';
    img.classList.add('image-unavailable');
    const note=document.createElement('span');note.className='image-fallback-note';note.textContent='Imagem indisponível · ilustração de apoio';img.after(note);
    img.src='/images/image-unavailable.svg';
  }
  const observer='IntersectionObserver' in window?new IntersectionObserver(entries=>{
    for(const entry of entries)if(entry.isIntersecting){start(entry.target);observer.unobserve(entry.target);}
  },{rootMargin:'200px'}):null;
  function start(img){
    if(img.complete){if(!img.naturalWidth)fallback(img);return;}
    // Apply a finite wait only to remote images that have approached the viewport.
    if(/^https?:/.test(img.currentSrc||img.src)&&new URL(img.src,location.href).origin!==location.origin)
      timers.set(img,setTimeout(()=>{if(!img.complete)fallback(img);},12000));
  }
  function watch(root){
    const imgs=root instanceof HTMLImageElement?[root]:root.querySelectorAll?.('img')||[];
    for(const img of imgs){if(watched.has(img))continue;watched.add(img);if(observer)observer.observe(img);else start(img);}
  }
  document.addEventListener('error',event=>fallback(event.target),true);
  document.addEventListener('load',event=>{if(event.target instanceof HTMLImageElement)clearTimeout(timers.get(event.target));},true);
  watch(document);
  new MutationObserver(records=>{for(const record of records)for(const node of record.addedNodes)if(node.nodeType===1)watch(node);}).observe(document.documentElement,{childList:true,subtree:true});
})();
