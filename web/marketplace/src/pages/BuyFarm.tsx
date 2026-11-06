import { sprite } from '@farmverse/shared/gameTheme';
import { useQuery } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { useWallet } from '../chain/wallet';
import { Select } from '../components/Select';
import { fmt } from '../components/ui';
import { config } from '../config';

/** A token 1Click can swap from (plan 15 §13.3). */
interface IntentsToken {
  assetId: string;
  symbol: string;
  blockchain: string;
  decimals: number;
}
interface Quote {
  live: boolean;
  nearOut: string;
  farmOut: string;
  depositAddress?: string;
  deadline?: string;
}

const CHAIN: Record<string, string> = { near: 'NEAR', eth: 'Ethereum', arb: 'Arbitrum', base: 'Base', sol: 'Solana', btc: 'Bitcoin', pol: 'Polygon', bsc: 'BNB Chain', gnosis: 'Gnosis', op: 'Optimism', avax: 'Avalanche' };
const POPULAR = ['USDC', 'USDT', 'ETH', 'BTC', 'SOL', 'wNEAR'];
const whole = (units: string, decimals: number) => Number(BigInt(units) / 10n ** BigInt(Math.max(0, decimals - 6))) / 1e6;

/** Turns "12.5" into base units for a token with `decimals` (no floating point). */
function toUnits(amount: string, decimals: number): string | null {
  const m = /^(\d+)(?:\.(\d+))?$/.exec(amount.trim());
  if (!m) return null;
  const frac = (m[2] ?? '').slice(0, decimals).padEnd(decimals, '0');
  const units = BigInt(m[1]! + frac);
  return units > 0n ? units.toString() : null;
}

/** Buy FARM with any token: swapped to NEAR through NEAR Intents, FARM sent at a fixed rate. */
export function BuyFarm() {
  const { accountId } = useWallet();
  const tokens = useQuery({
    queryKey: ['intents-tokens'],
    queryFn: async ({ signal }) => {
      const res = await fetch(`${config.apiUrl}/intents/tokens`, { signal });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      return (await res.json()) as IntentsToken[];
    },
    staleTime: 600_000,
    enabled: Boolean(config.apiUrl),
  });
  const choices = useMemo(
    () =>
      (tokens.data ?? [])
        .filter((t) => POPULAR.includes(t.symbol))
        .sort((a, b) => POPULAR.indexOf(a.symbol) - POPULAR.indexOf(b.symbol) || a.blockchain.localeCompare(b.blockchain))
        .slice(0, 24),
    [tokens.data],
  );
  const [asset, setAsset] = useState('');
  const [amount, setAmount] = useState('10');
  const [refundTo, setRefundTo] = useState('');
  const token = choices.find((t) => t.assetId === asset) ?? choices[0];
  const units = token ? toUnits(amount, token.decimals) : null;

  const quote = useQuery({
    queryKey: ['farm-quote', token?.assetId, units, accountId, refundTo],
    queryFn: async ({ signal }) => {
      const res = await fetch(`${config.apiUrl}/intents/quote`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ asset: token!.assetId, amount: units, refundTo, account: accountId }),
        signal,
      });
      if (!res.ok) throw new Error('No quote');
      return (await res.json()) as Quote;
    },
    enabled: Boolean(token && units),
    staleTime: 20_000,
  });

  return (
    <>
      <div className="explore-head">
        <div>
          <h1 className="fv-title">Get FARM with any token</h1>
          <p className="muted">
            Pay with USDC, ETH, SOL, BTC or NEAR from any chain. NEAR Intents swaps it, and FARM arrives in your wallet.
          </p>
        </div>
      </div>
      <section className="card fv-panel buy-farm">
        {!tokens.data ? (
          <p className="muted">{tokens.isError ? "Can't reach the swap service right now." : 'Loading tokens…'}</p>
        ) : (
          <>
            <div className="field">
              <span className="field-label">Pay with</span>
              <Select
                label="Pay with"
                value={token?.assetId ?? ''}
                onChange={setAsset}
                options={choices.map((t) => ({ value: t.assetId, label: `${t.symbol} on ${CHAIN[t.blockchain] ?? t.blockchain}` }))}
              />
            </div>
            <label className="field">
              <span className="field-label">Amount</span>
              <input className="fv-input" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
            </label>
            {quote.data?.live === false || !accountId ? null : (
              <label className="field">
                <span className="field-label">Refund address on {CHAIN[token?.blockchain ?? ''] ?? 'that chain'} (if the swap can't finish)</span>
                <input className="fv-input" value={refundTo} onChange={(e) => setRefundTo(e.target.value.trim())} />
              </label>
            )}
            <div className="buy-farm-out">
              <img className="fv-icon" src={sprite('Icon_Clover')} alt="" />
              {quote.isLoading ? (
                <span className="muted">Getting a price…</span>
              ) : quote.data ? (
                <span className="fv-title">
                  ≈ {fmt(whole(quote.data.farmOut, 18))} FARM <small className="muted">({fmt(whole(quote.data.nearOut, 24))} NEAR)</small>
                </span>
              ) : (
                <span className="muted">{units ? 'No price for that right now.' : 'Enter an amount.'}</span>
              )}
            </div>
            {quote.data && !quote.data.live && (
              <p className="dlg-note">Buying opens when Farmverse launches on NEAR mainnet. Until then, try the game's free test FARM.</p>
            )}
            {quote.data?.live && quote.data.depositAddress && (
              <div className="dlg-note">
                <p>
                  Send exactly {amount} {token?.symbol} on {CHAIN[token?.blockchain ?? ''] ?? token?.blockchain} to:
                </p>
                <code className="deposit">{quote.data.depositAddress}</code>
                <p className="muted">Your FARM arrives a minute or two after the swap finishes.</p>
              </div>
            )}
          </>
        )}
      </section>
    </>
  );
}
