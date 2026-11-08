import { sprite } from '@farmverse/shared/gameTheme';
import tetherMark from '@farmverse/shared/stageshow/brands/tether.svg';
import { useState, type FormEvent } from 'react';
import { API_URL, PLAY_URL } from './links';

/**
 * farmverse.world/redeem: a coupon code from a task in the game, and the wallet it pays to.
 * B3TR pays out through VeBetterDAO (backend/server/src/coupons); USDT is still to come.
 */

type Paid = { amount: number; unit: string; tx: string; txUrl?: string; confirmed: boolean };
type State =
  | { kind: 'idle' }
  | { kind: 'busy' }
  | { kind: 'paid'; paid: Paid; wallet: string }
  | { kind: 'refused'; code: string; txUrl?: string };

const CODE = /^[A-Z0-9]{2,8}-[0-9A-Z]{4}-[0-9A-Z]{4}$/;
const WALLET = /^0x[0-9a-fA-F]{40}$/;

const MESSAGES: Record<string, string> = {
  bad_code: 'That doesn’t look like a code. Codes look like B3TR-7KQ2-M9XD.',
  bad_wallet: 'That isn’t a VeChain address. It starts with 0x and has 40 more letters and numbers.',
  unknown: 'We can’t find that code. Check it against the one in the game.',
  used: 'This code has already been redeemed.',
  expired: 'This code has expired.',
  busy: 'This code is being paid out right now.',
  coming_soon: 'USDT codes can’t be redeemed yet. We’ll open them soon.',
  closed: 'B3TR payouts aren’t open yet. Please try again later.',
  paused: 'Payouts are paused for a moment. Please try again later.',
  no_funds: 'Payouts are paused for a moment. Please try again later.',
  failed: 'The payout didn’t go through. Please try again in a minute.',
  rate_limited: 'Too many tries. Wait a minute, then try again.',
  network: 'Can’t reach Farmverse. Check your connection and try again.',
};

const normalise = (code: string) => code.replace(/\s+/g, '').toUpperCase();
const short = (wallet: string) => `${wallet.slice(0, 6)}…${wallet.slice(-4)}`;
const fmt = (n: number) => n.toLocaleString('en-US', { maximumFractionDigits: 6 });

export function Redeem() {
  const [code, setCode] = useState(() => normalise(new URLSearchParams(location.search).get('code') ?? ''));
  const [wallet, setWallet] = useState('');
  const [state, setState] = useState<State>({ kind: 'idle' });

  const usdt = normalise(code).startsWith('USDT');
  const ready = CODE.test(normalise(code)) && WALLET.test(wallet.trim()) && !usdt;

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!ready || state.kind === 'busy') return;
    setState({ kind: 'busy' });
    try {
      const res = await fetch(`${API_URL}/v1/coupons/redeem`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ code: normalise(code), wallet: wallet.trim() }),
      });
      const body = (await res.json().catch(() => ({}))) as { ok?: boolean; code?: string; txUrl?: string } & Partial<Paid>;
      if (res.ok && body.ok) setState({ kind: 'paid', paid: body as Paid, wallet: wallet.trim() });
      else setState({ kind: 'refused', code: body.code ?? 'failed', ...(body.txUrl ? { txUrl: body.txUrl } : {}) });
    } catch {
      setState({ kind: 'refused', code: 'network' });
    }
  }

  const again = () => {
    setCode('');
    setState({ kind: 'idle' });
  };

  return (
    <section className="redeem">
      <div className="redeem-head">
        <img className="redeem-art" src={sprite('Icon_Gift')} alt="" />
        <div>
          <h1 className="fv-title">Redeem a code</h1>
          <p>Finish tasks in the game to earn coupon codes. Paste yours here and the reward goes straight to your wallet.</p>
        </div>
      </div>

      <ul className="redeem-tokens" aria-label="What you can redeem">
        <li className="fv-panel">
          <img className="token-mark" src={sprite('B3TR_Mark')} alt="" />
          <span className="token-text">
            <b>B3TR</b>
            <small>Paid to your VeChain wallet</small>
          </span>
          <span className="rd-pill live">Open</span>
        </li>
        <li className="fv-panel">
          <img className="token-mark" src={tetherMark} alt="" />
          <span className="token-text">
            <b>USDT</b>
            <small>Paid to your wallet</small>
          </span>
          <span className="rd-pill">Coming soon</span>
        </li>
      </ul>

      <div className="redeem-card fv-panel" aria-live="polite">
        {state.kind === 'paid' ? (
          <div className="redeem-done">
            <img className="redeem-done-art" src={sprite('Icon_ImageIcon_ClearStamp_l')} alt="" />
            <p className="fv-title redeem-amount">
              {fmt(state.paid.amount)} {state.paid.unit}
            </p>
            <p>
              {state.paid.confirmed ? 'Sent' : 'On its way'} to {short(state.wallet)}.
              {!state.paid.confirmed && ' It can take a minute to show in your wallet.'}
            </p>
            {state.paid.txUrl && (
              <a className="more-link" href={state.paid.txUrl} target="_blank" rel="noreferrer">
                See it on the VeChain explorer →
              </a>
            )}
            <div className="redeem-actions">
              <button className="fv-btn blue" onClick={again}>
                Redeem another code
              </button>
              <a className="fv-btn green" href={PLAY_URL}>
                Back to the game
              </a>
            </div>
          </div>
        ) : (
          <form onSubmit={submit} noValidate>
            <label className="rd-field">
              <span className="rd-label fv-title">Coupon code</span>
              <input
                className="rd-input code"
                value={code}
                onChange={(e) => {
                  setCode(e.target.value.toUpperCase());
                  if (state.kind === 'refused') setState({ kind: 'idle' });
                }}
                placeholder="B3TR-XXXX-XXXX"
                autoComplete="off"
                autoCapitalize="characters"
                spellCheck={false}
                maxLength={40}
                aria-describedby={usdt ? 'code-note' : undefined}
              />
              {usdt && (
                <small id="code-note" className="rd-note">
                  {MESSAGES['coming_soon']}
                </small>
              )}
            </label>
            <label className="rd-field">
              <span className="rd-label fv-title">VeChain wallet address</span>
              <input
                className="rd-input"
                value={wallet}
                onChange={(e) => {
                  setWallet(e.target.value);
                  if (state.kind === 'refused') setState({ kind: 'idle' });
                }}
                placeholder="0x…"
                autoComplete="off"
                spellCheck={false}
                maxLength={64}
                aria-invalid={wallet.trim() !== '' && !WALLET.test(wallet.trim())}
              />
              <small className="rd-hint">The address in your VeWorld or Sync2 wallet. B3TR is sent here.</small>
            </label>
            {state.kind === 'refused' && (
              <p className="rd-error" role="alert">
                {MESSAGES[state.code] ?? MESSAGES['failed']}
                {state.txUrl && (
                  <>
                    {' '}
                    <a href={state.txUrl} target="_blank" rel="noreferrer">
                      See the payment
                    </a>
                  </>
                )}
              </p>
            )}
            <button type="submit" className="fv-btn green lg redeem-go" disabled={!ready || state.kind === 'busy'}>
              {state.kind === 'busy' ? 'Redeeming…' : 'Redeem'}
            </button>
          </form>
        )}
      </div>

      <p className="redeem-foot">
        Each code works once, before it expires. Codes are free: anyone who asks you to pay for one isn’t from
        Farmverse.
      </p>
    </section>
  );
}
