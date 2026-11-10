/**
 * "Your farm": the guest farm from the game, signed for by the game site's key bridge.
 *
 * "Play as guest" keeps a NEAR key in the game site's storage. This page can't read it, but a small page next to the
 * game can (key-bridge.html on the play site, Assets/WebGLTemplates/Farmverse/key-bridge.js): the market frames it,
 * hidden, and posts requests; the key stays there and only signatures and results come back. When the bridge
 * reports a guest farm, NEAR Connect lists it as a wallet like the Email or Google one, and a player who came from
 * the game as a guest is signed in with it on arrival.
 */
import { config } from '../config';
import { implicitPublicKey } from './delegate';
import { GUEST_WALLET_ID } from './privyWallet';

const SOURCE = 'farmverse-key-bridge';
/** How long the market waits for the bridge before showing the wallets without it. */
const PROBE_MS = 4_000;
/** The player signed out of "Your farm" here: don't sign them back in on arrival (they can still choose it). */
const SIGNED_OUT = 'fv_guest_signed_out';

type Network = 'mainnet' | 'testnet';
type Status = { network: Network; accountId: string | null; active: boolean };
type ConnectorAction = { type: string; params: Record<string, unknown> } | Record<string, unknown>;
type Tx = { receiverId: string; actions: ConnectorAction[] };

export type GuestBridge = NonNullable<Awaited<ReturnType<typeof openBridge>>>;

/** The guest farm's wallet, or null when the game site has none in this browser. Use chain/near.ts getGuestBridge. */
export async function openBridge() {
  const src = `${config.playUrl}/key-bridge.html`;
  const origin = new URL(src).origin;
  const frame = document.createElement('iframe');
  frame.src = src;
  frame.title = 'Farmverse guest farm';
  frame.setAttribute('aria-hidden', 'true');
  frame.tabIndex = -1;
  frame.style.cssText = 'position:absolute;width:0;height:0;border:0;visibility:hidden';
  const loaded = new Promise<void>((resolve, reject) => {
    frame.addEventListener('load', () => resolve(), { once: true });
    frame.addEventListener('error', () => reject(new Error('The game site did not answer.')), { once: true });
  });
  document.body.appendChild(frame);

  const waiting = new Map<number, { resolve: (v: unknown) => void; reject: (e: Error) => void }>();
  let nextId = 1;
  window.addEventListener('message', (e) => {
    if (e.origin !== origin || e.source !== frame.contentWindow || e.data?.source !== SOURCE) return;
    const w = waiting.get(e.data.id);
    if (!w) return;
    waiting.delete(e.data.id);
    if (e.data.ok) w.resolve(e.data.result);
    else w.reject(new Error(String(e.data.error ?? 'The guest farm could not sign this.')));
  });

  const ask = <T>(type: string, params: object = {}, timeoutMs = 120_000) =>
    new Promise<T>((resolve, reject) => {
      const id = nextId++;
      const timer = setTimeout(() => {
        waiting.delete(id);
        reject(new Error('The guest farm did not answer. Reload the page and try again.'));
      }, timeoutMs);
      waiting.set(id, {
        resolve: (v) => (clearTimeout(timer), resolve(v as T)),
        reject: (err) => (clearTimeout(timer), reject(err)),
      });
      frame.contentWindow?.postMessage({ source: SOURCE, id, type, network: config.network, params }, origin);
    });

  const timeout = new Promise<never>((_, reject) => setTimeout(() => reject(new Error('The game site took too long.')), PROBE_MS));
  await Promise.race([loaded, timeout]);
  const status = await ask<Status>('status', {}, PROBE_MS);
  if (!status.accountId || status.network !== config.network) {
    frame.remove();
    return null;
  }
  const accountId = status.accountId;
  const accounts = () => [{ accountId, publicKey: implicitPublicKey(accountId) }];

  /** What NEAR Connect calls (as the Email or Google wallet: an "injected" one). */
  const wallet = {
    manifest: {
      id: GUEST_WALLET_ID,
      name: 'Your farm',
      icon: `${location.origin}/ui/Icon_Clover.webp`,
      description: 'The guest farm you play in this browser. No wallet app needed.',
      website: config.playUrl,
      version: '1.0.0',
      type: 'injected',
      platform: {},
      features: {
        signMessage: true,
        signInWithoutAddKey: true,
        signAndSendTransaction: true,
        signAndSendTransactions: true,
        signDelegateActions: true,
        mainnet: config.network === 'mainnet',
        testnet: config.network === 'testnet',
      },
      permissions: {},
    },
    async signIn() {
      clearSignedOut();
      return accounts();
    },
    async signOut() {
      try {
        localStorage.setItem(SIGNED_OUT, accountId);
      } catch {
        // storage blocked: they may be signed back in on the next visit
      }
    },
    async getAccounts() {
      return accounts();
    },
    async signMessage(p: { message: string; recipient: string; nonce: Uint8Array }) {
      return ask<{ accountId: string; publicKey: string; signature: string }>('signMessage', { message: p.message, recipient: p.recipient, nonce: p.nonce });
    },
    async signAndSendTransaction(p: Tx) {
      const [outcome] = await ask<unknown[]>('signAndSendTransactions', { transactions: [plainTx(p)] });
      return outcome;
    },
    async signAndSendTransactions(p: { transactions: Tx[] }) {
      return ask<unknown[]>('signAndSendTransactions', { transactions: p.transactions.map(plainTx) });
    },
    async signDelegateActions(p: { delegateActions: Tx[] }) {
      return ask<{ signedDelegateActions: string[] }>('signDelegateActions', { delegateActions: p.delegateActions.map(plainTx) });
    },
  };

  return {
    accountId,
    wallet,
    /** The game is playing as this guest farm right now, and the player didn't sign out of it here. */
    signInOnArrival: status.active && signedOut() !== accountId,
    /** Hands the wallet to NEAR Connect. Call right after `new NearConnector(…)`, before anything reads its wallets. */
    inject() {
      window.dispatchEvent(new CustomEvent('near-wallet-injected', { detail: wallet }));
    },
  };
}

/** Calls as plain data for postMessage (no class instances: the bridge rebuilds them). */
function plainTx(t: Tx): Tx {
  return {
    receiverId: t.receiverId,
    actions: t.actions.map((a) => {
      if (!('type' in a) || a.type !== 'FunctionCall') throw new Error('A guest farm can only call contracts.');
      const p = (a as { params: Record<string, unknown> }).params;
      return { type: 'FunctionCall', params: { methodName: p.methodName, args: p.args, gas: String(p.gas), deposit: String(p.deposit) } };
    }),
  };
}

function signedOut(): string | null {
  try {
    return localStorage.getItem(SIGNED_OUT);
  } catch {
    return null;
  }
}

function clearSignedOut() {
  try {
    localStorage.removeItem(SIGNED_OUT);
  } catch {
    // storage blocked
  }
}
