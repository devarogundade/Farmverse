import { X509Certificate, createHash, createVerify } from 'node:crypto';
import { GoogleAuth } from 'google-auth-library';

/**
 * Checking a purchase with the store it was made in. Each check answers what the store says about
 * one transaction: bought (and not refunded), still pending, or not valid.
 */
export interface StoreCheck {
  state: 'purchased' | 'pending' | 'invalid';
  productId: string;
  transactionId: string;
}

export interface StoreVerifier {
  google(productId: string, purchaseToken: string): Promise<StoreCheck>;
  apple(signedTransaction: string): Promise<StoreCheck>;
}

/** The Android package and the iOS bundle id the game is sold under. */
export const APP_ID = 'world.farmverse.mobile';

// ---- Google Play -----------------------------------------------------------------------------------

/**
 * Google Play Developer API, purchases.products.get. The function's service account needs "View
 * financial data" in Play Console (Users and permissions) for this to answer.
 */
export async function googleProductPurchase(productId: string, token: string, packageName = APP_ID): Promise<StoreCheck> {
  const auth = new GoogleAuth({ scopes: ['https://www.googleapis.com/auth/androidpublisher'] });
  const client = await auth.getClient();
  const url = `https://androidpublisher.googleapis.com/androidpublisher/v3/applications/${encodeURIComponent(packageName)}/purchases/products/${encodeURIComponent(productId)}/tokens/${encodeURIComponent(token)}`;
  try {
    const res = await client.request<{ purchaseState?: number; orderId?: string; productId?: string }>({ url });
    const p = res.data;
    // purchaseState: 0 purchased, 1 cancelled, 2 pending
    const state = p.purchaseState === 0 ? 'purchased' : p.purchaseState === 2 ? 'pending' : 'invalid';
    return { state, productId: p.productId ?? productId, transactionId: p.orderId ?? '' };
  } catch (e) {
    const status = (e as { response?: { status?: number } }).response?.status;
    if (status === 400 || status === 404 || status === 410) return { state: 'invalid', productId, transactionId: '' };
    throw e;
  }
}

// ---- Apple (StoreKit 2 signed transactions) -------------------------------------------------------

/** SHA-256 of Apple Root CA - G3 (https://www.apple.com/certificateauthority/), the root of App Store signatures. */
/** SHA-256 fingerprint of Apple Root CA - G3 (apple.com/certificateauthority), the root of App Store signatures. */
const APPLE_ROOT = '63:34:3A:BF:B8:9A:6A:03:EB:B5:7E:9B:3F:5F:A7:BE:7C:4F:5C:75:6F:30:17:B3:A8:C4:88:C3:65:3E:91:79';

const b64url = (s: string) => Buffer.from(s.replace(/-/g, '+').replace(/_/g, '/'), 'base64');

/** ES256 JOSE signatures are raw r‖s (64 bytes); node verifies DER. */
function joseToDer(sig: Buffer): Buffer {
  const int = (b: Buffer) => {
    let i = 0;
    while (i < b.length - 1 && b[i] === 0) i++;
    let v = b.subarray(i);
    if ((v[0] ?? 0) & 0x80) v = Buffer.concat([Buffer.from([0]), v]);
    return Buffer.concat([Buffer.from([0x02, v.length]), v]);
  };
  const body = Buffer.concat([int(sig.subarray(0, 32)), int(sig.subarray(32))]);
  return Buffer.concat([Buffer.from([0x30, body.length]), body]);
}

export interface AppleTransaction {
  bundleId: string;
  productId: string;
  transactionId: string;
  revocationDate?: number;
  environment?: string;
}

/**
 * Verify a StoreKit 2 JWS: its x5c chain must end in Apple Root CA - G3 (pinned by fingerprint), every
 * certificate in date, each signed by the next, and the payload signed by the leaf. Returns the payload.
 */
export function verifyAppleJws(jws: string, nowMs = Date.now()): AppleTransaction | null {
  const parts = jws.split('.');
  if (parts.length !== 3) return null;
  const [h, p, s] = parts as [string, string, string];
  let header: { alg?: string; x5c?: string[] };
  try {
    header = JSON.parse(b64url(h).toString('utf8'));
  } catch {
    return null;
  }
  if (header.alg !== 'ES256' || !Array.isArray(header.x5c) || header.x5c.length < 2) return null;
  let chain: X509Certificate[];
  try {
    chain = header.x5c.map((c) => new X509Certificate(Buffer.from(c, 'base64')));
  } catch {
    return null;
  }
  const root = chain[chain.length - 1]!;
  if (root.fingerprint256 !== APPLE_ROOT) return null;
  for (let i = 0; i < chain.length; i++) {
    const cert = chain[i]!;
    if (Date.parse(cert.validFrom) > nowMs || Date.parse(cert.validTo) < nowMs) return null;
    const issuer = chain[Math.min(i + 1, chain.length - 1)]!;
    if (!cert.verify(issuer.publicKey)) return null;
  }
  const verifier = createVerify('sha256');
  verifier.update(`${h}.${p}`);
  if (!verifier.verify(chain[0]!.publicKey, joseToDer(b64url(s)))) return null;
  try {
    return JSON.parse(b64url(p).toString('utf8')) as AppleTransaction;
  } catch {
    return null;
  }
}

export function appleCheck(jws: string, bundleId = APP_ID): StoreCheck {
  const t = verifyAppleJws(jws);
  if (!t || t.bundleId !== bundleId) return { state: 'invalid', productId: t?.productId ?? '', transactionId: t?.transactionId ?? '' };
  return { state: t.revocationDate ? 'invalid' : 'purchased', productId: t.productId, transactionId: t.transactionId };
}

export const liveStores: StoreVerifier = {
  google: (productId, token) => googleProductPurchase(productId, token),
  apple: async (jws) => appleCheck(jws),
};

/** A stable short id for a long store token (ledger keys). */
export const tokenId = (s: string) => createHash('sha256').update(s).digest('hex').slice(0, 40);
