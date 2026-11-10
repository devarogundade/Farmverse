/**
 * The game server's own API (not the market read API): wallet sign-in and crediting a redeemed
 * item to the holder's island.
 *
 * Redeeming burns the NFT on chain; the game adds its contents only when the holder calls
 * `chain-redeem` with that transaction's hash (the server checks it on RPC, once per token). If
 * that call fails, the hash is kept in this browser and retried on the next visit, so a burned item
 * can't be lost.
 */
import { config } from '../config';
import { getConnector } from '../chain/near';

/** Must match backend/server/src/auth/sessions.ts SIGN_IN_MESSAGE. */
const SIGN_IN_MESSAGE = 'Sign in to Farmverse';
const CLIENT_VERSION = '1.0.0';

interface Session {
  accountId: string;
  accessToken: string;
  /** Epoch ms. */
  accessExpiresAt: number;
}

const SESSION_KEY = 'fv_game_session';
const PENDING_KEY = 'fv_pending_redeems';

export class GameError extends Error {
  constructor(
    message: string,
    readonly code: string,
    /** The refusal's details (e.g. FAUCET_WAIT's `nextAt`). */
    readonly params: Record<string, unknown> = {},
  ) {
    super(message);
  }
}

async function post<T>(path: string, body: unknown, token?: string, timeoutMs = 20_000): Promise<T> {
  if (!config.gameApiUrl) throw new GameError('The game server is offline.', 'no_api');
  let res: Response;
  try {
    res = await fetch(config.gameApiUrl + path, {
      method: 'POST',
      headers: { 'content-type': 'application/json', ...(token ? { authorization: `Bearer ${token}` } : {}) },
      body: JSON.stringify(body ?? {}),
      signal: AbortSignal.timeout(timeoutMs),
    });
  } catch {
    throw new GameError('Can’t reach the game server. Check your connection and try again.', 'network');
  }
  const json = (await res.json().catch(() => null)) as { ok?: boolean; error?: { code?: string; params?: Record<string, unknown> } } | null;
  if (!res.ok || json?.ok === false) throw new GameError('The game server refused this.', json?.error?.code ?? `http_${res.status}`, json?.error?.params ?? {});
  return json as T;
}

function savedSession(accountId: string): Session | null {
  try {
    const s = JSON.parse(sessionStorage.getItem(SESSION_KEY) ?? 'null') as Session | null;
    return s && s.accountId === accountId && s.accessExpiresAt > Date.now() + 30_000 ? s : null;
  } catch {
    return null;
  }
}

/**
 * A game session for the connected wallet: reused while valid, otherwise the wallet signs the
 * sign-in message (NEP-413) for the game's recipient, once, without a transaction.
 */
export async function gameSession(accountId: string): Promise<Session> {
  const saved = savedSession(accountId);
  if (saved) return saved;
  const { nonce, recipient } = await post<{ nonce: string; recipient: string }>('/v1/auth/nonce', {});
  const wallet = await (await getConnector()).wallet();
  const signed = await wallet.signMessage({
    message: SIGN_IN_MESSAGE,
    recipient,
    nonce: Uint8Array.from(atob(nonce), (c) => c.charCodeAt(0)),
    network: config.network,
    signerId: accountId,
  });
  const tokens = await post<Session>('/v1/auth/wallet', { accountId: signed.accountId, publicKey: signed.publicKey, signature: signed.signature, nonce });
  const session: Session = { accountId: tokens.accountId, accessToken: tokens.accessToken, accessExpiresAt: tokens.accessExpiresAt };
  sessionStorage.setItem(SESSION_KEY, JSON.stringify(session));
  return session;
}

/* ---------- redeem credits ---------- */

type Pending = { accountId: string; txHash: string };

const PERMANENT = new Set(['INVALID_ARGUMENT', 'PERMISSION_DENIED', 'NOT_FOUND']);

const readPending = (): Pending[] => {
  try {
    const v = JSON.parse(localStorage.getItem(PENDING_KEY) ?? '[]');
    return Array.isArray(v) ? v.filter((p) => p && typeof p.txHash === 'string' && typeof p.accountId === 'string') : [];
  } catch {
    return [];
  }
};
const writePending = (list: Pending[]) => {
  try {
    localStorage.setItem(PENDING_KEY, JSON.stringify(list));
  } catch {
    // storage blocked: the retry only lasts this visit
  }
};

export interface RedeemCredit {
  tokenId: string;
  item: string;
  quantity: number;
  /** False when this token was credited before. */
  fresh: boolean;
}

/** Tells the game about a redeem transaction so it credits the island. Safe to repeat. */
export async function creditRedeem(accountId: string, txHash: string): Promise<RedeemCredit> {
  const forget = () => writePending(readPending().filter((p) => p.txHash !== txHash));
  writePending([...readPending().filter((p) => p.txHash !== txHash), { accountId, txHash }]);
  try {
    const session = await gameSession(accountId);
    const res = await post<{ data: RedeemCredit }>('/v1/call/chain-redeem', { meta: { clientVersion: CLIENT_VERSION, platform: 'webgl' }, txHash }, session.accessToken);
    forget();
    return res.data;
  } catch (e) {
    // A refusal that can never succeed (not a redeem, not this wallet's) isn't worth retrying.
    if (e instanceof GameError && PERMANENT.has(e.code)) forget();
    throw e;
  }
}

/** Retries credits that failed earlier for this account (called when a wallet connects). */
export async function retryPendingRedeems(accountId: string): Promise<number> {
  const mine = readPending().filter((p) => p.accountId === accountId);
  let done = 0;
  for (const p of mine) {
    try {
      await creditRedeem(accountId, p.txHash);
      done++;
    } catch {
      // still pending; next visit
    }
  }
  return done;
}

/* ---------- sponsored calls (plan 15 §14) ---------- */

export interface Relayed {
  txHash: string;
  /** The call's event logs. */
  logs: string[];
}

/**
 * Has the game server send a call the player's key signed (a delegate action), paying its gas. Waits for the call
 * to finish on chain (a minute at most). Throws GameError: PERMISSION_DENIED / RATE_LIMITED / INVALID_ARGUMENT
 * when it was refused (nothing was sent), CONFLICT with the contract's words in `params.reason` when it ran and
 * failed, anything else when it can't tell.
 */
export async function relayDelegate(accountId: string, delegate: object): Promise<Relayed> {
  const session = await gameSession(accountId);
  const res = await post<{ data: { txHash: string; logs?: string[] } }>(
    '/v1/call/chain-relay',
    { meta: { clientVersion: CLIENT_VERSION, platform: 'webgl' }, delegate },
    session.accessToken,
    60_000,
  );
  return { txHash: res.data.txHash, logs: res.data.logs ?? [] };
}

/* ---------- testnet faucet (plan 15 §6.6) ---------- */

export interface FaucetClaim {
  /** Whole FARM sent. */
  farm: string;
  /** Whole NEAR sent ("0" after the first claim). */
  near: string;
  /** When the next claim opens (epoch ms). */
  nextAt: number;
}

/** Today's test FARM (and 1 test NEAR the first time). Throws GameError FAUCET_WAIT / FAUCET_CLOSED. */
export async function claimFaucet(accountId: string): Promise<FaucetClaim> {
  const session = await gameSession(accountId);
  const res = await post<{ data: FaucetClaim }>('/v1/call/chain-faucet', { meta: { clientVersion: CLIENT_VERSION, platform: 'webgl' } }, session.accessToken);
  return res.data;
}
