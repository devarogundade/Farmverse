import { createHash } from 'node:crypto';
import { actions, buildDelegateAction, encodeDelegateAction, KeyPair, PublicKey } from 'near-api-js';
import { describe, expect, it } from 'vitest';
import { FixedClock } from '../../game/src/core/clock';
import { AppError } from '../../game/src/core/errors';
import { memoryLogger } from '../../game/src/core/logger';
import { MemoryStore } from '../../game/src/infra/memoryStore';
import { relay, RelayedCallFailed, welcome, type DelegateWire, type Relayer } from '../src/chain/relay';

const ITEMS = 'items.farmverse.testnet';
const MARKET = 'market.farmverse.testnet';
const FARM = 'farm.farmverse.testnet';
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
    relay: async (s) => { relayed.push(s.delegateAction.senderId); return { txHash: 'TxHash', logs: ['EVENT_JSON:{}'] }; },
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
    expect(await relay(deps, 'alice.testnet', delegate(), memoryLogger())).toEqual({ txHash: 'TxHash', logs: ['EVENT_JSON:{}'] });
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

  it('returns the call logs, and the contract’s words when the call failed', async () => {
    const { r } = fakeRelayer();
    const deps = { env: env(), relayer: r, itemsContract: ITEMS, allow: async () => true };
    const fails = { ...r, relay: async () => { throw new RelayedCallFailed('Not listed'); } };
    const err = await relay({ ...deps, relayer: fails }, 'alice.testnet', delegate(), memoryLogger()).catch((e: unknown) => e as AppError);
    expect(err).toBeInstanceOf(AppError);
    expect([(err as AppError).code, (err as AppError).params.reason]).toEqual(['CONFLICT', 'Not listed']);
    const lost = { ...r, relay: async () => { throw new Error('timeout'); } };
    expect(await code(relay({ ...deps, relayer: lost }, 'alice.testnet', delegate(), memoryLogger()))).toBe('INTERNAL');
  });

  it('a new account gets its welcome NEAR once', async () => {
    const { r, sent } = fakeRelayer();
    const e = env();
    expect(await welcome(e, r, 'alice.testnet', 50n, 20n, memoryLogger())).toBe(true);
    expect(await welcome(e, r, 'alice.testnet', 50n, 20n, memoryLogger())).toBe(false);
    expect(sent).toEqual(['alice.testnet:50']);
  });
});

describe('sponsored market calls', () => {
  const MILLI = 10n ** 21n;
  const call = (receiverId: string, method: string, args: object, deposit: bigint | string, gas = '100000000000000', over: Partial<Omit<DelegateWire, 'signature'>> = {}) =>
    delegate({ receiverId, method, args: Buffer.from(JSON.stringify(args)).toString('base64'), deposit: String(deposit), gas, ...over });
  const setup = (budget = 100n * MILLI) => {
    const { r, relayed } = fakeRelayer();
    const deps = { env: env(), relayer: r, itemsContract: ITEMS, marketContract: MARKET, farmContract: FARM, depositBudget: budget, allow: async () => true };
    const run = (w: DelegateWire, as = 'alice.testnet') => code(relay(deps, as, w, memoryLogger()));
    return { deps, run, relayed };
  };
  const buy = (over: { amountTo?: string; deposit?: bigint; gas?: string } = {}) =>
    call(FARM, 'ft_transfer_call', { receiver_id: over.amountTo ?? MARKET, amount: '5000000000000000000', msg: '{"nft_contract_id":"items","token_id":"g-1"}' }, over.deposit ?? 1n, over.gas ?? '300000000000000');

  it('pays with FARM: exactly 1 yoctoNEAR, to the market or (a drop) the items contract only', async () => {
    const { run, relayed } = setup();
    expect(await run(buy())).toBe('ok');
    expect(relayed).toEqual(['alice.testnet']);
    expect(await run(buy({ amountTo: ITEMS }))).toBe('ok');
    expect(await run(buy({ deposit: 0n }))).toBe('PERMISSION_DENIED');
    expect(await run(buy({ deposit: 2n }))).toBe('PERMISSION_DENIED');
    expect(await run(buy({ amountTo: 'evil.testnet' }))).toBe('PERMISSION_DENIED');
    expect(await run(buy({ amountTo: FARM }))).toBe('PERMISSION_DENIED');
    expect(await run(buy({ gas: '300000000000001' }))).toBe('INVALID_ARGUMENT');
    // plain transfers out of the account never
    expect(await run(call(FARM, 'ft_transfer', { receiver_id: 'evil.testnet', amount: '1' }, 1n))).toBe('PERMISSION_DENIED');
  });

  it('approves the market, and only the market, with at most 0.002 NEAR', async () => {
    const { run } = setup();
    const approve = (accountId: string, deposit: bigint, args: object = {}) => call(ITEMS, 'nft_approve', { token_id: 'g-1', account_id: accountId, msg: '{"price":"1"}', ...args }, deposit, '100000000000000');
    expect(await run(approve(MARKET, 2n * MILLI))).toBe('ok');
    expect(await run(approve('evil.testnet', 2n * MILLI))).toBe('PERMISSION_DENIED');
    expect(await run(approve(MARKET, 2n * MILLI + 1n))).toBe('PERMISSION_DENIED');
    expect(await run(approve(MARKET, 0n))).toBe('PERMISSION_DENIED');
    expect(await run(approve(MARKET, 2n * MILLI, { token_id: 5 }))).toBe('PERMISSION_DENIED');
    // other token moves on the items contract stay unsponsored
    expect(await run(call(ITEMS, 'nft_transfer', { receiver_id: 'evil.testnet', token_id: 'g-1' }, 1n))).toBe('PERMISSION_DENIED');
    expect(await run(call(ITEMS, 'nft_revoke_all', { token_id: 'g-1' }, 1n))).toBe('PERMISSION_DENIED');
  });

  it('tops up market storage and FARM registration for the signer only, within caps', async () => {
    const { run } = setup();
    expect(await run(call(MARKET, 'storage_deposit', { account_id: 'alice.testnet' }, 10n * MILLI))).toBe('ok');
    expect(await run(call(MARKET, 'storage_deposit', {}, 10n * MILLI))).toBe('ok');
    expect(await run(call(MARKET, 'storage_deposit', { account_id: 'bob.testnet' }, 10n * MILLI))).toBe('PERMISSION_DENIED');
    expect(await run(call(MARKET, 'storage_deposit', { account_id: 'alice.testnet' }, 50n * MILLI + 1n))).toBe('PERMISSION_DENIED');
    expect(await run(call(FARM, 'storage_deposit', { account_id: 'alice.testnet' }, 1_250_000_000_000_000_000_000n))).toBe('ok');
    expect(await run(call(FARM, 'storage_deposit', { account_id: 'bob.testnet' }, 1_250_000_000_000_000_000_000n))).toBe('PERMISSION_DENIED');
    expect(await run(call(FARM, 'storage_deposit', { account_id: 'alice.testnet' }, 10n * MILLI + 1n))).toBe('PERMISSION_DENIED');
    // getting storage back out is the player's own transaction
    expect(await run(call(MARKET, 'storage_withdraw', {}, 1n))).toBe('PERMISSION_DENIED');
    // gas above a storage call's cap
    expect(await run(call(MARKET, 'storage_deposit', {}, 10n * MILLI, '100000000000001'))).toBe('INVALID_ARGUMENT');
    // arguments that aren't a JSON object
    expect(await run(delegate({ receiverId: MARKET, method: 'storage_deposit', args: Buffer.from('nope').toString('base64'), deposit: String(10n * MILLI) }))).toBe('PERMISSION_DENIED');
  });

  it('hands a worker to the market to rent out: 1 yoctoNEAR, to the market only', async () => {
    const { run } = setup();
    const rentOut = (receiverId: string, deposit = 1n) => call(ITEMS, 'nft_transfer_call', { receiver_id: receiverId, token_id: 's-fh-1', msg: '{"rent":{}}' }, deposit, '300000000000000');
    expect(await run(rentOut(MARKET))).toBe('ok');
    expect(await run(rentOut('evil.testnet'))).toBe('PERMISSION_DENIED');
    expect(await run(rentOut(MARKET, 2n))).toBe('PERMISSION_DENIED');
  });

  it('the seller’s own market calls: exactly 1 yoctoNEAR (collecting: none)', async () => {
    const { run } = setup();
    const args = { nft_contract_id: ITEMS, token_id: 'g-1' };
    for (const m of ['delist', 'update_price', 'offer_cancel', 'rent_withdraw', 'rent_end']) {
      expect(await run(call(MARKET, m, args, 1n))).toBe('ok');
      expect(await run(call(MARKET, m, args, 0n))).toBe('PERMISSION_DENIED');
      expect(await run(call(MARKET, m, args, 2n))).toBe('PERMISSION_DENIED');
    }
    expect(await run(call(MARKET, 'delist', args, 1n, '300000000000001'))).toBe('INVALID_ARGUMENT');
    expect(await run(call(MARKET, 'withdraw_owed', {}, 0n))).toBe('ok');
    expect(await run(call(MARKET, 'withdraw_owed', {}, 1n))).toBe('PERMISSION_DENIED');
  });

  it('other market methods, and anything signed for someone else, are refused', async () => {
    const { run } = setup();
    for (const m of ['set_config', 'pause', 'storage_unregister', 'buy', 'new']) expect(await run(call(MARKET, m, {}, 1n))).toBe('PERMISSION_DENIED');
    expect(await run(call(FARM, 'storage_unregister', {}, 1n))).toBe('PERMISSION_DENIED');
    expect(await run(buy(), 'bob.testnet')).toBe('PERMISSION_DENIED');
  });

  it('counts deposits against a daily budget per account (1 yoctoNEAR payments don’t count)', async () => {
    const { deps, run } = setup(25n * MILLI);
    expect(await run(call(MARKET, 'storage_deposit', {}, 20n * MILLI))).toBe('ok');
    expect(await run(call(MARKET, 'storage_deposit', {}, 10n * MILLI))).toBe('RATE_LIMITED');
    expect(await run(buy())).toBe('ok');
    expect(await run(call(FARM, 'storage_deposit', {}, 5n * MILLI))).toBe('ok'); // exactly the budget
    expect(await run(call(FARM, 'storage_deposit', {}, 1n))).toBe('ok'); // 1 yocto is free
    // another account has its own budget; tomorrow starts again
    expect(await code(relay(deps, 'bob.testnet', call(MARKET, 'storage_deposit', {}, 20n * MILLI, undefined, { senderId: 'bob.testnet' }), memoryLogger()))).toBe('ok');
    const tomorrow = { ...deps, env: { ...deps.env, clock: new FixedClock(1_800_000_000_000 + 86_400_000) } };
    expect(await code(relay(tomorrow, 'alice.testnet', call(MARKET, 'storage_deposit', {}, 20n * MILLI), memoryLogger()))).toBe('ok');
  });

  it('market calls are off without the market and FARM contracts or without a budget', async () => {
    const { r } = fakeRelayer();
    const itemsOnly = { env: env(), relayer: r, itemsContract: ITEMS, allow: async () => true };
    expect(await code(relay(itemsOnly, 'alice.testnet', buy(), memoryLogger()))).toBe('PERMISSION_DENIED');
    const { run } = setup(0n);
    expect(await run(call(MARKET, 'storage_deposit', {}, 10n * MILLI))).toBe('RATE_LIMITED');
    expect(await run(buy())).toBe('ok');
  });

  it('keeps the per-account rate limit', async () => {
    const { deps } = setup();
    expect(await code(relay({ ...deps, allow: async () => false }, 'alice.testnet', buy(), memoryLogger()))).toBe('RATE_LIMITED');
  });
});
