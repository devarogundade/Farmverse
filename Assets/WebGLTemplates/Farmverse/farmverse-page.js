// The page side of the game (docs 13 §5.2): NEAR wallet sign-in (NEP-413 → game server session)
// and contract calls, exposed as window.farmversePage for Plugins/WebGL/FarmversePage.jslib.
// The page owns the refresh token; the game only ever asks for a valid access token.
import { NearConnector } from 'https://cdn.jsdelivr.net/npm/@hot-labs/near-connect@0.12.0/+esm';
import { Account, JsonRpcProvider, KeyPair, KeyPairSigner, actions, buildDelegateAction, encodeDelegateAction } from 'https://cdn.jsdelivr.net/npm/near-api-js@7.4.0/+esm';

const config = window.FARMVERSE_CONFIG;
const api = config.apiBaseUrl.replace(/\/+$/, '');
const REFRESH_KEY = 'fv_refresh_token';
const MARGIN_MS = 60_000;

const connector = new NearConnector({ network: config.network, footerBranding: null, autoConnect: true });

// social login (docs 15 §13.2): "Email or Google", a Privy wallet whose sign page lives on the market site
if (config.socialLogin && config.marketplaceUrl) {
  const site = config.marketplaceUrl.replace(/\/+$/, '');
  connector
    .registerWallet({
      id: 'farmverse-email', name: 'Email or Google', icon: `${site}/ui/Icon_Clover.webp`,
      description: 'Sign in with your email. No wallet app needed.', website: site, version: '1.0.0',
      executor: 'https://raw.githubusercontent.com/beneviolabs/privy-near-connect/3d56b782669d242a1ea432921e291abc26b4ebc7/executor.js',
      type: 'sandbox', platform: { web: site },
      features: { signMessage: true, signInWithoutAddKey: true, signInAndSignMessage: true, signInWithFunctionCallKey: true, signAndSendTransaction: true, signAndSendTransactions: true, mainnet: true, testnet: true },
      permissions: { isPrivyConnect: true, storage: true, allowsOpen: [site] },
      metadata: { signPageURL: `${site}/sign.html`, debug: false },
    })
    .catch(() => {});
}

let session = null; // { accessToken, accessExpiresAt, uid, accountId }

// ---- sponsored calls (docs 15 §14): a session key signs mints and redeems, our relayer pays the gas ----
// FastNEAR on testnet: rpc.testnet.near.org rate-limits (429) after a few calls
const RPC = config.rpcUrl || (config.network === 'mainnet' ? 'https://rpc.mainnet.near.org' : 'https://test.rpc.fastnear.com');
const SPONSORED = ['mint_with_voucher', 'redeem'];
const KEY_ALLOWANCE = '250000000000000000000000'; // 0.25 NEAR of gas the key could spend itself (the relayer pays, so ~none)
let sponsorOff = config.sponsored === false;
const lastNonce = new Map();

async function rpc(method, params) {
  const res = await fetch(RPC, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ jsonrpc: '2.0', id: 'fv', method, params }) });
  const json = await res.json();
  return json.result ?? null;
}

/** The key's access key, or null. A missing key still comes back as a `result`, with an `error` in it and no nonce. */
async function accessKeyOf(accountId, publicKey) {
  try {
    const ak = await rpc('query', { request_type: 'view_access_key', finality: 'final', account_id: accountId, public_key: publicKey });
    return ak && !ak.error && ak.nonce !== undefined ? ak : null;
  } catch {
    return null; // busy or offline RPC: treated as "not there yet" (the caller adds it or waits)
  }
}

function sessionKey(accountId) {
  const guest = guestKey(false);
  if (guest && guestAccountOf(guest) === accountId) return guest; // a guest's own key signs its mints and redeems
  const slot = `fv_session_key:${config.network}:${accountId}`;
  let secret = localStorage.getItem(slot);
  if (!secret) {
    secret = KeyPair.fromRandom('ed25519').toString();
    localStorage.setItem(slot, secret);
  }
  return KeyPair.fromString(secret);
}

/** The session key on chain: once, the wallet adds it (it can only mint and redeem on the items contract). */
async function ensureSessionKey(wallet, accountId, contractId) {
  const key = sessionKey(accountId);
  const publicKey = key.getPublicKey().toString();
  let ak = await accessKeyOf(accountId, publicKey);
  if (ak) return { key, ak };
  if (key === guestKey(false)) {
    // a guest's account appears with the welcome NEAR sent at sign-in: wait for it rather than add a key
    for (let i = 0; i < 15 && !(ak = await accessKeyOf(accountId, publicKey)); i++) await new Promise((r) => setTimeout(r, 1500));
    if (!ak) throw new Error('Your farm is still being set up. Try again in a minute.');
    return { key, ak };
  }
  try {
    await wallet.signAndSendTransaction({
      receiverId: accountId,
      actions: [{ type: 'AddKey', params: { publicKey, accessKey: { nonce: 0, permission: { receiverId: contractId, allowance: KEY_ALLOWANCE, methodNames: SPONSORED } } } }],
    });
    for (let i = 0; i < 20 && !(ak = await accessKeyOf(accountId, publicKey)); i++) await new Promise((r) => setTimeout(r, 1500));
    if (!ak) throw new Error('The session key is not on chain yet.');
  } catch (e) {
    // some wallets won't add keys (or the player said no): this call goes through the wallet instead
    console.warn('[farmverse] no session key, signing with the wallet:', e?.message ?? e);
    throw Object.assign(new Error('SESSION_KEY'), { fallback: true });
  }
  return { key, ak };
}

const b64 = (bytes) => btoa(String.fromCharCode(...bytes));

// ---- guest play: a NEAR key made in this browser. No wallet app, no sign-up; the welcome NEAR sent at
// sign-in creates its account (an implicit one: its id is the key). "Save your farm" in Settings shows the key.
const GUEST_KEY = `fv_guest_key:${config.network}`;
const WALLET_KIND = 'fv_wallet_kind';
const B58 = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';

function base58Decode(text) {
  let n = 0n;
  for (const ch of text) n = n * 58n + BigInt(B58.indexOf(ch));
  const bytes = [];
  while (n > 0n) { bytes.unshift(Number(n & 0xffn)); n >>= 8n; }
  for (const ch of text) { if (ch !== '1') break; bytes.unshift(0); }
  return Uint8Array.from(bytes);
}

/** This browser's guest key (made on first use when `create`), or null. */
function guestKey(create) {
  let secret = localStorage.getItem(GUEST_KEY);
  if (!secret && create) {
    secret = KeyPair.fromRandom('ed25519').toString();
    localStorage.setItem(GUEST_KEY, secret);
  }
  return secret ? (guestKey.cached?.secret === secret ? guestKey.cached.key : (guestKey.cached = { secret, key: KeyPair.fromString(secret) }).key) : null;
}
const guestAccountOf = (key) => Array.from(base58Decode(key.getPublicKey().toString().split(':')[1]), (b) => b.toString(16).padStart(2, '0')).join('');
const isGuest = () => localStorage.getItem(WALLET_KIND) === 'guest' && !!localStorage.getItem(GUEST_KEY);

/** NEP-413: sha256(u32le(2^31 + 413) ‖ borsh({ message, nonce[32], recipient, callbackUrl: none })). */
async function nep413Hash(message, nonce, recipient) {
  const enc = new TextEncoder();
  const str = (s) => {
    const b = enc.encode(s);
    const out = new Uint8Array(4 + b.length);
    new DataView(out.buffer).setUint32(0, b.length, true);
    out.set(b, 4);
    return out;
  };
  const tag = new Uint8Array(4);
  new DataView(tag.buffer).setUint32(0, 2 ** 31 + 413, true);
  const parts = [tag, str(message), nonce, str(recipient), new Uint8Array([0])];
  const bytes = new Uint8Array(parts.reduce((n, x) => n + x.length, 0));
  let at = 0;
  for (const x of parts) { bytes.set(x, at); at += x.length; }
  return new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));
}

/** A wallet for the guest key, shaped like near-connect's: signs messages and contract calls itself. */
function guestWallet(key) {
  const accountId = guestAccountOf(key);
  return {
    guest: true,
    accountId,
    async signMessage({ message, recipient, nonce }) {
      const signature = key.sign(await nep413Hash(message, nonce, recipient)).signature;
      return { accountId, publicKey: key.getPublicKey().toString(), signature: b64(signature) };
    },
    async signAndSendTransaction({ receiverId, actions: list }) {
      const calls = list.map((a) => {
        if (a.type !== 'FunctionCall') throw new Error('A guest farm can only call contracts.');
        const p = a.params;
        return actions.functionCall(p.methodName, p.args ?? {}, BigInt(p.gas ?? '30000000000000'), BigInt(p.deposit ?? '0'));
      });
      const account = new Account(accountId, new JsonRpcProvider({ url: RPC }), new KeyPairSigner(key));
      return account.signAndSendTransaction({ receiverId, actions: calls });
    },
  };
}

/** Shows the guest's key with a copy button, to keep the farm on another device or in a wallet app. */
function guestBackup() {
  const key = guestKey(false);
  if (!key) return;
  document.getElementById('fv-link')?.remove();
  const layer = document.createElement('div');
  layer.id = 'fv-link';
  const card = document.createElement('div');
  card.className = 'fv-card';
  const title = document.createElement('p');
  title.className = 'fv-card-title';
  title.textContent = 'Save your farm';
  const text = document.createElement('p');
  text.textContent = 'Your guest farm lives in this browser. Keep this key somewhere safe: import it into any NEAR wallet (Meteor, MyNearWallet, HOT: "import with private key") to play on another device or trade on the market. Anyone with it owns your farm.';
  const box = document.createElement('code');
  box.className = 'fv-keybox';
  box.textContent = key.toString();
  const account = document.createElement('p');
  account.className = 'fv-small';
  account.textContent = `Account: ${guestAccountOf(key)}`;
  const copy = document.createElement('button');
  copy.type = 'button';
  copy.textContent = 'Copy key';
  copy.onclick = () => navigator.clipboard.writeText(key.toString()).then(() => (copy.textContent = 'Copied'), () => (copy.textContent = 'Select the key and copy it'));
  const close = document.createElement('button');
  close.type = 'button';
  close.className = 'fv-plain';
  close.textContent = 'Close';
  close.onclick = () => layer.remove();
  card.append(title, text, box, account, copy, close);
  layer.append(card);
  document.getElementById('fv-root').append(layer);
}

/** Signs the call with the session key and has the game server relay it (no popup, no gas). */
async function sponsoredCall(wallet, accountId, contractId, method, args, gasTgas) {
  const { key, ak } = await ensureSessionKey(wallet, accountId, contractId);
  const publicKey = key.getPublicKey();
  const seen = BigInt(ak.nonce);
  const nonce = (seen > (lastNonce.get(accountId) ?? 0n) ? seen : lastNonce.get(accountId)) + 1n;
  lastNonce.set(accountId, nonce);
  const block = await rpc('block', { finality: 'final' });
  const maxBlockHeight = BigInt(block.header.height) + 300n;
  const argsBytes = new TextEncoder().encode(JSON.stringify(args ?? {}));
  const gas = BigInt(gasTgas ?? 100) * 10n ** 12n;
  const da = buildDelegateAction({ senderId: accountId, receiverId: contractId, actions: [actions.functionCall(method, argsBytes, gas, 0n)], nonce, maxBlockHeight, publicKey });
  const hash = new Uint8Array(await crypto.subtle.digest('SHA-256', encodeDelegateAction(da)));
  const signature = key.sign(hash).signature;
  const s = await window.farmversePage.requestSession(false);
  const res = await fetch(api + '/v1/call/chain-relay', {
    method: 'POST',
    headers: { 'content-type': 'application/json', authorization: `Bearer ${s.accessToken}` },
    body: JSON.stringify({
      meta: { clientVersion: config.clientVersion || '1.0.0', platform: 'webgl' },
      delegate: {
        senderId: accountId, receiverId: contractId, method, args: b64(argsBytes), gas: String(gas), deposit: '0',
        nonce: String(nonce), maxBlockHeight: String(maxBlockHeight), publicKey: publicKey.toString(), signature: b64(signature),
      },
    }),
  });
  const json = await res.json().catch(() => ({}));
  if (!res.ok) {
    const code = json?.error?.code ?? `HTTP ${res.status}`;
    if (code === 'UNSUPPORTED') sponsorOff = true; // this server doesn't sponsor: use the wallet from now on
    throw Object.assign(new Error(code), { fallback: true });
  }
  return { txHash: json.data?.txHash ?? json.txHash };
}
let pending = null; // one sign-in at a time

const $ = (id) => document.getElementById(id);

async function post(path, body) {
  const res = await fetch(api + path, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body ?? {}) });
  const json = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(json?.error?.code ?? `HTTP ${res.status}`);
  return json;
}

function keep(tokens) {
  if (tokens.refreshToken) localStorage.setItem(REFRESH_KEY, tokens.refreshToken);
  session = { accessToken: tokens.accessToken, accessExpiresAt: tokens.accessExpiresAt, uid: tokens.uid, accountId: tokens.accountId };
  return session;
}

async function refresh() {
  const token = localStorage.getItem(REFRESH_KEY);
  if (!token) return null;
  try {
    return keep(await post('/v1/auth/refresh', { refreshToken: token }));
  } catch {
    localStorage.removeItem(REFRESH_KEY); // revoked or expired: sign in again
    return null;
  }
}

function base64ToBytes(b64) {
  const bin = atob(b64);
  return Uint8Array.from(bin, (c) => c.charCodeAt(0));
}

/**
 * Shows the sign-in card and resolves once the player connected and signed. Connecting asks for the
 * signature straight away (an extension wallet shows it at once); only if that can't open without
 * another click (a web wallet's popup) does the card wait on its "Sign in" button. A wallet that is
 * already connected goes straight to signing.
 */
function signInWithWallet() {
  return new Promise((resolve) => {
    const card = $('fv-signin');
    const button = $('fv-signin-button');
    const text = $('fv-signin-text');
    const error = $('fv-signin-error');
    let wallet = null;
    let busy = false;
    card.hidden = false;
    error.hidden = true;
    button.textContent = 'Connect wallet';
    button.disabled = false;
    button.classList.add('fv-secondary');
    text.textContent = 'Jump straight in as a guest, or connect a NEAR wallet. Your farm is saved to your account.';
    // guest play: one tap, no wallet app (the same browser comes back to the same farm)
    let guest = $('fv-guest-button');
    if (!guest) {
      guest = document.createElement('button');
      guest.id = 'fv-guest-button';
      guest.type = 'button';
      button.before(guest);
    }
    guest.textContent = localStorage.getItem(GUEST_KEY) ? 'Continue as guest' : 'Play as guest';
    guest.disabled = false;

    const show = (message) => {
      error.textContent = message;
      error.hidden = !message;
    };

    const readyToSign = () => {
      text.textContent = 'Sign in to Farmverse with your wallet: a free signature, no transaction.';
      button.textContent = 'Sign in';
      button.disabled = false;
    };

    // signs the server's challenge and trades it for a session; the card goes when it worked
    const signIn = async () => {
      text.textContent = wallet.guest ? 'Setting up your farm…' : 'Approve the sign-in in your wallet…';
      button.disabled = true;
      guest.disabled = true;
      const challenge = await post('/v1/auth/nonce');
      const signed = await wallet.signMessage({ message: challenge.message, recipient: challenge.recipient, nonce: base64ToBytes(challenge.nonce) });
      if (!signed?.signature) throw new Error('The wallet did not sign.');
      const tokens = await post('/v1/auth/wallet', {
        accountId: signed.accountId,
        publicKey: signed.publicKey,
        signature: signed.signature,
        nonce: challenge.nonce,
      });
      localStorage.setItem(WALLET_KIND, wallet.guest ? 'guest' : 'wallet');
      card.hidden = true;
      resolve(keep(tokens));
    };

    const run = async (connect) => {
      if (busy) return;
      busy = true;
      show('');
      button.disabled = true;
      try {
        if (connect || !wallet) wallet = await connector.connect();
        await signIn();
      } catch (e) {
        // connected but the signature didn't happen (declined, or the popup needs this click): ask again
        if (wallet) readyToSign();
        else button.disabled = false;
        guest.disabled = false;
        show(String(e?.message ?? e));
      } finally {
        busy = false;
      }
    };

    button.onclick = () => run(false);
    guest.onclick = async () => {
      if (busy) return;
      busy = true;
      show('');
      try {
        wallet = guestWallet(guestKey(true));
        await signIn();
      } catch (e) {
        wallet = null;
        button.disabled = false;
        guest.disabled = false;
        button.textContent = 'Connect wallet';
        show(String(e?.message ?? e));
      } finally {
        busy = false;
      }
    };

    // a wallet remembered from last time (autoConnect): no need to pick it again
    connector.wallet().then((w) => {
      if (!w || wallet || busy) return;
      wallet = w;
      readyToSign();
    }, () => {});
  });
}

/**
 * Opens a link in a new tab. The game asks a frame after the click, which browsers can treat as a
 * popup and block: then a small card offers the link itself, which opens on the player's own click.
 */
function openTab(url, label) {
  // no 'noopener' feature: with it window.open always returns null, and the card would show though the tab opened
  const opened = window.open(url, '_blank');
  if (opened) {
    try { opened.opener = null; } catch { /* already cut off */ }
    return;
  }
  document.getElementById('fv-link')?.remove();
  const layer = document.createElement('div');
  layer.id = 'fv-link';
  const card = document.createElement('div');
  card.className = 'fv-card';
  const text = document.createElement('p');
  text.textContent = `${label || 'Open the link'} in a new tab.`; // the game's labels read 'Open the market'
  const link = document.createElement('a');
  link.className = 'fv-button';
  link.href = url;
  link.target = '_blank';
  link.rel = 'noopener';
  link.textContent = 'Open';
  const close = document.createElement('button');
  close.type = 'button';
  close.className = 'fv-plain';
  close.textContent = 'Close';
  const done = () => layer.remove();
  link.addEventListener('click', () => setTimeout(done, 0));
  close.addEventListener('click', done);
  layer.addEventListener('click', (e) => { if (e.target === layer) done(); });
  card.append(text, link, close);
  layer.append(card);
  document.getElementById('fv-root').append(layer);
}

window.farmversePage = {
  /** A page setting the game reads (siteUrl, marketplaceUrl, …); empty when unset. */
  config(key) {
    if (key === 'guest') return isGuest() ? '1' : ''; // the game shows "Save your farm" to guests
    const value = config[key];
    return typeof value === 'string' ? value : '';
  },

  openTab,

  guestBackup,

  /** A session with a valid access token: kept, refreshed, or after the player signs in. */
  async requestSession(forceRefresh) {
    if (session && !forceRefresh && session.accessExpiresAt - Date.now() > MARGIN_MS) return session;
    pending ??= (async () => {
      try {
        return (await refresh()) ?? (await signInWithWallet());
      } finally {
        pending = null;
      }
    })();
    return pending;
  },

  async signOut() {
    const token = localStorage.getItem(REFRESH_KEY);
    localStorage.removeItem(REFRESH_KEY);
    session = null;
    localStorage.removeItem(WALLET_KIND); // a guest key stays: "Continue as guest" brings the same farm back
    if (token) await post('/v1/auth/logout', { refreshToken: token }).catch(() => {});
    try { await connector.disconnect(); } catch { /* not connected */ }
  },

  /** Signs and sends one function call; resolves with its transaction hash once it succeeded. */
  async callContract({ contractId, method, args, deposit, gasTgas }) {
    const wallet = isGuest() ? guestWallet(guestKey(false)) : await connector.wallet();
    // mints and redeems are sponsored when the server relays them (no NEAR needed, no popup after the first)
    if (!sponsorOff && SPONSORED.includes(method) && session?.accountId) {
      try {
        return await sponsoredCall(wallet, session.accountId, contractId, method, args, gasTgas);
      } catch (e) {
        console.warn(`[farmverse] sponsored ${method} failed:`, e?.message ?? e);
        if (!e?.fallback) throw e; // the call itself failed
      }
    }
    const outcome = await wallet.signAndSendTransaction({
      receiverId: contractId,
      actions: [{ type: 'FunctionCall', params: { methodName: method, args: args ?? {}, gas: String(BigInt(gasTgas ?? 100) * 10n ** 12n), deposit: String(deposit ?? '0') } }],
    });
    const failed = [outcome?.status, ...(outcome?.receipts_outcome ?? []).map((r) => r?.outcome?.status)].find((s) => s && typeof s === 'object' && 'Failure' in s);
    if (failed) throw new Error(JSON.stringify(failed.Failure).match(/"ExecutionError":"([^"]+)"/)?.[1] ?? 'The transaction failed.');
    const txHash = outcome?.transaction?.hash ?? outcome?.transaction_outcome?.id;
    if (!txHash) throw new Error('The wallet did not return a transaction.');
    return { txHash };
  },
};
