// The guest farm's signer for the marketplace (key-bridge.html, embedded hidden by the market).
//
// "Play as guest" keeps a NEAR key in the game site's storage (farmverse-page.js, fv_guest_key:<network>). The market
// is another site name on the same domain, so its page can't read that storage, but this page, served next to the
// game and framed by the market, can. The key never leaves: the market posts requests, this page checks who is
// asking (the market's own address, or localhost while developing), signs and answers with signatures and results.
//
// It signs only what the market needs: the game's sign-in message, and calls to our own contracts (items, market,
// FARM), sent itself or handed back as delegate actions for the game server to relay with its gas.
const config = window.FARMVERSE_BRIDGE;
const SOURCE = 'farmverse-key-bridge';
const SIGN_IN_MESSAGE = 'Sign in to Farmverse'; // backend/server/src/auth/sessions.ts SIGN_IN_MESSAGE
const GUEST_KEY = `fv_guest_key:${config.network}`;
const WALLET_KIND = 'fv_wallet_kind';
const TTL_BLOCKS = 300n; // a delegate action's life (the relay takes at most 600)
const api = config.apiBaseUrl.replace(/\/+$/, '');

// who may ask: the market, and any localhost page while this one is served from localhost too
const marketOrigin = new URL(config.marketplaceUrl).origin;
const dev = ['localhost', '127.0.0.1'].includes(location.hostname);
const allowed = (origin) => origin === marketOrigin || (dev && /^https?:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(origin));

// near-api-js only when signing: answering "is there a guest farm here?" needs nothing
let nearLib = null;
const near = () => (nearLib ??= import('https://cdn.jsdelivr.net/npm/near-api-js@7.4.0/+esm'));

/** The contracts and RPC from the game server's /v1/config (the same ones the game uses). */
let chainInfo = null;
const chain = () =>
  (chainInfo ??= fetch(`${api}/v1/config`, { cache: 'no-store' })
    .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`))))
    .then(({ values: v = {} }) => {
      const contracts = [v.items_contract, v.market_contract, v.farm_contract].filter((c) => typeof c === 'string' && c);
      if (contracts.length !== 3) throw new Error('The game server has no market set up.');
      const rpcUrl = v.rpc_url || (config.network === 'mainnet' ? 'https://rpc.mainnet.near.org' : 'https://test.rpc.fastnear.com');
      return { contracts: new Set(contracts), rpcUrl };
    })
    .catch((e) => {
      chainInfo = null; // try again next time
      throw e;
    }));

const B58 = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';
function base58Decode(text) {
  let n = 0n;
  for (const ch of text) n = n * 58n + BigInt(B58.indexOf(ch));
  const bytes = [];
  while (n > 0n) { bytes.unshift(Number(n & 0xffn)); n >>= 8n; }
  for (const ch of text) { if (ch !== '1') break; bytes.unshift(0); }
  return Uint8Array.from(bytes);
}
const b64 = (bytes) => { let s = ''; for (const b of bytes) s += String.fromCharCode(b); return btoa(s); };
/** A guest's account is implicit: its id is its public key in hex. */
const accountOf = (publicKey) => Array.from(base58Decode(publicKey.split(':')[1]), (b) => b.toString(16).padStart(2, '0')).join('');

function guestSecret() {
  try {
    return localStorage.getItem(GUEST_KEY);
  } catch {
    return null; // storage blocked
  }
}

async function guestKey() {
  const secret = guestSecret();
  if (!secret) throw new Error('There is no guest farm in this browser.');
  const { KeyPair } = await near();
  return KeyPair.fromString(secret);
}

/** NEP-413: sha256(u32le(2^31 + 413) ‖ borsh({ message, nonce[32], recipient, callbackUrl: none })), as the game page. */
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

/** Contract calls to our own contracts only, as near-api-js actions. */
async function callsOf(list) {
  const { contracts } = await chain();
  const { actions } = await near();
  if (!Array.isArray(list) || list.length === 0 || list.length > 8) throw new Error('Nothing to sign.');
  return list.map((t) => {
    if (!contracts.has(t?.receiverId)) throw new Error('A guest farm only signs Farmverse calls here.');
    if (!Array.isArray(t.actions) || t.actions.length === 0) throw new Error('Nothing to sign.');
    return {
      receiverId: t.receiverId,
      actions: t.actions.map((a) => {
        if (a?.type !== 'FunctionCall') throw new Error('A guest farm can only call contracts.');
        const p = a.params;
        const args = typeof p.args === 'string' ? new TextEncoder().encode(p.args) : p.args instanceof Uint8Array ? p.args : p.args ?? {};
        return actions.functionCall(String(p.methodName), args, BigInt(p.gas ?? '30000000000000'), BigInt(p.deposit ?? '0'));
      }),
    };
  });
}

/** Plain data for postMessage (RPC results can hold bigints). */
const plain = (x) => JSON.parse(JSON.stringify(x, (_k, v) => (typeof v === 'bigint' ? v.toString() : v)));

let lastNonce = 0n;

const handlers = {
  /** Is there a guest farm here, and is the game playing as it right now? */
  async status() {
    const secret = guestSecret();
    // an ed25519 secret is the seed and then the public key, 32 bytes each: the account id is the second half in hex
    const bytes = secret?.startsWith('ed25519:') ? base58Decode(secret.slice(8)) : null;
    if (!bytes || bytes.length !== 64) return { network: config.network, accountId: null, active: false };
    const accountId = Array.from(bytes.slice(32), (b) => b.toString(16).padStart(2, '0')).join('');
    let active = false;
    try { active = localStorage.getItem(WALLET_KIND) === 'guest'; } catch { /* storage blocked */ }
    return { network: config.network, accountId, active };
  },

  /** The game server's sign-in message (NEP-413), and nothing else. */
  async signMessage({ message, recipient, nonce }) {
    if (message !== SIGN_IN_MESSAGE) throw new Error('A guest farm only signs the Farmverse sign-in here.');
    if (typeof recipient !== 'string' || !(nonce instanceof Uint8Array) || nonce.length !== 32) throw new Error('That sign-in request is malformed.');
    const key = await guestKey();
    const publicKey = key.getPublicKey().toString();
    const signature = key.sign(await nep413Hash(message, nonce, recipient)).signature;
    return { accountId: accountOf(publicKey), publicKey, signature: b64(signature) };
  },

  /** Signs and sends each call itself (the farm pays its own gas): the path for calls the relay doesn't take. */
  async signAndSendTransactions({ transactions }) {
    const calls = await callsOf(transactions);
    const { rpcUrl } = await chain();
    const { Account, JsonRpcProvider, KeyPairSigner } = await near();
    const key = await guestKey();
    const account = new Account(accountOf(key.getPublicKey().toString()), new JsonRpcProvider({ url: rpcUrl }), new KeyPairSigner(key));
    const outcomes = [];
    for (const c of calls) outcomes.push(plain(await account.signAndSendTransaction(c)));
    return outcomes;
  },

  /** Signs each call as a delegate action (NEP-366) for the game server to relay: base64 borsh, NEAR Connect's shape. */
  async signDelegateActions({ delegateActions }) {
    const calls = await callsOf(delegateActions);
    const { rpcUrl } = await chain();
    const { Account, JsonRpcProvider, KeyPairSigner, buildDelegateAction, encodeSignedDelegate } = await near();
    const key = await guestKey();
    const publicKey = key.getPublicKey();
    const account = new Account(accountOf(publicKey.toString()), new JsonRpcProvider({ url: rpcUrl }));
    const [ak, block] = await Promise.all([account.getAccessKey(publicKey), account.provider.viewBlock({ finality: 'final' })]);
    let nonce = BigInt(ak.nonce) > lastNonce ? BigInt(ak.nonce) : lastNonce;
    const maxBlockHeight = BigInt(block.header.height) + TTL_BLOCKS;
    const signer = new KeyPairSigner(key);
    const signedDelegateActions = [];
    for (const c of calls) {
      nonce += 1n;
      const da = buildDelegateAction({ senderId: account.accountId, receiverId: c.receiverId, actions: c.actions, nonce, maxBlockHeight, publicKey });
      const { signedDelegate } = await signer.signDelegateAction(da);
      signedDelegateActions.push(b64(encodeSignedDelegate(signedDelegate)));
    }
    lastNonce = nonce;
    return { signedDelegateActions };
  },
};

window.addEventListener('message', async (e) => {
  const d = e.data;
  if (!d || d.source !== SOURCE || typeof d.type !== 'string' || !Object.hasOwn(handlers, d.type)) return;
  // only the page that framed this one, and only from an address we know
  if (window.parent === window || e.source !== window.parent || !allowed(e.origin)) return;
  const reply = (body) => e.source.postMessage({ source: SOURCE, id: d.id, ...body }, e.origin);
  if (d.network && d.network !== config.network) return reply({ ok: false, error: `This guest farm is on ${config.network}.` });
  try {
    reply({ ok: true, result: await handlers[d.type](d.params ?? {}) });
  } catch (err) {
    reply({ ok: false, error: String(err?.message ?? err) });
  }
});
