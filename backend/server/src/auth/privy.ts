/**
 * Social login (plan 15 §13.2): players sign in with email/Google through a Privy wallet in near-connect. Privy's
 * browser SDK can't create NEAR wallets, so the sign page asks this server: we check the player's Privy access token
 * (ES256, signed with one of the app's keys) and create their NEAR embedded wallet through Privy's REST API if they
 * have none. The wallet is the player's (Privy's TEE holds the key); our app secret can only create it.
 */
import { createPublicKey, verify, type JsonWebKey, type KeyObject } from 'node:crypto';
import { AppError } from '../../../game/src/core/errors';

export interface PrivyConfig {
  readonly appId: string;
  readonly appSecret: string;
  /** The app's verification key (SPKI PEM). Unset: Privy's published keys for the app (JWKS), cached an hour. */
  readonly verificationKey?: string;
  readonly fetchImpl?: typeof fetch;
  readonly nowMs?: () => number;
}

type Jwk = JsonWebKey & { kid?: string };

const b64url = (s: string) => Buffer.from(s.replace(/-/g, '+').replace(/_/g, '/'), 'base64');
const jwksCache = new Map<string, { at: number; keys: Jwk[] }>();

/** The key that signed a token: the configured PEM, else the app's JWKS entry with the token's `kid`. */
async function keyFor(cfg: PrivyConfig, kid: string | undefined): Promise<KeyObject> {
  if (cfg.verificationKey) return createPublicKey(cfg.verificationKey.split('\\n').join('\n'));
  let cached = jwksCache.get(cfg.appId);
  if (!cached || Date.now() - cached.at > 3_600_000 || (kid && !cached.keys.some((k) => k.kid === kid))) {
    const url = `https://auth.privy.io/api/v1/apps/${encodeURIComponent(cfg.appId)}/jwks.json`;
    const res = await (cfg.fetchImpl ?? fetch)(url, { signal: AbortSignal.timeout(8_000) });
    if (!res.ok) throw new Error(`Privy JWKS answered ${res.status}`);
    cached = { at: Date.now(), keys: ((await res.json()) as { keys: Jwk[] }).keys };
    jwksCache.set(cfg.appId, cached);
  }
  const jwk = kid ? cached.keys.find((k) => k.kid === kid) : cached.keys[0];
  if (!jwk) throw new AppError('UNAUTHENTICATED');
  return createPublicKey({ key: jwk, format: 'jwk' });
}

/** The Privy user id (`did:privy:…`) of a valid access token for this app, or throws UNAUTHENTICATED. */
export async function privyUserOf(cfg: PrivyConfig, token: string): Promise<string> {
  const parts = token.split('.');
  if (parts.length !== 3) throw new AppError('UNAUTHENTICATED');
  const [h, p, s] = parts as [string, string, string];
  let header: { alg?: string; kid?: string };
  let payload: { sub?: string; aud?: string | string[]; iss?: string; exp?: number };
  try {
    header = JSON.parse(b64url(h).toString('utf8'));
    payload = JSON.parse(b64url(p).toString('utf8'));
  } catch {
    throw new AppError('UNAUTHENTICATED');
  }
  if (header.alg !== 'ES256') throw new AppError('UNAUTHENTICATED');
  const key = await keyFor(cfg, header.kid);
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
  const userId = await privyUserOf(cfg, privyToken);
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
