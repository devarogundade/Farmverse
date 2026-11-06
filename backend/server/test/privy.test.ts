import { generateKeyPairSync, sign } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import { AppError } from '../../game/src/core/errors';
import { ensureNearWallet, privyUserOf } from '../src/auth/privy';

const { privateKey, publicKey } = generateKeyPairSync('ec', { namedCurve: 'P-256' });
const pem = publicKey.export({ type: 'spki', format: 'pem' }).toString();
const b64u = (b: Buffer | string) => Buffer.from(b).toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
function token(payload: object, alg = 'ES256') {
  const h = b64u(JSON.stringify({ alg, typ: 'JWT' }));
  const p = b64u(JSON.stringify(payload));
  const s = sign('sha256', Buffer.from(`${h}.${p}`), { key: privateKey, dsaEncoding: 'ieee-p1363' });
  return `${h}.${p}.${b64u(s)}`;
}
const cfg = { appId: 'app_1', appSecret: 'secret_123456', verificationKey: pem, nowMs: () => 1_800_000_000_000 };
const good = { sub: 'did:privy:abc', aud: 'app_1', iss: 'privy.io', exp: 1_800_000_100 };
const code = (p: Promise<unknown>) => p.then(() => 'ok', (e: unknown) => (e instanceof AppError ? e.code : String(e)));

describe('privy social login', () => {
  it('accepts a valid access token and refuses others', async () => {
    expect(await privyUserOf(cfg, token(good))).toBe('did:privy:abc');
    expect(await code(privyUserOf(cfg, token({ ...good, aud: 'other' })))).toBe('UNAUTHENTICATED');
    expect(await code(privyUserOf(cfg, token({ ...good, exp: 1 })))).toBe('UNAUTHENTICATED');
    expect(await code(privyUserOf(cfg, token(good).slice(0, -4) + 'AAAA'))).toBe('UNAUTHENTICATED');
  });

  it("uses the app's published keys when no key is configured", async () => {
    const jwk = { ...publicKey.export({ format: 'jwk' }), kid: 'k1' };
    const fetchImpl = (async () => new Response(JSON.stringify({ keys: [jwk] }))) as unknown as typeof fetch;
    const h = b64u(JSON.stringify({ alg: 'ES256', kid: 'k1' }));
    const p = b64u(JSON.stringify(good));
    const s = b64u(sign('sha256', Buffer.from(`${h}.${p}`), { key: privateKey, dsaEncoding: 'ieee-p1363' }));
    expect(await privyUserOf({ appId: 'app_1', appSecret: 'x'.repeat(12), fetchImpl, nowMs: cfg.nowMs }, `${h}.${p}.${s}`)).toBe('did:privy:abc');
  });

  it('creates a NEAR wallet only when the user has none', async () => {
    const calls: string[] = [];
    const fetchImpl = (async (url: string, init?: RequestInit) => {
      calls.push(`${init?.method ?? 'GET'} ${url}`);
      if (url.includes('/users/')) return new Response(JSON.stringify({ linked_accounts: [{ type: 'email' }] }));
      return new Response(JSON.stringify({ address: 'a'.repeat(64) }));
    }) as typeof fetch;
    expect(await ensureNearWallet({ ...cfg, fetchImpl }, token(good))).toEqual({ address: 'a'.repeat(64), created: true });
    expect(calls).toEqual(['GET https://api.privy.io/v1/users/did%3Aprivy%3Aabc', 'POST https://api.privy.io/v1/wallets']);
  });
});
