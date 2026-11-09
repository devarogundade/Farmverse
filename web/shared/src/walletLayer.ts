/**
 * near-connect draws its wallet list (and wallet iframes) in a div on <body> with a big z-index. Our popups are
 * native modal <dialog>s, which live in the browser's top layer: above any z-index, and everything outside them
 * is inert. So a wallet list opened from a popup showed underneath it and couldn't be clicked.
 *
 * This moves each near-connect popup into a <dialog> of its own and opens that modally while the popup is
 * showing, so it lands on top of the top layer (the newest modal wins). Call it once per page; repeat calls
 * do nothing.
 */
const POPUP = 'hot-connector-popup';
const LAYER = 'fv-wallet-layer';

let started = false;

export function liftWalletPopups(): void {
  if (started || typeof document === 'undefined') return;
  started = true;

  const style = document.createElement('style');
  style.textContent = `
dialog.${LAYER} { position: fixed; inset: 0; width: 100%; height: 100%; max-width: none; max-height: none; margin: 0; padding: 0; border: 0; background: transparent; overflow: visible; }
dialog.${LAYER}::backdrop { background: transparent; }`;
  document.head.append(style);

  const lift = (root: HTMLElement) => {
    if (root.parentElement?.classList.contains(LAYER)) return;
    const layer = document.createElement('dialog');
    layer.className = LAYER;
    // near-connect closes its popup on Escape itself; keep the layer from closing on its own.
    layer.addEventListener('cancel', (e) => e.preventDefault());
    document.body.append(layer);
    layer.append(root);

    // showing / hidden: near-connect toggles the root's display
    const sync = () => {
      const showing = root.style.display !== 'none';
      if (showing && !layer.open) layer.showModal();
      else if (!showing && layer.open) layer.close();
    };
    const shown = new MutationObserver(sync);
    shown.observe(root, { attributes: true, attributeFilter: ['style'] });
    // the popup is gone (near-connect removes its root): drop the layer too
    const gone = new MutationObserver(() => {
      if (layer.contains(root)) return;
      shown.disconnect();
      gone.disconnect();
      if (layer.open) layer.close();
      layer.remove();
    });
    gone.observe(layer, { childList: true });
    sync();
  };

  const scan = () => document.querySelectorAll<HTMLElement>(`body > .${POPUP}`).forEach(lift);
  new MutationObserver(scan).observe(document.body, { childList: true });
  scan();
}
