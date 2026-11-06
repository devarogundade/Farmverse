import { generateKeyPairSync, sign } from 'node:crypto';
import { CALLABLES } from '@farmverse/contracts';
import { describe, expect, it } from 'vitest';
import { FixedClock } from '../../functions/src/core/clock';
import { memoryLogger } from '../../functions/src/core/logger';
import { MemoryStore } from '../../functions/src/infra/memoryStore';
import { canRead, checkUpdate, listLimit } from '../src/access';
import { base58Decode, base58Encode, nep413Bytes, nep413Hash, verifyNep413 } from '../src/auth/nep413';
import { accountOnNetwork } from '../src/auth/sessions';
import { callDefinitions, webMessenger, type Runtime } from '../src/calls';
import { assertQuery } from '../src/store/query';

describe('access (port of firestore.rules)', () => {
  it('own player docs and allowed aggregates only', () => {
    expect(canRead('a', 'players/a')).toBe(true);
    expect(canRead('a', 'players/a/state/island')).toBe(true);
    expect(canRead('a', 'players/a/state/walletDay')).toBe(false);
    expect(canRead('a', 'players/a/ledger/x')).toBe(false);
    expect(canRead('a', 'players/b/state/island')).toBe(false);
    expect(canRead('a', 'players/a/private/settings')).toBe(true);
    expect(canRead('a', 'players/a/notifications/n1')).toBe(true);
  });

  it('public documents', () => {
    expect(canRead('a', 'config/catalogue')).toBe(true);
    expect(canRead('a', 'news/n1')).toBe(true);
    expect(canRead('a', 'leaderboards/w1/players/b')).toBe(true);
    expect(canRead('a', 'usernames/bob')).toBe(false);
    expect(canRead('a', 'couponCodes/X')).toBe(false);
  });

  it('list limits', () => {
    expect(listLimit('a', 'players/a/notifications')).toBe(50);
    expect(listLimit('a', 'players/b/notifications')).toBe(0);
    expect(listLimit('a', 'news')).toBe(20);
    expect(listLimit('a', 'leaderboards/w1/players')).toBe(50);
    expect(listLimit('a', 'players')).toBe(0);
  });

  const now = 1_800_000_000_000;
  const settings = {
    allowVisitors: true, pushEnabled: false, quietStart: 22, quietEnd: 8, locale: 'en', timezone: 'UTC',
    reduceMotion: false, textScale: 100,
  };

  it('settings: listed fields with valid values only', () => {
    expect(checkUpdate('a', 'players/a/private/settings', { textScale: 120, music: false }, settings, now)).toBeNull();
    expect(checkUpdate('a', 'players/a/private/settings', { textScale: 151 }, settings, now)).toBe('invalid textScale');
    expect(checkUpdate('a', 'players/a/private/settings', { gems: 1 }, settings, now)).toBe('gems may not change');
    expect(checkUpdate('a', 'players/a/private/settings', { pushTopics: { raids: false } }, settings, now)).toBeNull();
    expect(checkUpdate('a', 'players/a/private/settings', { pushTopics: { spam: true } }, settings, now)).toBe('invalid pushTopics');
    expect(checkUpdate('b', 'players/a/private/settings', { music: true }, settings, now)).toBe('not writable');
  });

  it('notifications: readAt once, close to now', () => {
    const unread = { readAt: 0, title: 'x' };
    expect(checkUpdate('a', 'players/a/notifications/n1', { readAt: now }, unread, now)).toBeNull();
    expect(checkUpdate('a', 'players/a/notifications/n1', { readAt: now + 600_000 }, unread, now)).toBe('readAt must be now');
    expect(checkUpdate('a', 'players/a/notifications/n1', { readAt: now }, { readAt: 5 }, now)).toBe('already read');
    expect(checkUpdate('a', 'players/a/notifications/n1', { title: 'y' }, unread, now)).toBe('only readAt may change');
    expect(checkUpdate('a', 'players/a/state/wallet', { gems: 9 }, { gems: 1 }, now)).toBe('not writable');
  });
});

describe('NEP-413', () => {
  const { publicKey, privateKey } = generateKeyPairSync('ed25519');
  const raw = publicKey.export({ format: 'der', type: 'spki' }).subarray(-32);
  const nearKey = `ed25519:${base58Encode(raw)}`;
  const payload = { message: 'Sign in to Farmverse', nonce: new Uint8Array(32).fill(7), recipient: 'play.farmverse.world', callbackUrl: null };
  const signature = sign(null, nep413Hash(payload), privateKey).toString('base64');

  it('base58 round-trips, leading zeros included', () => {
    const bytes = Buffer.from([0, 0, 1, 2, 255]);
    expect(base58Decode(base58Encode(bytes))).toEqual(bytes);
  });

  it('starts with the 2^31+413 tag, little-endian', () => {
    expect([...nep413Bytes(payload).subarray(0, 4)]).toEqual([0x9d, 0x01, 0x00, 0x80]);
  });

  it('accepts a real signature and rejects tampering', () => {
    expect(verifyNep413({ accountId: 'alice.testnet', publicKey: nearKey, signature }, payload)).toBe(true);
    expect(verifyNep413({ accountId: 'alice.testnet', publicKey: nearKey, signature }, { ...payload, recipient: 'evil.example' })).toBe(false);
    expect(verifyNep413({ accountId: 'alice.testnet', publicKey: nearKey, signature }, { ...payload, nonce: new Uint8Array(32) })).toBe(false);
    expect(verifyNep413({ accountId: 'alice.testnet', publicKey: 'secp256k1:abc', signature }, payload)).toBe(false);
  });

  it('accounts must belong to the configured network', () => {
    expect(accountOnNetwork('alice.testnet', 'testnet')).toBe(true);
    expect(accountOnNetwork('alice.near', 'testnet')).toBe(false);
    expect(accountOnNetwork('alice.near', 'mainnet')).toBe(true);
    expect(accountOnNetwork('a'.repeat(64).replace(/a/g, 'f'), 'mainnet')).toBe(true);
    expect(accountOnNetwork('Alice.near', 'mainnet')).toBe(false);
  });
});

describe('call registry', () => {
  const rt: Runtime = {
    store: new MemoryStore(),
    clock: new FixedClock(0),
    log: memoryLogger(),
    engine: {} as Runtime['engine'],
    wakes: { schedule: async () => {} },
    tasks: { schedule: async () => {} },
    messenger: webMessenger(memoryLogger()),
    deleteAccount: async () => {},
  };

  it('serves every callable in the contract, under its wire name', () => {
    const names = [...callDefinitions(rt).keys()].sort();
    expect(names).toEqual(Object.values(CALLABLES).map((c) => c.name).sort());
  });

  it('only coupon redemption is admin-only', () => {
    const admin = [...callDefinitions(rt).values()].filter((d) => d.auth === 'admin').map((d) => d.spec.name);
    expect(admin).toEqual(['coupons-redeem']);
  });

  it('the web messenger sends nothing', async () => {
    expect(await webMessenger(memoryLogger()).send([{ token: 't', title: 'x', body: 'y', data: {}, collapseKey: 'k' }])).toEqual([]);
  });
});

describe('query validation', () => {
  it('rejects unsafe field names and collections', () => {
    expect(() => assertQuery({ collection: 'news', filters: [{ field: "a'; drop", op: '==', value: 1 }], orders: [], limit: 1 })).toThrow('queryable');
    expect(() => assertQuery({ collection: 'news/../x', filters: [], orders: [], limit: 1 })).toThrow('collection');
    expect(() => assertQuery({ collection: 'news', filters: [], orders: [], limit: 0 })).toThrow('limit');
  });
});
