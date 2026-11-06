// The page side of the game (docs 13 §5.2): NEAR wallet sign-in (NEP-413 → game server session)
// and contract calls, exposed as window.farmversePage for Plugins/WebGL/FarmversePage.jslib.
// The page owns the refresh token; the game only ever asks for a valid access token.
import { NearConnector } from 'https://cdn.jsdelivr.net/npm/@hot-labs/near-connect@0.12.0/+esm';
import { KeyPair, actions, buildDelegateAction, encodeDelegateAction } from 'https://cdn.jsdelivr.net/npm/near-api-js@7.4.0/+esm';

const config = window.FARMVERSE_CONFIG;
const api = config.apiBaseUrl.replace(/\/+$/, '');
const REFRESH_KEY = 'fv_refresh_token';
const MARGIN_MS = 60_000;

const connector = new NearConnector({ network: config.network, footerBranding: null, autoConnect: true });

let session = null; // { accessToken, accessExpiresAt, uid, accountId }

// ---- sponsored calls (docs 15 §14): a session key signs mints and redeems, our relayer pays the gas ----
const RPC = config.rpcUrl || (config.network === 'mainnet' ? 'https://rpc.mainnet.near.org' : 'https://rpc.testnet.near.org');
const SPONSORED = ['mint_with_voucher', 'redeem'];
const KEY_ALLOWANCE = '250000000000000000000000'; // 0.25 NEAR of gas the key could spend itself (the relayer pays, so ~none)
let sponsorOff = config.sponsored === false;
const lastNonce = new Map();

async function rpc(method, params) {
  const res = await fetch(RPC, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ jsonrpc: '2.0', id: 'fv', method, params }) });
  const json = await res.json();
  return json.result ?? null;
}

const accessKeyOf = (accountId, publicKey) =>
  rpc('query', { request_type: 'view_access_key', finality: 'final', account_id: accountId, public_key: publicKey });

function sessionKey(accountId) {
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
  await wallet.signAndSendTransaction({
    receiverId: accountId,
    actions: [{ type: 'AddKey', params: { publicKey, accessKey: { nonce: 0, permission: { receiverId: contractId, allowance: KEY_ALLOWANCE, methodNames: SPONSORED } } } }],
  });
  for (let i = 0; i < 20 && !(ak = await accessKeyOf(accountId, publicKey)); i++) await new Promise((r) => setTimeout(r, 1500));
  if (!ak) throw new Error('The session key is not on chain yet.');
  return { key, ak };
}

const b64 = (bytes) => btoa(String.fromCharCode(...bytes));

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
      text.textContent = 'Approve the sign-in in your wallet…';
      button.disabled = true;
      const challenge = await post('/v1/auth/nonce');
      const signed = await wallet.signMessage({ message: challenge.message, recipient: challenge.recipient, nonce: base64ToBytes(challenge.nonce) });
      if (!signed?.signature) throw new Error('The wallet did not sign.');
      const tokens = await post('/v1/auth/wallet', {
        accountId: signed.accountId,
        publicKey: signed.publicKey,
        signature: signed.signature,
        nonce: challenge.nonce,
      });
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
        show(String(e?.message ?? e));
      } finally {
        busy = false;
      }
    };

    button.onclick = () => run(false);

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
  const opened = window.open(url, '_blank', 'noopener');
  if (opened !== null) return;
  document.getElementById('fv-link')?.remove();
  const layer = document.createElement('div');
  layer.id = 'fv-link';
  const card = document.createElement('div');
  card.className = 'fv-card';
  const text = document.createElement('p');
  text.textContent = label ? `Open ${label} in a new tab.` : 'Open the link in a new tab.';
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
    const value = config[key];
    return typeof value === 'string' ? value : '';
  },

  openTab,

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
    if (token) await post('/v1/auth/logout', { refreshToken: token }).catch(() => {});
    try { await connector.disconnect(); } catch { /* not connected */ }
  },

  /** Signs and sends one function call; resolves with its transaction hash once it succeeded. */
  async callContract({ contractId, method, args, deposit, gasTgas }) {
    const wallet = await connector.wallet();
    // mints and redeems are sponsored when the server relays them (no NEAR needed, no popup after the first)
    if (!sponsorOff && SPONSORED.includes(method) && session?.accountId) {
      try {
        return await sponsoredCall(wallet, session.accountId, contractId, method, args, gasTgas);
      } catch (e) {
        if (!e?.fallback) throw e; // declined in the wallet, or the call itself failed
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
