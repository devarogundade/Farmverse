import { sprite } from '@farmverse/shared/gameTheme';
import { useState, type InputHTMLAttributes, type ReactNode } from 'react';
import { Dialog, toast } from './Dialog';

export const fmt = (n: number | null | undefined, digits = 0) =>
  n == null ? '—' : n.toLocaleString('en-US', { maximumFractionDigits: digits || (Math.abs(n) < 100 ? 2 : 0) });

export function timeAgo(at: number | null | undefined, now = Date.now()) {
  if (!at) return 'never';
  const s = Math.round((now - at) / 1000);
  if (s < 0) return `in ${timeLeft(-s * 1000)}`;
  if (s < 60) return 'just now';
  if (s < 3600) return `${Math.round(s / 60)}m ago`;
  if (s < 86400) return `${Math.round(s / 3600)}h ago`;
  return `${Math.round(s / 86400)}d ago`;
}

function timeLeft(ms: number) {
  const m = Math.round(ms / 60_000);
  if (m < 60) return `${m}m`;
  if (m < 1440) return `${Math.round(m / 60)}h`;
  return `${Math.round(m / 1440)}d`;
}

export const dateTime = (at: number | null | undefined) =>
  at ? new Date(at).toLocaleString('en-GB', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—';

const HUES = [268, 28, 190, 330, 140, 48, 210];
export function Avatar({ seed, label, size = 34 }: { seed: string; label?: string; size?: number }) {
  let h = 0;
  for (const ch of seed) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
  const hue = HUES[h % HUES.length];
  return (
    <span
      className="avatar"
      style={{ width: size, height: size, fontSize: size * 0.42, background: `linear-gradient(135deg, hsl(${hue} 80% 62%), hsl(${(hue + 40) % 360} 70% 42%))` }}
      aria-hidden="true"
    >
      {(label || seed)[0]?.toUpperCase()}
    </span>
  );
}

export function Loading({ text = 'Loading…' }: { text?: string }) {
  return (
    <div className="state" role="status" aria-live="polite">
      <img className="spin" src={sprite('Loading_Rotate')} alt="" />
      <p>{text}</p>
    </div>
  );
}

export function Failed({ error, retry }: { error: unknown; retry?: () => void }) {
  return (
    <div className="state" role="alert">
      <img className="state-art" src={sprite('Icon_ImageIcon_Info')} alt="" />
      <h2 className="fv-title">That didn’t load</h2>
      <p>{(error as Error)?.message ?? 'Something went wrong.'}</p>
      {retry && (
        <button className="fv-btn sm" onClick={retry}>
          Try again
        </button>
      )}
    </div>
  );
}

export function Empty({ title, children, art = 'Icon_Compass' }: { title: string; children?: ReactNode; art?: string }) {
  return (
    <div className="state">
      <img className="state-art" src={sprite(art)} alt="" />
      <h2 className="fv-title">{title}</h2>
      {children && <p>{children}</p>}
    </div>
  );
}

export function Stat({ icon, label, value, sub, tone }: { icon: string; label: string; value: ReactNode; sub?: ReactNode; tone?: 'gold' | 'green' | 'pink' }) {
  return (
    <div className="stat fv-panel">
      <img className="stat-icon" src={sprite(icon)} alt="" />
      <div className="stat-text">
        <span className="stat-label">{label}</span>
        <strong className={`fv-title stat-value ${tone ?? ''}`}>{value}</strong>
        {sub && <span className="stat-sub">{sub}</span>}
      </div>
    </div>
  );
}

export function PageHead({ icon, title, children, actions }: { icon: string; title: string; children?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="page-head">
      <img className="page-icon" src={sprite(icon)} alt="" />
      <div className="page-title">
        <h1 className="fv-title">{title}</h1>
        {children && <p className="muted">{children}</p>}
      </div>
      {actions && <div className="page-actions">{actions}</div>}
    </div>
  );
}

export function Pager({ page, more, go }: { page: number; more: boolean; go: (page: number) => void }) {
  if (page === 0 && !more) return null;
  return (
    <nav className="pager" aria-label="Pages">
      <button className="fv-btn gray sm" disabled={page === 0} onClick={() => go(page - 1)}>
        Previous
      </button>
      <span className="muted">Page {page + 1}</span>
      <button className="fv-btn gray sm" disabled={!more} onClick={() => go(page + 1)}>
        Next
      </button>
    </nav>
  );
}

/**
 * A labelled control. An error takes the hint's place under its own field, so nothing below jumps.
 * `group` for several controls (swatches): a <label> would send a click on its text to the first one.
 */
export function Field({ label, hint, error, group, children }: { label: string; hint?: ReactNode; error?: string; group?: boolean; children: ReactNode }) {
  const Box = group ? 'div' : 'label';
  return (
    <Box className="field" data-invalid={error ? true : undefined}>
      <span className="field-label">{label}</span>
      {children}
      {error ? (
        <small className="field-hint field-error" role="alert">
          {error}
        </small>
      ) : (
        hint && <small className="field-hint">{hint}</small>
      )}
    </Box>
  );
}

export function Input(props: InputHTMLAttributes<HTMLInputElement>) {
  return <input {...props} className={`input ${props.className ?? ''}`} />;
}

export function Toggle({ on, onChange, label }: { on: boolean; onChange: (on: boolean) => void; label: string }) {
  return (
    <button type="button" role="switch" aria-checked={on} aria-label={label} className={`toggle${on ? ' on' : ''}`} onClick={() => onChange(!on)}>
      <i />
    </button>
  );
}

export function Pill({ tone, children }: { tone: 'green' | 'gold' | 'pink' | 'sky' | 'muted'; children: ReactNode }) {
  return <span className={`pill pill-${tone}`}>{children}</span>;
}

/**
 * A change that needs a reason: the reason goes to the activity log with the change.
 * `onConfirm` throws to show its message and keep the dialog open.
 */
export function ReasonDialog({
  open,
  onClose,
  title,
  icon,
  confirm,
  tone = 'green',
  children,
  onConfirm,
  done,
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  icon?: string;
  confirm: string;
  tone?: 'green' | 'yellow' | 'purple' | 'gray';
  children?: ReactNode;
  onConfirm: (reason: string) => Promise<unknown>;
  /** Toast shown when it worked. */
  done: string;
}) {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const close = () => {
    if (busy) return;
    setReason('');
    setError(null);
    onClose();
  };
  return (
    <Dialog open={open} onClose={close} title={title} icon={icon}>
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          setBusy(true);
          setError(null);
          try {
            await onConfirm(reason.trim());
            toast(done);
            setBusy(false);
            setReason('');
            onClose();
          } catch (err) {
            setError((err as Error).message);
            setBusy(false);
          }
        }}
      >
        {children}
        <ReasonField value={reason} onChange={setReason} />
        {error && <p className="form-err">{error}</p>}
        <div className="dlg-actions">
          <button type="button" className="fv-btn gray" onClick={close}>
            Cancel
          </button>
          <button type="submit" className={`fv-btn ${tone}`} disabled={busy || reason.trim().length < 3}>
            {busy ? 'Working…' : confirm}
          </button>
        </div>
      </form>
    </Dialog>
  );
}

export function ReasonField({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  return (
    <Field label="Reason" hint="Kept in the activity log with your wallet name.">
      <textarea className="input" rows={2} maxLength={300} value={value} onChange={(e) => onChange(e.target.value)} placeholder="Why are you doing this? e.g. ticket 8821" required />
    </Field>
  );
}
