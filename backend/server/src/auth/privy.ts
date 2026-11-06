/**
 * Social login (plan 15 §13.2): players sign in with email/Google through a Privy wallet in near-connect. Privy's
 * browser SDK can't create NEAR wallets, so the sign page asks this server: we check the player's Privy access token
 * (ES256, signed with the app's verification key) and create their NEAR embedded wallet through Privy's REST API if
 * they have none. The wallet is the player's (Privy's TEE holds the key); our app secret can only create it.
 */
import { createPublicKey, verify } from 'node:crypto';
import { AppError } from '../../../game/src/core/errors';

export interface PrivyConfig {
  readonly appId: string;
  readonly appSecret: string;
  /** The app's verification key (SPKI PEM) from the Privy dashboard. */
  readonly verificationKey: string;
  readonly fetchImpl?: typeof fetch;
  readonly nowMs?: () => number;
}

const b64url = (s: string) => Buffer.from(s.replace(/-/g, '+').replace(/_/g, '/'), 'base64');

/** The Privy user id (`did:privy:…`) of a valid access token for this app, or throws UNAUTHENTICATED. */
export function privyUserOf(cfg: PrivyConfig, token: string): string {
  const parts = token.split('.');
  if (parts.length !== 3) throw new AppError('UNAUTHENTICATED');
  const [h, p, s] = parts as [string, string, string];
  let header: { alg?: string };
  let payload: { sub?: string; aud?: string | string[]; iss?: string; exp?: number };
  try {
    header = JSON.parse(b64url(h).toString('utf8'));
    payload = JSON.parse(b64url(p).toString('utf8'));
  } catch {
    throw new AppError('UNAUTHENTICATED');
  }
  if (header.alg !== 'ES256') throw new AppError('UNAUTHENTICATED');
  const key = createPublicKey(cfg.verificationKey.replace(/\\n/g, '\n'));
  // JWS ES256 signatures are r‖s (64 bytes): node's verify wants that as "ieee-p1363"
  const ok = verify('sha256', Buffer.from(`${h}.${p}`), { key, dsaEncoding: 'ieee-p1363' }, b64url(s));
  const aud = Array.isArray(payload.aud) ? payload.aud : [payload.aud];
  const now = (cfg.nowMs ?? Date.now)() / 1000;
  if (!ok || payload.iss !== 'privy.io' || !aud.includes(cfg.appId) || !payload.sub || !payload.exp || payload.exp < now) {
    throw new AppError('UNAUTHENTICATED');
  }
  return payload.sub;
}

/** The player's NEAR wallet address (an implicit account id), created through Privy's API if they have none. */
export async function ensureNearWallet(cfg: PrivyConfig, privyToken: string): Promise<{ address: string; created: boolean }> {
  const userId = privyUserOf(cfg, privyToken);
  const f = cfg.fetchImpl ?? fetch;
  const headers = {
    authorization: 'Basic ' + Buffer.from(`${cfg.appId}:${cfg.appSecret}`).toString('base64'),
    'privy-app-id': cfg.appId,
    'content-type': 'application/json',
  };
  const user = await f(`https://api.privy.io/v1/users/${encodeURIComponent(userId)}`, { headers, signal: AbortSignal.timeout(10_000) });
  if (!user.ok) throw new Error(`Privy users answered ${user.status}`);
  const u = (await user.json()) as { linked_accounts?: { type?: string; chain_type?: string; address?: string }[] };
  const near = (u.linked_accounts ?? []).find((a) => a.type === 'wallet' && a.chain_type === 'near' && a.address);
  if (near?.address) return { address: near.address, created: false };
  const made = await f('https://api.privy.io/v1/wallets', {
    method: 'POST',
    headers,
    body: JSON.stringify({ chain_type: 'near', owner: { user_id: userId } }),
    signal: AbortSignal.timeout(10_000),
  });
  if (!made.ok) throw new Error(`Privy wallets answered ${made.status}`);
  const w = (await made.json()) as { address?: string };
  if (!w.address) throw new Error('Privy returned no wallet address');
  return { address: w.address, created: true };
}
