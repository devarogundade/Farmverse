import { createPublicKey, verify } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import { AUDIT_BASE_PER_HOUR, AUDIT_FIRST_WORTH, auditUpload } from '../../game/src/game/islands';
import { base58Decode } from '../src/auth/nep413';
import { packEntitlement, redeemOps, titleOf, tokenIdFor } from '../src/mint/mint';
import { parseRedeems, type TxStatus } from '../src/mint/nearChain';
import { VoucherSigner, voucherBytes, type MintVoucher } from '../src/mint/voucher';

const VECTOR: MintVoucher = {
  contract_id: 'items.farmverse.near',
  token_id: 'g-1',
  receiver_id: 'alice.near',
  kind: 'building',
  item_id: 'chicken_coop',
  quantity: 1,
  props: '{"level":3}',
  title: 'Chicken coop',
  media: 'items/chicken_coop.png',
  expires_at_ms: 1_800_000_060_000,
};

describe('mint voucher', () => {
  it('encodes byte-for-byte as the items contract does (same hex pinned in chain/contracts/items tests)', () => {
    expect(voucherBytes(VECTOR).toString('hex')).toBe(
      '6661726d76657273653a6974656d733a6d696e743a7631140000006974656d732e6661726d76657273652e6e65617203000000672d310a000000616c6963652e6e656172020c000000636869636b656e5f636f6f7001000000000000000b0000007b226c6576656c223a337d0c000000436869636b656e20636f6f70160000006974656d732f636869636b656e5f636f6f702e706e67603a5d18a3010000',
    );
  });

  it('signs with an ed25519 key whose public half verifies the voucher', () => {
    const signer = new VoucherSigner(Buffer.alloc(32, 9));
    const sig = Buffer.from(signer.sign(VECTOR), 'base64');
    const raw = base58Decode(signer.publicKey.slice('ed25519:'.length));
    const key = createPublicKey({ key: Buffer.concat([Buffer.from('302a300506032b6570032100', 'hex'), raw]), format: 'der', type: 'spki' });
    expect(verify(null, voucherBytes(VECTOR), key, sig)).toBe(true);
    expect(verify(null, voucherBytes({ ...VECTOR, quantity: 2 }), key, sig)).toBe(false);
  });

  it('token ids are deterministic per player and key, and valid on the contract', () => {
    expect(tokenIdFor('uA', 'k-12345678')).toBe(tokenIdFor('uA', 'k-12345678'));
    expect(tokenIdFor('uA', 'k-12345678')).not.toBe(tokenIdFor('uB', 'k-12345678'));
    expect(tokenIdFor('uA', 'k-12345678')).toMatch(/^g-[0-9a-f]{30}$/);
  });

  it('titles read well', () => {
    expect(titleOf('chicken_coop')).toBe('Chicken coop');
    expect(titleOf('wheat', 250)).toBe('Wheat ×250');
  });
});

describe('upload audit (shadow mode)', () => {
  const now = 1_800_000_000_000;
  it('lets honest growth through', () => {
    expect(auditUpload(10_000, 10_000 + AUDIT_BASE_PER_HOUR - 1, 3_600_000, now)).toBeUndefined();
  });
  it('flags a jump no island could make in the time', () => {
    const a = auditUpload(10_000, 5_000_000, 3_600_000, now);
    expect(a).toMatchObject({ reason: 'worth_jump', fromWorth: 10_000, toWorth: 5_000_000 });
  });
  it('a quick re-upload still gets the minimum window, not zero', () => {
    expect(auditUpload(1_000, 1_000 + AUDIT_BASE_PER_HOUR / 4 - 1, 1_000, now)).toBeUndefined();
  });
  it('a first upload may not start rich', () => {
    expect(auditUpload(undefined, AUDIT_FIRST_WORTH, 0, now)).toBeUndefined();
    expect(auditUpload(undefined, AUDIT_FIRST_WORTH + 1, 0, now)?.reason).toBe('first_upload_too_rich');
  });
});

describe('redeems from a NEAR transaction', () => {
  const event = (owner: string) =>
    `EVENT_JSON:${JSON.stringify({
      standard: 'farmverse_items',
      version: '1.0.0',
      event: 'item_redeem',
      data: [{ token_id: 'g-1', owner_id: owner, item: { kind: 'good', item_id: 'wheat', quantity: 250, props: '{}' } }],
    })}`;
  const tx = (over: Partial<TxStatus> = {}, owner = 'alice.testnet', executor = 'items.farmverse.testnet'): TxStatus => ({
    status: { SuccessValue: '' },
    transaction: { signer_id: 'alice.testnet', receiver_id: 'items.farmverse.testnet' },
    receipts_outcome: [{ outcome: { executor_id: executor, logs: [event(owner)], status: { SuccessValue: '' } } }],
    ...over,
  });

  it('reads the item_redeem event of a successful transaction by the signer', () => {
    expect(parseRedeems(tx(), 'alice.testnet', 'items.farmverse.testnet')).toEqual([
      { tokenId: 'g-1', ownerId: 'alice.testnet', item: { kind: 'good', item_id: 'wheat', quantity: 250, props: '{}' } },
    ]);
  });
  it('ignores failed transactions', () => {
    expect(parseRedeems(tx({ status: { Failure: {} } }), 'alice.testnet', 'items.farmverse.testnet')).toEqual([]);
  });
  it("ignores someone else's transaction", () => {
    expect(parseRedeems(tx(), 'mallory.testnet', 'items.farmverse.testnet')).toEqual([]);
  });
  it('ignores look-alike events from another contract', () => {
    expect(parseRedeems(tx({}, 'alice.testnet', 'evil.testnet'), 'alice.testnet', 'items.farmverse.testnet')).toEqual([]);
  });
  it('ignores a redeem of a token the signer does not own', () => {
    expect(parseRedeems(tx({}, 'bob.testnet'), 'alice.testnet', 'items.farmverse.testnet')).toEqual([]);
  });
});

describe('what a redeem credits', () => {
  it('goods go back to the farmhouse; buildings come back held, with their level', () => {
    expect(redeemOps({ kind: 'good', item_id: 'wheat', quantity: 250, props: '{}' })).toEqual([{ op: 'give', item: 'wheat', n: 250 }]);
    expect(redeemOps({ kind: 'building', item_id: 'chicken_coop', quantity: 1, props: '{"level":3,"price":120}' })).toEqual([
      { op: 'hold', id: 'chicken_coop', level: 3, price: 120 },
    ]);
    expect(redeemOps({ kind: 'boost', item_id: 'fert', quantity: 2, props: '{}' })).toEqual([]);
  });
  it('packs map to entitlements', () => {
    expect(packEntitlement('gems_500', 2)).toEqual({ kind: 'bundle', contents: { coins: 0, gems: 1000, boosts: {} } });
    expect(packEntitlement('farm_pass', 1)).toEqual({ kind: 'pass' });
    expect(packEntitlement('mystery', 1)).toBeUndefined();
  });
});
