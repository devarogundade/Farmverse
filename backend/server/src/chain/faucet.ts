/**
 * The testnet faucet (plan 15 §6.6): each signed-in account may take 100 test FARM a day, plus 1 test NEAR
 * the first time (gas and storage for its first mints). It pays from its own `faucet.` account, never the
 * treasury, and there is no faucet on mainnet.
 *
 * The claim is booked in Postgres first (so two tabs can't both claim), then the transfers are sent; if
 * sending fails the booking is undone, so the player can try again.
 */
import { Account, JsonRpcProvider, type KeyPairString, teraToGas } from 'near-api-js';
import { AppError, fail } from '../../../game/src/core/errors';
import type { Logger } from '../../../game/src/core/logger';
import type { Env } from '../../../game/src/game/services';

const DAY_MS = 86_400_000;
/** What NEP-145 storage for one FT account costs on the FARM contract. */
const FT_STORAGE = 12_500_000_000_000_000_000_000n;

export interface FaucetSender {
  readonly accountId: string;
  registered(accountId: string): Promise<boolean>;
  register(accountId: string): Promise<void>;
  sendFarm(accountId: string, units: bigint): Promise<void>;
  sendNear(accountId: string, yocto: bigint): Promise<void>;
}

export interface FaucetDeps {
  readonly env: Env;
  readonly sender: FaucetSender;
  /** FARM units (18 decimals) per daily claim. */
  readonly farmUnits: bigint;
  /** yoctoNEAR given once per account. */
  readonly nearYocto: bigint;
  /** Claims the faucet pays in one UTC day across all players. */
  readonly dailyCap: number;
}

export interface FaucetClaim {
  /** FARM sent, whole units as a decimal string. */
  farm: string;
  /** NEAR sent (0 after the first claim). */
  near: string;
  /** When the next claim opens (epoch ms). */
  nextAt: number;
}

interface FaucetDoc {
  farmDay?: string;
  nearGiven?: boolean;
  updatedAt: number;
}

const userPath = (uid: string) => `players/${uid}/state/faucet`;
const dayPath = (day: string) => `ops/faucet_${day}`;
const utcDay = (ms: number) => new Date(ms).toISOString().slice(0, 10);
const whole = (units: bigint, decimals: number) => {
  const base = 10n ** BigInt(decimals);
  const frac = (units % base).toString().padStart(decimals, '0').replace(/0+$/, '');
  return `${units / base}${frac ? '.' + frac : ''}`;
};

export async function claimFaucet(deps: FaucetDeps, uid: string, accountId: string, log: Logger): Promise<FaucetClaim> {
  const { env } = deps;
  const nowMs = env.clock.nowMs();
  const today = utcDay(nowMs);
  const nextAt = (Math.floor(nowMs / DAY_MS) + 1) * DAY_MS;

  const booked = await env.store.runTransaction(async (tx) => {
    const mine = (await tx.get(userPath(uid))).data as FaucetDoc | undefined;
    const day = (await tx.get(dayPath(today))).data as { claims?: number } | undefined;
    if (mine?.farmDay === today) return { wait: true as const };
    if ((day?.claims ?? 0) >= deps.dailyCap) return { closed: true as const };
    const giveNear = !mine?.nearGiven && deps.nearYocto > 0n;
    tx.set(userPath(uid), { farmDay: today, nearGiven: Boolean(mine?.nearGiven) || giveNear, updatedAt: nowMs });
    tx.set(dayPath(today), { claims: (day?.claims ?? 0) + 1, updatedAt: nowMs });
    return { giveNear, before: mine };
  });
  if ('wait' in booked) fail('FAUCET_WAIT', { nextAt });
  if ('closed' in booked) fail('FAUCET_CLOSED');

  try {
    if (!(await deps.sender.registered(accountId))) await deps.sender.register(accountId);
    await deps.sender.sendFarm(accountId, deps.farmUnits);
    if (booked.giveNear) await deps.sender.sendNear(accountId, deps.nearYocto);
  } catch (e) {
    log.error('faucet transfer failed; claim undone', { uid, accountId, error: String(e) });
    await env.store.runTransaction(async (tx) => {
      const day = (await tx.get(dayPath(today))).data as { claims?: number } | undefined;
      tx.set(userPath(uid), { ...(booked.before ?? {}), updatedAt: nowMs });
      tx.set(dayPath(today), { claims: Math.max(0, (day?.claims ?? 1) - 1), updatedAt: nowMs });
    });
    throw new AppError('FAUCET_CLOSED', {}, { cause: e });
  }
  log.info('faucet claim', { uid, accountId, near: booked.giveNear });
  return { farm: whole(deps.farmUnits, 18), near: booked.giveNear ? whole(deps.nearYocto, 24) : '0', nextAt };
}

/** Sends from the faucet account with its own key (near-api-js). */
export function nearFaucetSender(rpcUrl: string, faucetId: string, privateKey: string, farmContract: string): FaucetSender {
  const provider = new JsonRpcProvider({ url: rpcUrl });
  const account = new Account(faucetId, provider, privateKey as KeyPairString);
  return {
    accountId: faucetId,
    async registered(accountId) {
      const r = await provider.callFunction({ contractId: farmContract, method: 'storage_balance_of', args: { account_id: accountId } });
      return r !== null && r !== undefined;
    },
    async register(accountId) {
      await account.callFunction({ contractId: farmContract, methodName: 'storage_deposit', args: { account_id: accountId }, deposit: FT_STORAGE, gas: teraToGas('30') });
    },
    async sendFarm(accountId, units) {
      await account.callFunction({
        contractId: farmContract,
        methodName: 'ft_transfer',
        args: { receiver_id: accountId, amount: units.toString(), memo: 'Farmverse test FARM' },
        deposit: 1n,
        gas: teraToGas('30'),
      });
    },
    async sendNear(accountId, yocto) {
      await account.transfer({ receiverId: accountId, amount: yocto });
    },
  };
}
