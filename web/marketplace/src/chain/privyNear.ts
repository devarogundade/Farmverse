/**
 * The "Email or Google" wallet, signing in the page itself: no popup window and no approve screen.
 *
 * Privy's embedded wallet signs here, through its hidden iframe (the same `rawSign` the old popup sign page used).
 * NEAR Connect takes this wallet as an "injected" one, under the id players' saved sessions already point at, so the
 * market's and the game's wallet calls (`connector.wallet().signAndSendTransaction(…)`) work unchanged.
 *
 * Signing in is the caller's screen: an emailed code (`sendEmailCode` + `loginWithEmailCode`) or Google
 * (`startGoogle`, a full-page redirect that `finishGoogle` completes on the way back). Then
 * `connector.connect({ walletId: PRIVY_NEAR_ID })` makes sure the player has a NEAR wallet (our server creates it
 * through Privy) and signs in.
 *
 * Used by the market (chain/near.ts) and, bundled as privy-near.js, by the game page (farmverse-page.js).
 * Privy's dashboard must list both sites as allowed origins.
 */
import Privy, { LocalStorage, rawSign } from '@privy-io/js-sdk-core';
import { Account, JsonRpcProvider, PublicKey, Signer, actions } from 'near-api-js';
import { signMessage as signNep413 } from 'near-api-js/nep413';

/** The wallet's id in NEAR Connect (kept from the popup wallet: players' saved sessions point at it). */
export const PRIVY_NEAR_ID = 'farmverse-email';

export type PrivyNearOptions = {
  appId: string;
  clientId: string;
  /** The game server (it creates the player's NEAR wallet through Privy). */
  gameApiUrl: string;
  /** NEAR RPC per network; the network's public FastNEAR endpoint when unset. */
  rpcUrls?: Partial<Record<Network, string>>;
  /** The wallet's icon in NEAR Connect's list. */
  icon: string;
};

type Network = 'mainnet' | 'testnet';
type NearWallet = { id: string; address: string };
type ConnectorAction = { type: string; params: Record<string, any> } | Record<string, unknown>;
type Tx = { receiverId: string; actions: ConnectorAction[] };

const OAUTH_RETURN = 'fv_privy_return';
const PUBLIC_RPC: Record<Network, string> = { mainnet: 'https://rpc.mainnet.fastnear.com', testnet: 'https://rpc.testnet.fastnear.com' };

export type PrivyNear = ReturnType<typeof createPrivyNear>;

export function createPrivyNear(o: PrivyNearOptions) {
  const privy = new Privy({ appId: o.appId, clientId: o.clientId, storage: new LocalStorage() });
  const api = o.gameApiUrl.replace(/\/+$/, '');
  let ready: Promise<void> | null = null;
  let iframe: Promise<void> | null = null;
  let nearWallet: NearWallet | null = null;
  const accounts = new Map<Network, Account>();

  const init = () => (ready ??= privy.initialize());

  /** Privy's hidden wallet iframe: it holds the key share and signs. Mounted once, when signing first needs it. */
  const mountIframe = () =>
    (iframe ??= new Promise<void>((resolve, reject) => {
      const el = document.createElement('iframe');
      el.src = privy.embeddedWallet.getURL();
      el.style.display = 'none';
      el.setAttribute('aria-hidden', 'true');
      window.addEventListener('message', (e) => {
        if (e.source === el.contentWindow) privy.embeddedWallet.onMessage(e.data);
      });
      el.addEventListener(
        'load',
        () => {
          privy.setMessagePoster({
            postMessage: (msg: unknown, origin: string, transfer?: Transferable) => el.contentWindow!.postMessage(msg, origin, transfer ? [transfer] : undefined),
            reload: () => {
              el.src = privy.embeddedWallet.getURL();
            },
          } as never);
          resolve();
        },
        { once: true },
      );
      el.addEventListener('error', () => {
        iframe = null;
        reject(new Error('Sign-in is not available right now.'));
      });
      document.body.appendChild(el);
    }));

  async function authed(): Promise<boolean> {
    await init();
    return !!(await privy.getAccessToken().catch(() => null));
  }

  /** The player's Privy NEAR wallet; with `create`, our server makes one first if they have none. */
  async function findWallet(create: boolean): Promise<NearWallet | null> {
    if (nearWallet) return nearWallet;
    const read = async () => {
      const { user } = await privy.user.get();
      const w = user.linked_accounts.find((a: any) => a.type === 'wallet' && a.chain_type === 'near' && typeof a.id === 'string');
      return w ? { id: (w as any).id as string, address: (w as any).address as string } : null;
    };
    let w = await read();
    if (!w && create) {
      const token = await privy.getAccessToken();
      const res = await fetch(`${api}/v1/auth/privy/near-wallet`, { method: 'POST', headers: { authorization: `Bearer ${token}` } });
      if (!res.ok) throw new Error('Could not set up your account.');
      w = await read();
    }
    nearWallet = w;
    return w;
  }

  class PrivySigner extends Signer {
    constructor(private readonly w: NearWallet) {
      super();
    }
    async getPublicKey() {
      return implicitKey(this.w.address);
    }
    protected async signBytes(bytes: Uint8Array) {
      await mountIframe();
      const { data } = await rawSign(privy as never, (req: any) => privy.embeddedWallet.signWithUserSigner(req), {
        wallet_id: this.w.id,
        params: { hash: toHex(bytes) },
      } as never);
      return fromHex((data as { signature: string }).signature);
    }
  }

  async function account(network: Network = 'testnet'): Promise<Account> {
    if (!(await authed())) throw new Error('Choose Google or your email on the sign-in screen first.');
    const w = await findWallet(true);
    if (!w) throw new Error('Could not set up your account.');
    let a = accounts.get(network);
    if (!a || a.accountId !== w.address) {
      a = new Account(w.address, new JsonRpcProvider({ url: o.rpcUrls?.[network] ?? PUBLIC_RPC[network] }), new PrivySigner(w));
      accounts.set(network, a);
    }
    return a;
  }

  const nep413 = async (network: Network | undefined, p: { message: string; recipient: string; nonce: Uint8Array; callbackUrl?: string }) => {
    const a = await account(network);
    const s = await signNep413({ signerAccount: a, payload: p as never });
    return { accountId: s.accountId, publicKey: s.publicKey.toString(), signature: toBase64(s.signature) };
  };

  /** What NEAR Connect calls (it hands actions over in its own shape; toNearAction turns them back). */
  const wallet = {
    manifest: {
      id: PRIVY_NEAR_ID,
      name: 'Email or Google',
      icon: o.icon,
      description: 'Sign in with your email. No wallet app needed.',
      website: location.origin,
      version: '2.0.0',
      type: 'injected',
      platform: {},
      features: {
        signMessage: true,
        signInWithoutAddKey: true,
        signInAndSignMessage: true,
        signInWithFunctionCallKey: true,
        signAndSendTransaction: true,
        signAndSendTransactions: true,
        mainnet: true,
        testnet: true,
      },
      permissions: {},
    },
    async signIn(d: { network?: Network; addFunctionCallKey?: any } = {}) {
      const a = await account(d.network);
      const k = d.addFunctionCallKey;
      if (k) {
        await a.addFunctionCallAccessKey({
          contractId: k.contractId,
          publicKey: k.publicKey,
          methodNames: k.allowMethods?.anyMethod ? [] : (k.allowMethods?.methodNames ?? []),
          allowance: k.gasAllowance?.kind === 'limited' ? BigInt(k.gasAllowance.amount) : undefined,
        } as never);
        return [{ accountId: a.accountId, publicKey: k.publicKey }];
      }
      return [{ accountId: a.accountId, publicKey: (await implicitKey(a.accountId)).toString() }];
    },
    async signInAndSignMessage(d: { network?: Network; addFunctionCallKey?: any; messageParams: any }) {
      const accs = await wallet.signIn(d);
      const signedMessage = await nep413(d.network, d.messageParams);
      return accs.map((acc) => ({ ...acc, signedMessage }));
    },
    async signOut() {
      nearWallet = null;
      accounts.clear();
      await init();
      await (privy.auth as any).logout?.().catch(() => {});
    },
    async getAccounts() {
      if (!(await authed())) return [];
      const w = await findWallet(false).catch(() => null);
      return w ? [{ accountId: w.address, publicKey: (await implicitKey(w.address)).toString() }] : [];
    },
    async signMessage(p: { network?: Network; message: string; recipient: string; nonce: Uint8Array; callbackUrl?: string }) {
      return nep413(p.network, p);
    },
    async signAndSendTransaction(p: Tx & { network?: Network }) {
      const a = await account(p.network);
      return a.signAndSendTransaction({ receiverId: p.receiverId, actions: p.actions.map(toNearAction) as never });
    },
    async signAndSendTransactions(p: { network?: Network; transactions: Tx[] }) {
      const a = await account(p.network);
      return a.signAndSendTransactions({
        transactions: p.transactions.map((t) => ({ receiverId: t.receiverId, actions: t.actions.map(toNearAction) as never })),
      });
    },
    async signDelegateActions(): Promise<never> {
      throw new Error('Not supported by this wallet.');
    },
  };

  return {
    wallet,

    /** Hands the wallet to NEAR Connect. Call right after `new NearConnector(…)`, before anything reads its wallets. */
    inject() {
      window.dispatchEvent(new CustomEvent('near-wallet-injected', { detail: wallet }));
    },

    /** Signed in to Privy on this site (the NEAR wallet may still be created on connect). */
    isSignedIn: authed,

    async sendEmailCode(email: string) {
      await init();
      await privy.auth.email.sendCode(email);
    },

    async loginWithEmailCode(email: string, code: string) {
      await init();
      nearWallet = null;
      await privy.auth.email.loginWithCode(email, code.trim());
    },

    /** Leaves for Google; back on `returnTo` (this page by default), `finishGoogle` signs the player in. */
    async startGoogle(returnTo = location.href): Promise<void> {
      await init();
      const here = new URL(returnTo);
      // Privy returns to the address without its #part: keep that for the way back
      sessionStorage.setItem(OAUTH_RETURN, here.hash);
      const { url } = await privy.auth.oauth.generateURL('google', `${here.origin}${here.pathname}${here.search}`);
      location.href = url;
    },

    /** True when this page load is the way back from Google and the player is now signed in to Privy. */
    async finishGoogle(): Promise<boolean> {
      const q = new URLSearchParams(location.search);
      const code = q.get('privy_oauth_code');
      const state = q.get('privy_oauth_state');
      if (!code || !state) return false;
      q.delete('privy_oauth_code');
      q.delete('privy_oauth_state');
      q.delete('privy_oauth_provider');
      const hash = sessionStorage.getItem(OAUTH_RETURN) ?? location.hash;
      sessionStorage.removeItem(OAUTH_RETURN);
      history.replaceState(null, '', `${location.pathname}${q.size ? `?${q}` : ''}${hash}`);
      await init();
      nearWallet = null;
      await privy.auth.oauth.loginWithCode(code, state, 'google');
      return true;
    },
  };
}

// ---- NEAR helpers ----

const B58 = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';

function base58(bytes: Uint8Array): string {
  let n = 0n;
  for (const b of bytes) n = (n << 8n) | BigInt(b);
  let out = '';
  while (n > 0n) {
    out = B58[Number(n % 58n)] + out;
    n /= 58n;
  }
  for (const b of bytes) {
    if (b !== 0) break;
    out = '1' + out;
  }
  return out;
}

const toHex = (bytes: Uint8Array) => `0x${Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')}`;

function fromHex(hex: string): Uint8Array {
  const h = hex.startsWith('0x') ? hex.slice(2) : hex;
  const out = new Uint8Array(h.length / 2);
  for (let i = 0; i < out.length; i++) out[i] = parseInt(h.slice(i * 2, i * 2 + 2), 16);
  return out;
}

const toBase64 = (bytes: Uint8Array) => btoa(String.fromCharCode(...bytes));

/** An implicit account's key: its id is the key in hex. */
async function implicitKey(accountId: string): Promise<PublicKey> {
  return PublicKey.fromString(`ed25519:${base58(fromHex(accountId))}`);
}

/** NEAR Connect's action shape → near-api-js actions (as privy-near-connect does it). */
function toNearAction(action: ConnectorAction): unknown {
  if (!('type' in action) || typeof action.type !== 'string') return action;
  const p = (action as { params: Record<string, any> }).params;
  switch (action.type) {
    case 'CreateAccount':
      return actions.createAccount();
    case 'FunctionCall':
      return actions.functionCall(p.methodName, p.args, BigInt(p.gas), BigInt(p.deposit));
    case 'Transfer':
      return actions.transfer(BigInt(p.deposit));
    case 'AddKey': {
      const pk = PublicKey.fromString(p.publicKey);
      const perm = p.accessKey.permission;
      if (perm === 'FullAccess') return actions.addFullAccessKey(pk);
      return actions.addFunctionCallAccessKey(pk, perm.receiverId, perm.methodNames ?? [], perm.allowance !== undefined ? BigInt(perm.allowance) : undefined);
    }
    case 'DeleteKey':
      return actions.deleteKey(PublicKey.fromString(p.publicKey));
    default:
      throw new Error(`This wallet can't sign a ${action.type} action.`);
  }
}
