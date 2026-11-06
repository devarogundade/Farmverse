import { describe, expect, it } from 'vitest';
import { FixedClock } from '../../game/src/core/clock';
import { AppError } from '../../game/src/core/errors';
import { memoryLogger } from '../../game/src/core/logger';
import { MemoryStore } from '../../game/src/infra/memoryStore';
import { claimFaucet, type FaucetDeps, type FaucetSender } from '../src/chain/faucet';

const E18 = 10n ** 18n;
const E24 = 10n ** 24n;

function fakeSender(failFarm = false) {
  const sent: string[] = [];
  const registered = new Set<string>();
  const sender: FaucetSender = {
    accountId: 'faucet.farmverse.testnet',
    registered: async (a) => registered.has(a),
    register: async (a) => {
      registered.add(a);
      sent.push(`register ${a}`);
    },
    sendFarm: async (a, units) => {
      if (failFarm) throw new Error('rpc down');
      sent.push(`farm ${a} ${units / E18}`);
    },
    sendNear: async (a, yocto) => {
      sent.push(`near ${a} ${yocto / E24}`);
    },
  };
  return { sender, sent };
}

function deps(sender: FaucetSender, dailyCap = 100) {
  const clock = new FixedClock(Date.UTC(2026, 10, 7, 9));
  const store = new MemoryStore();
  const d: FaucetDeps = { env: { store, clock, log: memoryLogger() }, sender, farmUnits: 100n * E18, nearYocto: E24, dailyCap };
  return { d, clock };
}

const code = (p: Promise<unknown>) => p.then(() => 'ok', (e: unknown) => (e instanceof AppError ? e.code : String(e)));

describe('testnet faucet', () => {
  it('gives FARM every day and NEAR only the first time', async () => {
    const { sender, sent } = fakeSender();
    const { d, clock } = deps(sender);
    const first = await claimFaucet(d, 'u1', 'alice.testnet', memoryLogger());
    expect(first).toEqual({ farm: '100', near: '1', nextAt: Date.UTC(2026, 10, 8) });
    expect(sent).toEqual(['register alice.testnet', 'farm alice.testnet 100', 'near alice.testnet 1']);

    expect(await code(claimFaucet(d, 'u1', 'alice.testnet', memoryLogger()))).toBe('FAUCET_WAIT');

    clock.set(Date.UTC(2026, 10, 8, 1));
    const second = await claimFaucet(d, 'u1', 'alice.testnet', memoryLogger());
    expect(second.near).toBe('0');
    expect(sent.slice(3)).toEqual(['farm alice.testnet 100']);
  });

  it('stops at the daily cap across players', async () => {
    const { sender } = fakeSender();
    const { d } = deps(sender, 1);
    await claimFaucet(d, 'u1', 'alice.testnet', memoryLogger());
    expect(await code(claimFaucet(d, 'u2', 'bob.testnet', memoryLogger()))).toBe('FAUCET_CLOSED');
  });

  it('a failed transfer undoes the claim so it can be tried again', async () => {
    const failing = fakeSender(true);
    const { d } = deps(failing.sender);
    expect(await code(claimFaucet(d, 'u1', 'alice.testnet', memoryLogger()))).toBe('FAUCET_CLOSED');
    const working = fakeSender();
    const retry = await claimFaucet({ ...d, sender: working.sender }, 'u1', 'alice.testnet', memoryLogger());
    expect(retry.near).toBe('1');
  });
});
