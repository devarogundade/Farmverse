import { z } from 'zod';
import { named } from './registry';
import { Empty, RequestMeta } from './common/envelope';
import { CatalogueId, InstallationId, NotificationId, Uid } from './common/ids';
import { DisplayName, EpochMs, Stock } from './common/primitives';
import { BoostId, ChestId, GemSource, OfferId } from './catalogue';

/**
 * Callables return small acknowledgements, not documents: the client already
 * listens to its own documents, and the listener delivers the authoritative
 * state. That keeps one path for state into the client.
 */

export const PlayerCreateRequest = named(
  'PlayerCreateRequest',
  z.object({
    meta: RequestMeta,
    displayName: DisplayName,
    avatarId: CatalogueId,
  }),
  'Create the signed-in player and their starting farm.',
);
export const PlayerCreateResponse = named('PlayerCreateResponse', z.object({ uid: Uid }));

export const PlayerUpdateProfileRequest = named(
  'PlayerUpdateProfileRequest',
  z.object({
    meta: RequestMeta,
    displayName: DisplayName.optional(),
    avatarId: CatalogueId.optional(),
    frameId: CatalogueId.optional(),
    allowVisitors: z.boolean().optional(),
    pushEnabled: z.boolean().optional(),
    timezone: z.string().max(64).optional(),
    /** The game's language (ISO 639-1, e.g. "en"): leaderboard flag and Country tab, push copy. */
    locale: z.string().regex(/^[a-z]{2}(?:[-_][A-Za-z]{2})?$/).optional(),
  }),
  'Change profile fields and settings. Omitted fields are unchanged.',
);

export const PlayerSuggestNamesRequest = named(
  'PlayerSuggestNamesRequest',
  z.object({ meta: RequestMeta, count: z.number().int().min(1).max(10) }),
  'Farm name ideas for the S07 dice: free names only, never an AI call.',
);
export const PlayerSuggestNamesResponse = named(
  'PlayerSuggestNamesResponse',
  z.object({ names: z.array(DisplayName).max(10) }),
);

// ---- friends (R44) --------------------------------------------------------------------

export const FriendsCreateInviteRequest = named('FriendsCreateInviteRequest', z.object({ meta: RequestMeta }));
export const FriendsCreateInviteResponse = named(
  'FriendsCreateInviteResponse',
  z.object({ code: z.string().regex(/^[A-Z0-9]{8}$/), link: z.string().max(200), usesLeft: z.number().int().min(0).max(100), expiresAt: EpochMs }),
  'An invite code and the link to share.',
);
export const FriendsAcceptRequest = named('FriendsAcceptRequest', z.object({ meta: RequestMeta, code: z.string().regex(/^[A-Z0-9]{8}$/) }));
export const FriendsAcceptResponse = named(
  'FriendsAcceptResponse',
  z.object({ friendUid: Uid, displayName: z.string().max(16), avatarId: CatalogueId, shortId: z.string().max(10) }),
  'The player who invited you, now a friend.',
);

/**
 * The island: the server keeps every island and runs it forward with the game's own rules (the C#
 * island engine on Cloud Run). The island travels as the engine's JSON (`IslandState`), one string,
 * so the client loads it with the same code it saves with.
 */
export const IslandSyncRequest = named(
  'IslandSyncRequest',
  z.object({
    meta: RequestMeta,
    /**
     * The device's own island (IslandState JSON), sent once to move a local island to the server.
     * Ignored when the server already has an island for this player.
     */
    adopt: z.string().max(400_000).optional(),
    /**
     * Shadow mode (rollout phase 1, while the device is still authoritative): `adopt` replaces the
     * server's island every sync, keeping the device's own clock, so the server's run can be compared
     * with the game's. Off once the server owns the island.
     */
    mirror: z.boolean().optional(),
    /**
     * The device's own save files (opaque JSON), kept beside a mirrored island so the farm comes back
     * when the player signs in with the same Google or Apple account on another phone.
     */
    device: z.string().max(400_000).optional(),
  }),
  'Bring the island up to now and return it, with what happened while the player was away.',
);

export const IslandGathered = named(
  'IslandGathered',
  z.object({ item: z.string().min(1).max(64), count: Stock }),
);

export const IslandAwayReport = named(
  'IslandAwayReport',
  z.object({
    awaySeconds: z.number().min(0),
    workers: z.number().int().min(0),
    loads: z.number().int().min(0),
    seedCoins: z.number().int().min(0),
    storageFull: z.boolean(),
    gathered: z.array(IslandGathered).max(64),
    /** Workers run the farm: what each job did, food used, and why work stopped ('' | 'food' | 'storage'). */
    harvested: z.number().int().min(0).optional(),
    replanted: z.number().int().min(0).optional(),
    fed: z.number().int().min(0).optional(),
    collected: z.number().int().min(0).optional(),
    refilled: z.number().int().min(0).optional(),
    restocked: z.number().int().min(0).optional(),
    sold: z.number().int().min(0).optional(),
    soldCoins: z.number().int().min(0).optional(),
    crates: z.number().int().min(0).optional(),
    foodUsed: z.number().min(0).optional(),
    stopped: z.string().max(16).optional(),
    stoppedAt: z.number().min(0).optional(),
    capped: z.boolean().optional(),
  }),
  'What the island\'s workers did while nobody was watching.',
);

export type IslandAwayReport = z.infer<typeof IslandAwayReport>;

export const ReferralOutcome = named('ReferralOutcome', z.enum(['none', 'not_referred', 'already', 'level']), 'Why a referral claim gave nothing (none = it did).');

export const ReferralClaimRequest = named(
  'ReferralClaimRequest',
  z.object({ meta: RequestMeta }),
  "The invited player's island reached the referral level: reward both players (checked on the server's island copy).",
);

export const ReferralClaimResponse = named(
  'ReferralClaimResponse',
  z.object({
    rewarded: z.boolean(),
    /** Why not, when not rewarded. */
    reason: ReferralOutcome,
  }),
);

export const RewardsClaimRequest = named(
  'RewardsClaimRequest',
  z.object({ meta: RequestMeta, id: z.string().regex(/^r_[A-Za-z0-9]{8,24}$/) }),
  'Collect a reward once: the answer says what to add to the island.',
);

export const RewardsClaimResponse = named(
  'RewardsClaimResponse',
  z.object({
    coins: z.number().int().min(0),
    gems: z.number().int().min(0),
    /** False when it had already been collected (nothing to add). */
    fresh: z.boolean(),
  }),
);

// ---- wallet (gems, server-owned) -------------------------------------------------------

/** A client-made id for one purchase or one earning: retrying with the same key never charges or pays twice. */
export const OpKey = z.string().regex(/^[A-Za-z0-9_-]{8,64}$/);

export const WalletSpendRequest = named(
  'WalletSpendRequest',
  z.object({ meta: RequestMeta, offerId: OfferId, key: OpKey }),
  'Buy a catalogue offer with gems.',
);
export const WalletSpendResponse = named(
  'WalletSpendResponse',
  z.object({
    /** The balance after the purchase. */
    gems: z.number().int().min(0),
    /** What to add: coins to the island, boosts to Storage. */
    coins: z.number().int().min(0),
    boosts: z.record(z.string(), z.number().int().min(0)),
    /** False when this key was already used (the first answer is repeated; add nothing again). */
    fresh: z.boolean(),
  }),
);

export const WalletEarnRequest = named(
  'WalletEarnRequest',
  z.object({ meta: RequestMeta, source: GemSource, amount: z.number().int().min(1).max(1_000), key: OpKey }),
  'Gems earned in the game, within the source\'s daily cap.',
);
export const WalletEarnResponse = named(
  'WalletEarnResponse',
  z.object({
    gems: z.number().int().min(0),
    /** What was actually added (less than asked once the day's cap is reached). */
    granted: z.number().int().min(0),
    fresh: z.boolean(),
  }),
);

// ---- shop: boosts, free gift, daily chests --------------------------------------------

export const BoostUseRequest = named('BoostUseRequest', z.object({ meta: RequestMeta, boostId: BoostId, key: OpKey }), 'Use one boost from Storage.');
export const BoostUseResponse = named(
  'BoostUseResponse',
  z.object({
    /** How many are left. */
    left: z.number().int().min(0),
    /** When its effect ends (epoch ms); 0 for a boost that acts at once. */
    activeUntil: EpochMs,
    fresh: z.boolean(),
  }),
);

export const ShopClaimGiftRequest = named('ShopClaimGiftRequest', z.object({ meta: RequestMeta, key: OpKey }), 'Collect the free coin gift.');
export const ShopClaimGiftResponse = named(
  'ShopClaimGiftResponse',
  z.object({ coins: z.number().int().min(0), nextGiftAt: EpochMs, fresh: z.boolean() }),
);

export const ShopClaimChestRequest = named(
  'ShopClaimChestRequest',
  z.object({ meta: RequestMeta, chest: ChestId, key: OpKey }),
  "Open today's chest: its gems and boosts go to the server copies, its coins to the island.",
);
export const ShopClaimChestResponse = named(
  'ShopClaimChestResponse',
  z.object({
    coins: z.number().int().min(0),
    gems: z.number().int().min(0),
    boosts: z.record(z.string(), z.number().int().min(0)),
    fresh: z.boolean(),
  }),
);

// ---- goals: achievements ---------------------------------------------------------------

export const GoalsClaimAchievementRequest = named(
  'GoalsClaimAchievementRequest',
  z.object({ meta: RequestMeta, id: z.string().regex(/^[a-z][a-z0-9_]{1,39}$/), key: OpKey }),
  "Collect an achievement's gems: the server checks the stat on its copy of the island.",
);
export const GoalsClaimAchievementResponse = named(
  'GoalsClaimAchievementResponse',
  z.object({
    /** The wallet after the reward. */
    gems: z.number().int().min(0),
    granted: z.number().int().min(0),
    fresh: z.boolean(),
  }),
);

// ---- daily login and the Lucky Wheel ------------------------------------------------------

/** What a prize paid: gems and boosts are already on the server; coins are for the island. */
const PrizePaid = {
  coins: z.number().int().min(0),
  gems: z.number().int().min(0),
  boosts: z.record(z.string(), z.number().int().min(0)),
  fresh: z.boolean(),
};

export const DailyClaimLoginRequest = named('DailyClaimLoginRequest', z.object({ meta: RequestMeta, key: OpKey }), "Collect today's login reward.");
export const DailyClaimLoginResponse = named(
  'DailyClaimLoginResponse',
  z.object({ ...PrizePaid, day: z.number().int().min(1).max(7) }),
);

export const WheelSpinRequest = named(
  'WheelSpinRequest',
  z.object({ meta: RequestMeta, key: OpKey }),
  'Spin the Lucky Wheel after watching a rewarded ad: `key` is the ad\'s key (its server-side verification), one spin per ad, spinsPerDay a day.',
);
export const WheelSpinResponse = named(
  'WheelSpinResponse',
  z.object({ ...PrizePaid, segment: z.number().int().min(0).max(7), spinsLeft: z.number().int().min(0) }),
);

// ---- Farm Pass ------------------------------------------------------------------------------

export const PassJoinRequest = named('PassJoinRequest', z.object({ meta: RequestMeta }), "Join this season's Farm Pass (pass XP counts from now).");
export const PassJoinResponse = named('PassJoinResponse', z.object({ seasonId: z.string().max(16), endsAt: EpochMs, baseXp: z.number().int().min(0) }));

export const PassClaimRequest = named(
  'PassClaimRequest',
  z.object({ meta: RequestMeta, tier: z.number().int().min(1).max(100), premium: z.boolean(), key: OpKey }),
  'Collect a Farm Pass tier: the server checks the pass XP on its copy of the island.',
);
export const PassClaimResponse = named('PassClaimResponse', z.object({ ...PrizePaid }));

// ---- coupons --------------------------------------------------------------------------------

export const CouponsClaimRequest = named(
  'CouponsClaimRequest',
  z.object({ meta: RequestMeta, taskId: z.string().regex(/^[a-z0-9_]{1,24}$/) }),
  "A coupon for a finished task: the server checks the task on its copy of the island and hands out a code from the type's pool.",
);
export const CouponsClaimResponse = named(
  'CouponsClaimResponse',
  z.object({
    code: z.string().max(40),
    typeId: z.string().max(24),
    expiresAt: EpochMs,
    /** What the code is worth, in `unit`. */
    amount: z.number().positive(),
    unit: z.string().max(12),
    /** False when this task had already paid (the same code again). */
    fresh: z.boolean(),
  }),
);

export const CouponsRedeemRequest = named(
  'CouponsRedeemRequest',
  z.object({ meta: RequestMeta, code: z.string().min(4).max(40) }),
  'Redeem page (admin accounts only): check a code and mark it used, once.',
);
export const CouponsRedeemResponse = named(
  'CouponsRedeemResponse',
  z.object({ typeId: z.string().max(24), amount: z.number().positive(), unit: z.string().max(12), redeemedAt: EpochMs }),
);

// ---- visits ---------------------------------------------------------------------------------

/** A building on a visited island: catalogue id and its cells. */
export const VisitBuilding = named(
  'VisitBuilding',
  z.object({
    id: z.string().max(48),
    x: z.number().int().min(-1000).max(1000),
    z: z.number().int().min(-1000).max(1000),
    w: z.number().int().min(1).max(64),
    d: z.number().int().min(1).max(64),
  }),
  'A building seen on a visit.',
);

export const VisitsGetRequest = named('VisitsGetRequest', z.object({ meta: RequestMeta, uid: Uid }), "Another player's island as the server last ran it, to look at.");
export const VisitsGetResponse = named(
  'VisitsGetResponse',
  z.object({
    name: z.string().max(16),
    avatarId: z.string().max(48),
    locale: z.string().max(16),
    level: z.number().int().min(1),
    worth: z.number().int().min(0),
    likes: z.number().int().min(0),
    /** True when the visitor already liked this island today. */
    likedToday: z.boolean(),
    crops: z.number().int().min(0),
    raids: z.number().int().min(0),
    buildings: z.array(VisitBuilding).max(600),
  }),
);

export const VisitsLikeRequest = named('VisitsLikeRequest', z.object({ meta: RequestMeta, uid: Uid }), 'Like an island (once a day per visitor).');
export const VisitsLikeResponse = named('VisitsLikeResponse', z.object({ likes: z.number().int().min(0), fresh: z.boolean() }));

// ---- inbox ----------------------------------------------------------------------------------

export const InboxDeleteRequest = named(
  'InboxDeleteRequest',
  z.object({ meta: RequestMeta, ids: z.array(NotificationId).min(1).max(50) }),
  'Delete inbox messages (read ones whose reward, if any, was collected).',
);
export const InboxDeleteResponse = named('InboxDeleteResponse', z.object({ deleted: z.number().int().min(0) }));

// ---- rewarded ads ---------------------------------------------------------------------------

export const AdsClaimRequest = named(
  'AdsClaimRequest',
  z.object({ meta: RequestMeta, placement: z.string().regex(/^[a-z][a-z0-9_]{1,39}$/), key: OpKey }),
  "A watched rewarded ad's prize. Live backends pay only after AdMob's server-side verification for this key arrived.",
);
export const AdsClaimResponse = named('AdsClaimResponse', z.object({ ...PrizePaid }));

/** One kind of good and how many (a raid's haul). */
export const ItemCount = named('ItemCount', z.object({ item: z.string().min(1).max(32), n: z.number().int().min(0) }), 'A good and how many.');

export const RaidBy = named(
  'RaidBy',
  z.enum(['none', 'client', 'server', 'shield', 'skipped']),
  "Who settled the day's raid: the game while the player was there, the server while they were away, a Raid Shield, or the island was too small.",
);

/** What a raid the server ran took (applied to the game's own island on its next session). */
export const RaidResult = named(
  'RaidResult',
  z.object({
    size: z.number().int().min(0).max(64),
    won: z.boolean(),
    stolen: z.array(ItemCount).max(64),
    coins: z.number().int().min(0),
    /** Fields trampled (building uids). */
    trampled: z.array(z.number().int()).max(64),
    /** Buildings ruined (building uids). */
    ruined: z.array(z.number().int()).max(8),
    atMs: EpochMs,
  }),
  'The losses of a raid the server ran while the player was away.',
);

/** The day's raid as the server plans it (one a day; the game runs it while the player is there). */
export const RaidPlan = named(
  'RaidPlan',
  z.object({
    /** The player's local day (YYYY-MM-DD) this raid belongs to. */
    day: z.string().max(10),
    /** When it comes if the player isn't there (epoch ms); the game may run it any time before. */
    dueMs: EpochMs,
    done: z.boolean(),
    by: RaidBy,
    /** A raid the server ran and the game hasn't shown yet. */
    result: RaidResult.nullable(),
  }),
  "Today's raid.",
);

export const SessionPingRequest = named(
  'SessionPingRequest',
  z.object({
    meta: RequestMeta,
    /** The device's offset from UTC in minutes right now (local day, quiet hours, best push hour). */
    offsetMin: z.number().int().min(-840).max(840),
    /** The game has applied and shown `raid.result`: forget it. */
    raidAck: z.boolean().optional(),
  }),
  'The game is open (sent at start and every minute while it is in front): no pushes while playing; returns the day\'s raid.',
);
export const SessionPingResponse = named(
  'SessionPingResponse',
  z.object({
    raid: RaidPlan,
    /** Gems just paid for island levels reached since the last ping (0 when none). */
    levelGems: z.number().int().min(0).optional(),
    /** The island level they were paid up to. */
    levelPaid: z.number().int().min(0).optional(),
  }),
);

export const RaidOutcomeKind = named('RaidOutcomeKind', z.enum(['won', 'lost', 'shield', 'skipped']), 'How a raid the game ran ended (or why it was skipped).');

export const RaidFinishRequest = named(
  'RaidFinishRequest',
  z.object({
    meta: RequestMeta,
    /** The plan's day this raid settled. */
    day: z.string().max(10),
    outcome: RaidOutcomeKind,
    size: z.number().int().min(0).max(64),
  }),
  "The game ran (or skipped) today's raid while the player was there.",
);
export const RaidFinishResponse = named('RaidFinishResponse', z.object({ raid: RaidPlan }));

export const PlayerClaimLinkRewardRequest = named(
  'PlayerClaimLinkRewardRequest',
  z.object({ meta: RequestMeta }),
  "Collect the catalogue's thank-you for linking Google or Apple (once per account).",
);
export const PlayerClaimLinkRewardResponse = named('PlayerClaimLinkRewardResponse', z.object({ ...PrizePaid }));

export const IapStore = named('IapStore', z.enum(['google', 'apple', 'fake']), 'Where a purchase was made (fake: the editor and test builds).');

export const IapVerifyRequest = named(
  'IapVerifyRequest',
  z.object({
    meta: RequestMeta,
    store: IapStore,
    /** The store product id (a catalogue `iap` key). */
    productId: z.string().min(1).max(64),
    /** The store's transaction id (Google: orderId; Apple: transactionId). */
    transactionId: z.string().min(1).max(256),
    /** Google: the purchase token; Apple: the signed transaction (JWS); fake: anything. */
    receipt: z.string().min(1).max(20_000),
  }),
  'Check a store purchase and pay it (once per transaction; a repeat returns the first answer).',
);
export const IapVerifyResponse = named(
  'IapVerifyResponse',
  z.object({
    ...PrizePaid,
    /** True when the store says the purchase is still pending (paid later, nothing given yet). */
    pending: z.boolean(),
  }),
);

export const IslandSyncResponse = named(
  'IslandSyncResponse',
  z.object({
    /** The island as of now (IslandState JSON). */
    state: z.string(),
    /** The rules version the server ran. */
    rulesVersion: z.number().int().min(1),
    report: IslandAwayReport,
    /** When the server will next look at this island by itself. */
    nextWakeMs: EpochMs,
  }),
);

export const PushRegisterTokenRequest = named(
  'PushRegisterTokenRequest',
  z.object({
    meta: RequestMeta,
    installationId: InstallationId,
    token: z.string().min(16).max(4096),
  }),
);

export const AccountDeleteRequest = named(
  'AccountDeleteRequest',
  z.object({ meta: RequestMeta, confirm: z.literal('DELETE') }),
  'Permanently delete the account and its data.',
);
export const AccountDeleteResponse = named('AccountDeleteResponse', z.object({ deletedAt: EpochMs }));

export const AccountExportRequest = named('AccountExportRequest', z.object({ meta: RequestMeta }), 'A copy of everything stored about the player (data portability).');
export const AccountExportResponse = named(
  'AccountExportResponse',
  z.object({
    /** Pretty-printed JSON of the player's documents; the client offers it as a file to save or share. */
    json: z.string().max(4_000_000),
    exportedAt: EpochMs,
    /** True when long histories were cut to their most recent rows. */
    truncated: z.boolean(),
  }),
);

// ---- art and audio delivery (world plan) ---------------------------------------------

export const ContentBundle = named(
  'ContentBundle',
  z.object({
    name: z.string().regex(/^[a-z0-9][a-z0-9_.-]{0,95}$/),
    url: z.string().url().max(512),
    bytes: z.number().int().min(1),
    /** Hex SHA-256 of the bundle file, checked after download. */
    sha256: z.string().regex(/^[0-9a-f]{64}$/),
    /** Catalogue ids whose art the bundle carries (empty for shared bundles such as music). */
    catalogueIds: z.array(CatalogueId).max(500),
  }),
  'One downloadable art or audio bundle.',
);

export const PlatformContent = named(
  'PlatformContent',
  z.object({
    contentVersion: z.number().int().min(1),
    /** The Addressables catalogue the client loads first. */
    catalogueUrl: z.string().url().max(512),
    bundles: z.array(ContentBundle).max(500),
  }),
  'Everything one platform downloads after install.',
);

/** `content/current`: what each platform downloads, written by the content publish tool. */
export const ContentManifestDoc = named(
  'ContentManifestDoc',
  z.object({
    android: PlatformContent.optional(),
    ios: PlatformContent.optional(),
    /** The oldest app version that can read this content. */
    minAppVersion: z.string().max(32),
    publishedAt: EpochMs,
  }),
  'The published art and audio for every platform.',
);

export const ContentManifestRequest = named('ContentManifestRequest', z.object({ meta: RequestMeta }), "This client's art and audio bundles.");
export const ContentManifestResponse = named(
  'ContentManifestResponse',
  z.object({
    /** Absent when nothing is published for this platform (the editor, or before the first publish). */
    content: PlatformContent.optional(),
    takenAt: EpochMs,
  }),
);

export interface CallableSpec<Req extends z.ZodType = z.ZodType, Res extends z.ZodType = z.ZodType> {
  readonly name: string;
  readonly request: Req;
  readonly response: Res;
}

function callable<Req extends z.ZodType, Res extends z.ZodType>(
  name: string,
  request: Req,
  response: Res,
): CallableSpec<Req, Res> {
  if (!/^[a-z]+-[a-z][A-Za-z]*$/.test(name)) {
    throw new Error(`Callable name '${name}' must look like 'area-verb'.`);
  }
  return { name, request, response };
}

/** Every callable function. The backend exports exactly these; the client calls exactly these. */
export const CALLABLES = {
  playerCreate: callable('player-create', PlayerCreateRequest, PlayerCreateResponse),
  playerUpdateProfile: callable('player-updateProfile', PlayerUpdateProfileRequest, Empty),
  playerSuggestNames: callable('player-suggestNames', PlayerSuggestNamesRequest, PlayerSuggestNamesResponse),
  islandSync: callable('island-sync', IslandSyncRequest, IslandSyncResponse),
  pushRegisterToken: callable('push-registerToken', PushRegisterTokenRequest, Empty),
  accountDelete: callable('account-delete', AccountDeleteRequest, AccountDeleteResponse),
  accountExport: callable('account-export', AccountExportRequest, AccountExportResponse),
  friendsCreateInvite: callable('friends-createInvite', FriendsCreateInviteRequest, FriendsCreateInviteResponse),
  friendsAccept: callable('friends-accept', FriendsAcceptRequest, FriendsAcceptResponse),
  contentManifest: callable('content-manifest', ContentManifestRequest, ContentManifestResponse),
  referralClaim: callable('referral-claim', ReferralClaimRequest, ReferralClaimResponse),
  rewardsClaim: callable('rewards-claim', RewardsClaimRequest, RewardsClaimResponse),
  walletSpend: callable('wallet-spend', WalletSpendRequest, WalletSpendResponse),
  walletEarn: callable('wallet-earn', WalletEarnRequest, WalletEarnResponse),
  boostUse: callable('boost-use', BoostUseRequest, BoostUseResponse),
  shopClaimGift: callable('shop-claimGift', ShopClaimGiftRequest, ShopClaimGiftResponse),
  shopClaimChest: callable('shop-claimChest', ShopClaimChestRequest, ShopClaimChestResponse),
  dailyClaimLogin: callable('daily-claimLogin', DailyClaimLoginRequest, DailyClaimLoginResponse),
  wheelSpin: callable('wheel-spin', WheelSpinRequest, WheelSpinResponse),
  passJoin: callable('pass-join', PassJoinRequest, PassJoinResponse),
  passClaim: callable('pass-claim', PassClaimRequest, PassClaimResponse),
  couponsClaim: callable('coupons-claim', CouponsClaimRequest, CouponsClaimResponse),
  couponsRedeem: callable('coupons-redeem', CouponsRedeemRequest, CouponsRedeemResponse),
  visitsGet: callable('visits-get', VisitsGetRequest, VisitsGetResponse),
  visitsLike: callable('visits-like', VisitsLikeRequest, VisitsLikeResponse),
  inboxDelete: callable('inbox-delete', InboxDeleteRequest, InboxDeleteResponse),
  adsClaim: callable('ads-claim', AdsClaimRequest, AdsClaimResponse),
  playerClaimLinkReward: callable('player-claimLinkReward', PlayerClaimLinkRewardRequest, PlayerClaimLinkRewardResponse),
  iapVerify: callable('iap-verify', IapVerifyRequest, IapVerifyResponse),
  goalsClaimAchievement: callable('goals-claimAchievement', GoalsClaimAchievementRequest, GoalsClaimAchievementResponse),
  sessionPing: callable('session-ping', SessionPingRequest, SessionPingResponse),
  raidFinish: callable('raid-finish', RaidFinishRequest, RaidFinishResponse),
} as const;

export type PlatformContent = z.infer<typeof PlatformContent>;
export type ContentManifestDoc = z.infer<typeof ContentManifestDoc>;
export type ContentBundle = z.infer<typeof ContentBundle>;
export type RaidPlan = z.infer<typeof RaidPlan>;
export type RaidResult = z.infer<typeof RaidResult>;
