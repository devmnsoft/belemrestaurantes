(() => {
 'use strict';
 const dialog=document.getElementById('qrCodeModal');if(!dialog)return;
 const trigger=document.querySelector('[data-menu-code-open]');
 trigger?.addEventListener('click',()=>{
  const image=dialog.querySelector('[data-menu-code-image]');if(image&&!image.src)image.src=image.dataset.src;
  dialog.showModal();
 });
 dialog.querySelector('[data-menu-code-close]')?.addEventListener('click',()=>dialog.close());
 dialog.addEventListener('close',()=>trigger?.focus());
})();
