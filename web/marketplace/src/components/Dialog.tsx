import { sprite } from '@farmverse/shared/gameTheme';
import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { Toaster, toast as sonner } from 'sonner';
import { useSettling } from '../chain/settle';
import { useWallet } from '../chain/wallet';
import { PortalContainer } from './Select';

/**
 * A game-style popup on the native <dialog> (focus trap, Esc and the top layer come built in, and the
 * top layer sits above the WebGL canvas). Click outside or press Esc to close.
 */
export function Dialog({ open, onClose, title, icon, children }: { open: boolean; onClose: () => void; title: string; icon?: string; children: ReactNode }) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const [el, setEl] = useState<HTMLDialogElement | null>(null);
  useEffect(() => setEl(ref.current), []);
  useEffect(() => {
    const d = ref.current;
    if (!d) return;
    if (open && !d.open) {
      d.showModal();
      // React's autoFocus runs before showModal, so focus the first field (if any) ourselves.
      d.querySelector<HTMLElement>('input, textarea')?.focus();
    }
    if (!open && d.open) d.close();
  }, [open]);
  return (
    <dialog
      ref={ref}
      className="fv-dialog"
      aria-labelledby={titleId}
      onCancel={(e) => {
        e.preventDefault();
        // Esc inside an open dropdown closes the dropdown, not the dialog.
        if (!ref.current?.querySelector('.fv-select-menu')) onClose();
      }}
      onClick={(e) => e.target === ref.current && onClose()}
    >
      {open && (
        <div className="dlg fv-panel">
          <header className="dlg-head">
            {icon && <img className="dlg-icon" src={sprite(icon)} alt="" />}
            <h2 id={titleId} className="fv-title">
              {title}
            </h2>
            <button className="fv-chip dlg-close" onClick={onClose} aria-label="Close">
              <img className="fv-icon" src={sprite('Icon_PictoIcon_Close')} alt="" />
            </button>
          </header>
          <PortalContainer.Provider value={el}>
            <div className="dlg-body">{children}</div>
          </PortalContainer.Provider>
        </div>
      )}
    </dialog>
  );
}

/** "Confirm in your wallet" while an action is pending ("Working on it…" for Google and email: nothing to confirm). */
export function WalletWait({ text }: { text?: string }) {
  // after the wallet: the change is on chain, the market is catching up (chain/settle.ts)
  const settling = useSettling();
  const { social } = useWallet();
  text ??= social ? 'Working on it…' : 'Confirm in your wallet…';
  return (
    <div className="dlg-wait" role="status" aria-live="polite">
      <img className="spin" src={sprite('Loading_Rotate')} alt="" />
      <p>{settling ? (social ? 'Done. Updating the market…' : 'Done in your wallet. Updating the market…') : text}</p>
    </div>
  );
}

/** Success screen with the game's stamp. */
export function Done({ title, children }: { title?: string; children?: ReactNode }) {
  return (
    <div className="dlg-done" role="status" aria-live="polite">
      <img className="stamp" src={sprite('Icon_Star')} alt="" />
      {title && <h3 className="fv-title">{title}</h3>}
      {children}
    </div>
  );
}

/** Runs an async action: idle → busy → done, or back to idle with an error message. */
export function useRun() {
  const [status, setStatus] = useState<'idle' | 'busy' | 'done'>('idle');
  const [error, setError] = useState<string | null>(null);
  const run = async (fn: () => Promise<void>) => {
    setError(null);
    setStatus('busy');
    try {
      await fn();
      setStatus('done');
    } catch (e) {
      setError((e as Error).message);
      setStatus('idle');
    }
  };
  return { status, error, run };
}

/* ---------- toasts (sonner, drawn with the game kit) ---------- */

export function toast(text: string, icon = 'Icon_Star') {
  sonner.custom(() => (
    <div className="toast fv-panel">
      <img src={sprite(icon)} alt="" />
      <span>{text}</span>
    </div>
  ));
}

export function ToastHost() {
  return <Toaster position="bottom-center" gap={8} visibleToasts={3} duration={3200} toastOptions={{ unstyled: true }} />;
}
