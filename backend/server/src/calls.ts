import { CALLABLES, type CallableSpec } from '@farmverse/contracts';
import type { CallableDefinition } from '../../functions/src/core/callable';
import type { Clock } from '../../functions/src/core/clock';
import type { Logger } from '../../functions/src/core/logger';
import { claimCoupon, redeemCoupon } from '../../functions/src/game/coupons';
import { claimLogin, spinWheel } from '../../functions/src/game/daily';
import { bookLikes, ping as pingSession, storageFull, workersHungry, type EngagementDeps } from '../../functions/src/game/engagement';
import { exportAccount } from '../../functions/src/game/export';
import * as friends from '../../functions/src/game/friends';
import { claimAchievement } from '../../functions/src/game/goals';
import { deleteMessages } from '../../functions/src/game/inbox';
import { sync as syncIsland, type IslandDeps, type WakeScheduler } from '../../functions/src/game/islands';
import { claimPass, joinPass } from '../../functions/src/game/pass';
import * as players from '../../functions/src/game/players';
import { registerPushToken } from '../../functions/src/game/players';
import type { Messenger } from '../../functions/src/game/push';
import { finishRaid, type TaskScheduler } from '../../functions/src/game/raids';
import { claimReferral, claimReward } from '../../functions/src/game/referrals';
import type { Env } from '../../functions/src/game/services';
import { claimChest, claimGift, useBoost } from '../../functions/src/game/shop';
import { getVisit, likeIsland } from '../../functions/src/game/visits';
import { earnGems, spendGems } from '../../functions/src/game/wallet';
import { contentFor } from '../../functions/src/game/content';
import type { IslandEngine } from '../../functions/src/infra/islandEngine';
import type { Store } from '../../functions/src/infra/store';
import { fail } from '../../functions/src/core/errors';
import { mint, redeem, type MintDeps, type MintLimits } from './mint/mint';
import type { ChainReader } from './mint/nearChain';
import type { VoucherSigner } from './mint/voucher';

/** Mint and redeem (absent when no voucher signer is configured: the calls are then refused). */
export interface ChainRuntime {
  readonly reader: ChainReader;
  readonly signer: VoucherSigner;
  readonly itemsContract: string;
  readonly limits: MintLimits;
  readonly scheduleExpiry: (uid: string, tokenId: string, atMs: number) => Promise<void>;
  /** The NEAR account behind a game uid. */
  readonly accountOf: (uid: string) => Promise<string | null>;
}

/** Everything the game handlers need, built once per process (main.ts / worker.ts / tests). */
export interface Runtime {
  readonly store: Store;
  readonly clock: Clock;
  readonly log: Logger;
  readonly engine: IslandEngine;
  readonly wakes: WakeScheduler;
  readonly tasks: TaskScheduler;
  readonly messenger: Messenger;
  /** Removes every trace of a player (documents, name, sessions). */
  readonly deleteAccount: (uid: string, log: Logger) => Promise<void>;
  readonly chain?: ChainRuntime;
}

export function mintDeps(rt: Runtime, log: Logger): MintDeps {
  const c = rt.chain;
  if (!c) fail('MAINTENANCE');
  return {
    env: envOf(rt, log),
    island: islandDeps(rt, log),
    chain: c.reader,
    signer: c.signer,
    itemsContract: c.itemsContract,
    limits: c.limits,
    scheduleExpiry: c.scheduleExpiry,
  };
}

async function walletOf(rt: Runtime, uid: string): Promise<string> {
  const accountId = rt.chain ? await rt.chain.accountOf(uid) : null;
  return accountId ?? fail('UNAUTHENTICATED');
}

export const envOf = (rt: Runtime, log: Logger): Env => ({ store: rt.store, clock: rt.clock, log });

export function islandDeps(rt: Runtime, log: Logger): IslandDeps {
  const env = envOf(rt, log);
  const booking: EngagementDeps = { env, engine: rt.engine, tasks: rt.tasks, messenger: rt.messenger };
  return {
    env,
    engine: rt.engine,
    wakes: rt.wakes,
    onStorageFull: (uid) => storageFull(booking, uid),
    onWorkersHungry: (uid) => workersHungry(booking, uid),
  };
}

export function bookingDeps(rt: Runtime, log: Logger): EngagementDeps {
  return { env: envOf(rt, log), engine: rt.engine, tasks: rt.tasks, messenger: rt.messenger };
}

/** The web has no push: notifications stay in the in-game inbox. */
export const webMessenger = (log: Logger): Messenger => ({
  async send(messages) {
    if (messages.length > 0) log.debug('push skipped on web', { count: messages.length });
    return [];
  },
});

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type AnyDefinition = CallableDefinition<any>;

const def = <S extends CallableSpec>(d: CallableDefinition<S>): AnyDefinition => d as AnyDefinition;

/** Every callable in the contract, by its wire name (`island-sync` …). */
export function callDefinitions(rt: Runtime): ReadonlyMap<string, AnyDefinition> {
  const env = (log: Logger) => envOf(rt, log);
  const list: AnyDefinition[] = [
    def({ spec: CALLABLES.playerCreate, auth: 'required', handler: async ({ uid, data, log }) => players.create(env(log), uid, { displayName: data.displayName, avatarId: data.avatarId }) }),
    def({ spec: CALLABLES.playerUpdateProfile, auth: 'required', handler: async ({ uid, data, log }) => players.updateProfile(env(log), uid, data) }),
    def({ spec: CALLABLES.playerSuggestNames, auth: 'required', handler: async ({ data, log }) => players.suggestNames(env(log), data.count) }),
    def({ spec: CALLABLES.pushRegisterToken, auth: 'required', handler: async ({ uid, data, log }) => registerPushToken(env(log), uid, data.installationId, data.token, data.meta.platform) }),
    def({ spec: CALLABLES.sessionPing, auth: 'required', handler: async ({ uid, data, log }) => pingSession(bookingDeps(rt, log), uid, data.offsetMin, data.raidAck === true) }),
    def({ spec: CALLABLES.raidFinish, auth: 'required', handler: async ({ uid, data, log }) => finishRaid(bookingDeps(rt, log), uid, data.day, data.outcome) }),
    def({
      spec: CALLABLES.accountDelete,
      auth: 'required',
      handler: async ({ uid, log }) => {
        await rt.deleteAccount(uid, log);
        return { deletedAt: rt.clock.nowMs() };
      },
    }),
    def({ spec: CALLABLES.accountExport, auth: 'required', handler: async ({ uid, log }) => exportAccount(env(log), uid) }),
    def({ spec: CALLABLES.friendsCreateInvite, auth: 'required', handler: async ({ uid, log }) => friends.createInvite(env(log), uid) }),
    def({ spec: CALLABLES.friendsAccept, auth: 'required', handler: async ({ uid, data, log }) => friends.acceptInvite(env(log), uid, data.code) }),
    def({ spec: CALLABLES.contentManifest, auth: 'required', handler: async ({ data, log }) => contentFor(env(log), data.meta.platform) }),
    def({ spec: CALLABLES.islandSync, auth: 'required', handler: async ({ uid, data, log }) => syncIsland(islandDeps(rt, log), uid, data.adopt, data.mirror === true, data.device) }),
    def({ spec: CALLABLES.referralClaim, auth: 'required', handler: async ({ uid, log }) => claimReferral(env(log), uid) }),
    def({ spec: CALLABLES.rewardsClaim, auth: 'required', handler: async ({ uid, data, log }) => claimReward(env(log), uid, data.id) }),
    def({ spec: CALLABLES.walletSpend, auth: 'required', handler: async ({ uid, data, log }) => spendGems(env(log), uid, data.offerId, data.key) }),
    def({ spec: CALLABLES.walletEarn, auth: 'required', handler: async ({ uid, data, log }) => earnGems(env(log), uid, data.source, data.amount, data.key) }),
    def({ spec: CALLABLES.boostUse, auth: 'required', handler: async ({ uid, data, log }) => useBoost(env(log), uid, data.boostId, data.key) }),
    def({ spec: CALLABLES.shopClaimGift, auth: 'required', handler: async ({ uid, data, log }) => claimGift(env(log), uid, data.key) }),
    def({ spec: CALLABLES.shopClaimChest, auth: 'required', handler: async ({ uid, data, log }) => claimChest(env(log), uid, data.chest, data.key) }),
    def({ spec: CALLABLES.goalsClaimAchievement, auth: 'required', handler: async ({ uid, data, log }) => claimAchievement(env(log), uid, data.id, data.key) }),
    def({ spec: CALLABLES.dailyClaimLogin, auth: 'required', handler: async ({ uid, data, log }) => claimLogin(env(log), uid, data.key) }),
    def({ spec: CALLABLES.wheelSpin, auth: 'required', handler: async ({ uid, data, log }) => spinWheel(env(log), uid, data.key) }),
    def({ spec: CALLABLES.passJoin, auth: 'required', handler: async ({ uid, log }) => joinPass(env(log), uid) }),
    def({ spec: CALLABLES.passClaim, auth: 'required', handler: async ({ uid, data, log }) => claimPass(env(log), uid, data.tier, data.premium, data.key) }),
    // Every player signs in with a NEAR wallet, which is the "linked account" coupon types ask for.
    def({ spec: CALLABLES.couponsClaim, auth: 'required', handler: async ({ uid, data, log }) => claimCoupon(env(log), uid, data.taskId, true) }),
    def({ spec: CALLABLES.couponsRedeem, auth: 'admin', handler: async ({ data, log }) => redeemCoupon(env(log), data.code) }),
    def({ spec: CALLABLES.visitsGet, auth: 'required', handler: async ({ uid, data, log }) => getVisit(env(log), uid, data.uid) }),
    def({
      spec: CALLABLES.visitsLike,
      auth: 'required',
      handler: async ({ uid, data, log }) => {
        const result = await likeIsland(env(log), uid, data.uid);
        if (result.fresh) {
          try {
            await bookLikes(bookingDeps(rt, log), data.uid);
          } catch (e) {
            log.warn('likes push not booked', { error: String(e) });
          }
        }
        return result;
      },
    }),
    def({ spec: CALLABLES.inboxDelete, auth: 'required', handler: async ({ uid, data, log }) => deleteMessages(env(log), uid, data.ids) }),
    def({
      spec: CALLABLES.chainMint,
      auth: 'required',
      handler: async ({ uid, data, log }) => mint(mintDeps(rt, log), uid, await walletOf(rt, uid), data, log),
    }),
    def({
      spec: CALLABLES.chainRedeem,
      auth: 'required',
      handler: async ({ uid, data, log }) => redeem(mintDeps(rt, log), uid, await walletOf(rt, uid), data.txHash, log),
    }),
  ];
  return new Map(list.map((d) => [d.spec.name, d]));
}
