/**
 * The deployed surface. Export groups become function name prefixes:
 * `player.create` deploys as `player-create`, matching contracts/src/callables.ts.
 * Nothing else is exported — every callable here is in the contract's table, plus
 * the island's wake task and the push trigger.
 */
import * as player from './api/player';
import * as push from './api/push';
import * as account from './api/account';
import * as friends from './api/friends';
import * as contentApi from './api/content';
import * as island from './api/islands';
import * as referralApi from './api/referrals';
import * as wallet from './api/wallet';
import * as shopApi from './api/shop';
import * as goalsApi from './api/goals';
import * as dailyApi from './api/daily';
import * as pass from './api/pass';
import * as coupons from './api/coupons';
import * as leaderboardApi from './api/leaderboard';
import * as visits from './api/visits';
import * as inboxApi from './api/inbox';
import * as ads from './api/ads';
import * as purchasesApi from './api/purchases';
import * as sessionApi from './api/session';

const accountFns = { delete: account.remove, export: account.exportData };
const content = { manifest: contentApi.manifest };
const referral = { claim: referralApi.claim };
const rewards = { claim: referralApi.claimRewardFn };
const boost = { use: shopApi.use };
const shop = { claimGift: shopApi.claimGiftFn, claimChest: shopApi.claimChestFn };
const goals = { claimAchievement: goalsApi.claimAchievementFn };
const daily = { claimLogin: dailyApi.claimLoginFn };
const leaderboard = { payWeek: leaderboardApi.payWeekJob };
const inbox = { delete: inboxApi.remove };
const wheel = { spin: dailyApi.spin };
const iap = { verify: purchasesApi.verify };
const session = { ping: sessionApi.ping };
const raid = { finish: sessionApi.finish };
const pushFns = { ...push, task: sessionApi.task, onNewsCreated: sessionApi.onNewsCreated };

export { player, pushFns as push, session, raid, accountFns as account, friends, content, island, referral, rewards, wallet, boost, shop, goals, daily, wheel, pass, coupons, leaderboard, visits, inbox, ads, iap };
