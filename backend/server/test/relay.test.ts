import { createHash } from 'node:crypto';
import { actions, buildDelegateAction, encodeDelegateAction, KeyPair, PublicKey } from 'near-api-js';
import { describe, expect, it } from 'vitest';
import { FixedClock } from '../../game/src/core/clock';
import { AppError } from '../../game/src/core/errors';
import { memoryLogger } from '../../game/src/core/logger';
import { MemoryStore } from '../../game/src/infra/memoryStore';
import { relay, welcome, type DelegateWire, type Relayer } from '../src/chain/relay';

const ITEMS = 'items.farmverse.testnet';
const key = KeyPair.fromRandom('ed25519');

function delegate(over: Partial<Omit<DelegateWire, 'signature'>> = {}): DelegateWire {
  const args = Buffer.from(JSON.stringify({ token_id: 'g-1' }));
  const w = {
    senderId: 'alice.testnet', receiverId: ITEMS, method: 'redeem', args: args.toString('base64'), gas: '30000000000000',
    deposit: '0', nonce: '7', maxBlockHeight: '1100', publicKey: key.getPublicKey().toString(), ...over,
  };
  const da = buildDelegateAction({
    senderId: w.senderId, receiverId: w.receiverId,
    actions: [actions.functionCall(w.method, Buffer.from(w.args, 'base64'), BigInt(w.gas), BigInt(w.deposit))],
    nonce: BigInt(w.nonce), maxBlockHeight: BigInt(w.maxBlockHeight), publicKey: PublicKey.fromString(w.publicKey),
  });
  const hash = createHash('sha256').update(encodeDelegateAction(da)).digest();
  return { ...w, signature: Buffer.from(key.sign(hash).signature).toString('base64') };
}

function fakeRelayer() {
  const relayed: string[] = [];
  const sent: string[] = [];
  const r: Relayer = {
    accountId: 'relayer.testnet',
    blockHeight: async () => 1000,
    relay: async (s) => { relayed.push(s.delegateAction.senderId); return 'TxHash'; },
    balanceOf: async () => null,
    send: async (a, y) => { sent.push(`${a}:${y}`); },
  };
  return { r, relayed, sent };
}

const env = () => ({ store: new MemoryStore(), clock: new FixedClock(1_800_000_000_000), log: memoryLogger() });
const code = (p: Promise<unknown>) => p.then(() => 'ok', (e: unknown) => (e instanceof AppError ? e.code : String(e)));

describe('sponsored relay', () => {
  it('relays a signed, deposit-free redeem for the signed-in account', async () => {
    const { r, relayed } = fakeRelayer();
    const deps = { env: env(), relayer: r, itemsContract: ITEMS, allow: async () => true };
    expect(await relay(deps, 'alice.testnet', delegate(), memoryLogger())).toEqual({ txHash: 'TxHash' });
    expect(relayed).toEqual(['alice.testnet']);
  });

  it('refuses anything outside the policy', async () => {
    const { r } = fakeRelayer();
    const deps = { env: env(), relayer: r, itemsContract: ITEMS, allow: async () => true };
    expect(await code(relay(deps, 'bob.testnet', delegate(), memoryLogger()))).toBe('PERMISSION_DENIED');
    expect(await code(relay(deps, 'alice.testnet', delegate({ method: 'nft_transfer' }), memoryLogger()))).toBe('PERMISSION_DENIED');
    expect(await code(relay(deps, 'alice.testnet', delegate({ receiverId: 'evil.testnet' }), memoryLogger()))).toBe('PERMISSION_DENIED');
    expect(await code(relay(deps, 'alice.testnet', delegate({ deposit: '1' }), memoryLogger()))).toBe('PERMISSION_DENIED');
    expect(await code(relay(deps, 'alice.testnet', delegate({ maxBlockHeight: '999' }), memoryLogger()))).toBe('INVALID_ARGUMENT');
    expect(await code(relay(deps, 'alice.testnet', delegate({ maxBlockHeight: '5000' }), memoryLogger()))).toBe('INVALID_ARGUMENT');
    const tampered = { ...delegate(), args: Buffer.from('{"token_id":"g-2"}').toString('base64') };
    expect(await code(relay(deps, 'alice.testnet', tampered, memoryLogger()))).toBe('UNAUTHENTICATED');
    expect(await code(relay({ ...deps, allow: async () => false }, 'alice.testnet', delegate(), memoryLogger()))).toBe('RATE_LIMITED');
  });

  it('a new account gets its welcome NEAR once', async () => {
    const { r, sent } = fakeRelayer();
    const e = env();
    expect(await welcome(e, r, 'alice.testnet', 50n, 20n, memoryLogger())).toBe(true);
    expect(await welcome(e, r, 'alice.testnet', 50n, 20n, memoryLogger())).toBe(false);
    expect(sent).toEqual(['alice.testnet:50']);
  });
});
