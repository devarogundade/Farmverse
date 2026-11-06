// The page side of the game (docs 13 §5.2): NEAR wallet sign-in (NEP-413 → game server session)
// and contract calls, exposed as window.farmversePage for Plugins/WebGL/FarmversePage.jslib.
// The page owns the refresh token; the game only ever asks for a valid access token.
import { NearConnector } from 'https://cdn.jsdelivr.net/npm/@hot-labs/near-connect@0.12.0/+esm';

const config = window.FARMVERSE_CONFIG;
const api = config.apiBaseUrl.replace(/\/+$/, '');
const REFRESH_KEY = 'fv_refresh_token';
const MARGIN_MS = 60_000;

const connector = new NearConnector({ network: config.network, footerBranding: null, autoConnect: true });

let session = null; // { accessToken, accessExpiresAt, uid, accountId }
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

window.farmversePage = {
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
