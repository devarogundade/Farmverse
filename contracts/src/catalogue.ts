import { z } from 'zod';
import { named } from './registry';
import { EpochMs } from './common/primitives';

/**
 * The game's tuning data: prices, rewards and limits the server enforces and the client shows.
 *
 * One source: {@link DEFAULT_CATALOGUE} below. `npm run catalogue:publish` writes it to
 * `config/catalogue` (the live copy both sides read), and `contracts:gen` compiles the same data
 * into the client (`CatalogueDefaults.g.cs`) for the first launch and offline play. Change a price
 * here, bump `version`, publish: no app release needed.
 *
 * Grows module by module (money first); a section is added when its feature moves to the server.
 */

/** Gem sinks and sources are keyed by short ids: `coins_1000`, `boost_fert`, `deal_daily`. */
export const OfferId = z.string().regex(/^[a-z][a-z0-9_]{1,39}$/);

/** A boost a player can hold (Storage ▸ Boosts). */
export const BoostId = named('BoostId', z.enum(['shield', 'fert', 'rush', 'instant', 'basket', 'nightshift', 'lunch']), 'A boost item.');

/**
 * Where gems may come from, until each source's own module validates it on the server. Each
 * source is capped per UTC day (`earnCaps`), so a tampered client can't print gems.
 */
export const GemSource = named(
  'GemSource',
  z.enum(['inbox', 'coupon']),
  'Where earned gems come from.',
);

/** Something bought with gems: what it costs and what it gives. */
export const GemOffer = named(
  'GemOffer',
  z.object({
    gems: z.number().int().min(1).max(100_000),
    /** Coins added to the island. */
    coins: z.number().int().min(0).max(10_000_000),
    /** Boosts added to Storage (BoostId → count). */
    boosts: z.record(z.string(), z.number().int().min(1).max(100)),
    /** How many times a player may buy it per UTC day; 0 = no limit. */
    perDay: z.number().int().min(0).max(100),
    /** The corner ribbon in the shop ("2x Value"). */
    ribbon: z.string().max(20).optional(),
    /** The line under its name ("Fertilizer · crops grow 2×"). */
    subtitle: z.string().max(48).optional(),
  }),
  'A gem price and what it buys.',
);

/** A boost's effect: how long it runs once used (0 = it acts at once: an instant build, basket slots, a spin). */
export const BoostDef = named(
  'BoostDef',
  z.object({ minutes: z.number().int().min(0).max(10_080) }),
  'What a boost does when used.',
);

/** The daily chests (Orders ▸ six orders done, Tasks ▸ every daily task done, Share ▸ a post on X, no condition). */
export const ChestId = named('ChestId', z.enum(['order', 'task', 'share']), 'A daily chest.');

/** A post the daily X share opens (one picked at random); `{link}` becomes the player's invite link. */
const SharePost = z.string().min(20).max(240).regex(/\{link\}/);

/** What a chest, a gift or a pack holds. */
export const Bundle = named(
  'Bundle',
  z.object({
    coins: z.number().int().min(0).max(10_000_000),
    gems: z.number().int().min(0).max(100_000),
    boosts: z.record(z.string(), z.number().int().min(1).max(100)),
    /** A picture and name to show it by as one prize (a "Big chest"); otherwise its parts are shown. */
    art: z.string().max(64).optional(),
    label: z.string().max(24).optional(),
  }),
  'Coins, gems and boosts given together.',
);

export const FreeGift = named(
  'FreeGift',
  z.object({ coins: z.number().int().min(1).max(100_000), everyMinutes: z.number().int().min(1).max(10_080) }),
  'The free coins in the shop, every few hours.',
);

export const StarterPack = named(
  'StarterPack',
  z.object({ windowHours: z.number().int().min(0).max(720), contents: Bundle }),
  'The one-time pack a new player sees for a while.',
);

/**
 * A lifetime achievement: a stat of the island reaching `need` pays `gems` once. The stat is one the
 * island counts (harvested, sold, planted, orders, raids_won, building_upgrades, made:<good>…); the
 * server checks it on its copy of the island.
 */
export const Achievement = named(
  'Achievement',
  z.object({
    title: z.string().min(1).max(40),
    /** One of the game's icon names. */
    icon: z.string().max(64),
    stat: z.string().min(1).max(40),
    need: z.number().int().min(1).max(100_000_000),
    gems: z.number().int().min(1).max(10_000),
    /** The line under the title; the game words one from the stat when absent. */
    about: z.string().max(80).optional(),
    /** Order on the screen (low first). */
    order: z.number().int().min(0).max(10_000),
  }),
  'An achievement and its reward.',
);

/** One slice of the Lucky Wheel. */
export const WheelSegment = named(
  'WheelSegment',
  z.object({ prize: Bundle, weight: z.number().int().min(1).max(10_000) }),
  'A wheel slice: what it pays and how often it comes up.',
);

export const WheelDef = named(
  'WheelDef',
  z.object({
    /** Spins per UTC day (no extra spins beyond this). */
    spinsPerDay: z.number().int().min(0).max(50),
    /** Exactly eight slices, clockwise from the top (the wheel art has eight). */
    segments: z.array(WheelSegment).length(8),
  }),
  'The Lucky Wheel.',
);

/** One line of a list of benefits (an icon name and its words). */
export const Perk = named('Perk', z.object({ icon: z.string().max(64), text: z.string().min(1).max(60) }), 'A listed benefit.');

/** One Farm Pass tier: the free prize and the premium one (premium needs the pass bought this season). */
export const PassTier = named('PassTier', z.object({ free: Bundle, premium: Bundle }), 'A Farm Pass tier.');

/**
 * The Farm Pass. Seasons roll by themselves: season n runs from `epochMs + n × seasonDays` for
 * `seasonDays`. Pass XP is island XP earned since the player joined the season, divided by
 * `islandXpPerPassXp`; every `xpPerTier` pass XP opens the next tier.
 */
export const PassDef = named(
  'PassDef',
  z.object({
    epochMs: EpochMs,
    seasonDays: z.number().int().min(1).max(120),
    xpPerTier: z.number().int().min(1).max(10_000),
    islandXpPerPassXp: z.number().int().min(1).max(10_000),
    tiers: z.array(PassTier).min(1).max(100),
    /** What premium gives, as the pass screens list it (icon, line). */
    premiumPerks: z.array(Perk).min(1).max(6),
    /** The shop card's headline for the premium pass. */
    pitch: z.string().max(60),
  }),
  'The Farm Pass.',
);

/**
 * A coupon type (a partner's voucher: B3TR, USDT…). Codes come from the type's pool, loaded by the team
 * (no code pools: the server makes each code itself); everything the screen shows comes from here.
 */
export const CouponType = named(
  'CouponType',
  z.object({
    name: z.string().min(1).max(16),
    /** A pack picture used as the logo; else `logoGlyph` on `logoBg`. */
    logoArt: z.string().max(64),
    logoGlyph: z.string().max(4),
    logoBg: z.string().max(9),
    accentA: z.string().max(9),
    accentB: z.string().max(9),
    ink: z.string().max(9),
    /** The unit amounts are counted in ("B3TR", "USDT"). */
    unit: z.string().min(1).max(12),
    /** Codes start with this (`B3TR-7KQ2-M9XD`). */
    codePrefix: z.string().regex(/^[A-Z0-9]{2,8}$/),
    redeemUrl: z.string().url().max(300),
    redeemLabel: z.string().max(32),
    /** Redeem steps; <b>…</b> marks the words shown in gold. */
    steps: z.array(z.string().max(120)).max(8),
    expiryDays: z.number().int().min(1).max(365),
    /** Only players signed in with Google or Apple may receive it (anonymous players see it locked). */
    linkedOnly: z.boolean(),
  }),
  'A coupon type.',
);

/**
 * A task that pays a coupon. `progress` is `today:<stat>` (counted since the UTC day began), `<stat>`
 * (lifetime), `goals` (story goals done) or `level` (best island level).
 */
export const CouponTask = named(
  'CouponTask',
  z.object({
    name: z.string().min(1).max(40),
    art: z.string().max(64),
    typeId: z.string().max(24),
    daily: z.boolean(),
    progress: z.string().min(1).max(48),
    of: z.number().int().min(1).max(100_000_000),
    lockLevel: z.number().int().min(0).max(200),
    order: z.number().int().min(0).max(10_000),
    /** What the coupon is worth, in its type's unit (e.g. 10 = 10 B3TR). */
    amount: z.number().positive().max(1_000_000),
  }),
  'A task that pays a coupon.',
);

/** Weekly leaderboard prizes: ranks up to `rankTo` (after the previous line's) get this. */
export const RankPrize = named(
  'RankPrize',
  z.object({ rankTo: z.number().int().min(1).max(10_000), coins: z.number().int().min(0).max(10_000_000), gems: z.number().int().min(0).max(100_000) }),
  'A weekly prize band.',
);

/** A farm's title by its net worth (the highest `minWorth` reached wins). */
export const FarmTitle = named('FarmTitle', z.object({ minWorth: z.number().int().min(0), title: z.string().min(1).max(30) }), 'A farm title.');

/** Profile rules the screens show. */
export const ProfileRules = named(
  'ProfileRules',
  z.object({
    /** Island level that opens the last two avatars. */
    avatarUnlockLevel: z.number().int().min(1).max(200),
    /** Lowest first. */
    farmTitles: z.array(FarmTitle).min(1).max(20),
  }),
  'Profile rules.',
);

/** The friend referral: both players get `reward` when the new one reaches `level`. */
export const ReferralRules = named('ReferralRules', z.object({ level: z.number().int().min(1).max(100), reward: Bundle }), 'The referral reward.');

export const CatalogueDoc = named(
  'CatalogueDoc',
  z.object({
    /** Bumped on every change; the client keeps the newest it has seen. */
    version: z.number().int().min(1),
    publishedAt: EpochMs,
    /** Gems a new wallet starts with. */
    startingGems: z.number().int().min(0).max(10_000),
    /** OfferId → offer. */
    gemOffers: z.record(z.string(), GemOffer),
    /** GemSource → most gems from that source per UTC day. */
    earnCaps: z.record(z.string(), z.number().int().min(0).max(100_000)),
    /** BoostId → effect. */
    boosts: z.record(z.string(), BoostDef),
    /** ChestId → contents (each claimed once per UTC day). */
    chests: z.record(z.string(), Bundle),
    /** Post office orders to deliver in a UTC day to open the order chest (the task chest needs every daily goal). */
    orderChestOrders: z.number().int().min(1).max(100),
    /** Achievement id → achievement. */
    achievements: z.record(z.string(), Achievement),
    freeGift: FreeGift,
    /** The daily login ladder, day 1..7 (a missed day pauses it; after day 7 it starts again). */
    dailyLogin: z.array(Bundle).length(7),
    wheel: WheelDef,
    pass: PassDef,
    /** CouponType id → type. */
    couponTypes: z.record(z.string(), CouponType),
    /** CouponTask id → task. */
    couponTasks: z.record(z.string(), CouponTask),
    /** World board weekly prizes, best ranks first (empty = no prizes). */
    leaderboardPrizes: z.array(RankPrize).max(20),
    starterPack: StarterPack,
    referral: ReferralRules,
    profile: ProfileRules,
    /** The daily X share's preset posts (Shop ▸ Offers), short enough for a free X account with the link in. */
    sharePosts: z.array(SharePost).min(1).max(20),
  }),
  'The live tuning data (config/catalogue).',
);

export type BoostId = z.infer<typeof BoostId>;
export type GemSource = z.infer<typeof GemSource>;
export type GemOffer = z.infer<typeof GemOffer>;
export type CatalogueDoc = z.infer<typeof CatalogueDoc>;
export type ChestId = z.infer<typeof ChestId>;
export type Bundle = z.infer<typeof Bundle>;

const bundle = (coins: number, gems: number, boosts: Partial<Record<BoostId, number>> = {}): Bundle => ({ coins, gems, boosts: { ...boosts } as Record<string, number> });

const offer = (gems: number, give: { coins?: number; boosts?: Partial<Record<BoostId, number>>; perDay?: number }): GemOffer => ({
  gems,
  coins: give.coins ?? 0,
  boosts: { ...(give.boosts ?? {}) } as Record<string, number>,
  perDay: give.perDay ?? 0,
});

/** The catalogue as designed. Edit, bump `version`, then `npm run catalogue:publish` and `npm run gen`. */
export const DEFAULT_CATALOGUE: CatalogueDoc = {
  version: 15,
  publishedAt: 0,
  startingGems: 50,
  gemOffers: {
    // coin packs (Shop ▸ Coins)
    coins_1000: offer(10, { coins: 1_000 }),
    coins_3000: offer(28, { coins: 3_000 }),
    coins_8000: offer(70, { coins: 8_000 }),
    coins_20000: offer(160, { coins: 20_000 }),
    coins_60000: offer(450, { coins: 60_000 }),
    // the daily deal (Shop ▸ Offers): 3 fertilizer, once a day
    deal_daily: { ...offer(29, { boosts: { fert: 3 }, perDay: 1 }), ribbon: '2x Value', subtitle: 'Fertilizer · crops grow 2×' },
    // boosts (Shop ▸ Boosts)
    boost_shield: offer(40, { boosts: { shield: 1 } }),
    boost_fert: offer(15, { boosts: { fert: 1 } }),
    boost_rush: offer(15, { boosts: { rush: 1 } }),
    boost_instant: offer(10, { boosts: { instant: 1 } }),
    boost_basket: offer(120, { boosts: { basket: 1 } }),
    // workers run the farm: one absence at the normal food price (24 h), and a full meal for every workers' house
    boost_nightshift: offer(15, { boosts: { nightshift: 1 } }),
    boost_lunch: offer(10, { boosts: { lunch: 1 } }),
    // costumes (Customize ▸ Costume): whole outfits from the packs, owned for good once bought (the island save keeps them)
    look_santa: { ...offer(250, {}), subtitle: 'Christmas pack · Santa' },
    look_builder: offer(120, {}),
    look_clerk: offer(120, {}),
    look_footballer: offer(150, {}),
    look_policeman: offer(150, {}),
    look_mime: offer(120, {}),
    look_tourist: offer(120, {}),
    look_wanderer: offer(90, {}),
    look_drifter: offer(90, {}),
    look_rambler: offer(90, {}),
    look_roamer: offer(90, {}),
  },
  earnCaps: {
    inbox: 100,
    coupon: 100,
  },
  boosts: {
    shield: { minutes: 480 },
    fert: { minutes: 30 },
    rush: { minutes: 30 },
    instant: { minutes: 0 },
    basket: { minutes: 0 },
    nightshift: { minutes: 0 },
    lunch: { minutes: 0 },
  },
  chests: {
    order: bundle(500, 5, { fert: 1 }),
    task: bundle(300, 3, { rush: 1 }),
    share: bundle(100, 5),
  },
  orderChestOrders: 6,
  achievements: {
    farmer1: { title: 'Master farmer I', icon: 'Icon_Laurel', stat: 'harvested', need: 1_000, gems: 10, order: 10 },
    farmer2: { title: 'Master farmer II', icon: 'Icon_Laurel', stat: 'harvested', need: 5_000, gems: 25, order: 11 },
    trader1: { title: 'Trader I', icon: 'Icon_Golds', stat: 'sold', need: 100, gems: 10, order: 20 },
    trader2: { title: 'Trader II', icon: 'Icon_Golds', stat: 'sold', need: 1_000, gems: 25, order: 21 },
    planter: { title: 'Green thumb', icon: 'Icon_Shovel', stat: 'planted', need: 200, gems: 10, order: 30 },
    postman: { title: 'Postman', icon: 'Icon_Mail', stat: 'orders', need: 25, gems: 15, about: 'Deliver 25 post office orders.', order: 40 },
    survivor: { title: 'Survivor', icon: 'Icon_Shield', stat: 'raids_won', need: 10, gems: 15, about: 'Beat 10 raids.', order: 50 },
    builder: { title: 'Master builder', icon: 'Icon_Hammer', stat: 'building_upgrades', need: 20, gems: 20, about: 'Upgrade buildings 20 times.', order: 60 },
  },
  freeGift: { coins: 50, everyMinutes: 240 },
  dailyLogin: [
    bundle(500, 0),
    bundle(0, 0, { fert: 1 }),
    bundle(0, 10),
    bundle(0, 0, { shield: 1 }),
    bundle(1_000, 0),
    bundle(0, 20),
    { ...bundle(1_000, 50, { fert: 2, shield: 1 }), art: 'ShopItem_SpecialChest_Purple', label: 'Big chest' },
  ],
  leaderboardPrizes: [
    { rankTo: 1, coins: 20_000, gems: 100 },
    { rankTo: 3, coins: 10_000, gems: 50 },
    { rankTo: 10, coins: 5_000, gems: 25 },
    { rankTo: 50, coins: 2_000, gems: 10 },
  ],
  couponTypes: {
    b3tr: {
      name: 'B3TR', logoArt: 'VB_mark', logoGlyph: '', logoBg: '#ffffff', accentA: '#9be15d', accentB: '#2fbf71', ink: '#0b3a1a',
      unit: 'B3TR', codePrefix: 'B3TR', redeemUrl: 'https://farmverse.world/redeem', redeemLabel: 'Open redeem page', expiryDays: 30, linkedOnly: true,
      steps: ['Tap <b>Open redeem page</b>.', 'Sign in with your <b>VeWorld</b> wallet.', 'Paste your code and tap <b>Redeem</b>.', 'B3TR arrives in your wallet.'],
    },
    usdt: {
      name: 'USDT', logoArt: '', logoGlyph: 'T', logoBg: '#26a17b', accentA: '#7fe3c4', accentB: '#26a17b', ink: '#073b2c',
      unit: 'USDT', codePrefix: 'USDT', redeemUrl: 'https://farmverse.world/redeem', redeemLabel: 'Open redeem page', expiryDays: 14, linkedOnly: true,
      steps: ['Tap <b>Open redeem page</b>.', 'Enter your wallet address.', 'Paste your code and confirm.'],
    },
  },
  couponTasks: {
    d1: { name: 'Harvest 20 crops', art: 'Icon_Pumkin', typeId: 'b3tr', daily: true, progress: 'today:harvested', of: 20, lockLevel: 0, order: 1, amount: 10 },
    d2: { name: 'Fill 3 orders', art: 'Icon_ImageIcon_Map_l', typeId: 'b3tr', daily: true, progress: 'today:orders', of: 3, lockLevel: 4, order: 2, amount: 10 },
    d3: { name: 'Chop 5 trees', art: 'G_Tree', typeId: 'b3tr', daily: true, progress: 'today:chopped', of: 5, lockLevel: 0, order: 3, amount: 10 },
    d4: { name: 'Plant 10 crops', art: 'G_Wheat', typeId: 'b3tr', daily: true, progress: 'today:planted', of: 10, lockLevel: 0, order: 4, amount: 10 },
    d5: { name: 'Sell 30 goods', art: 'Icon_Golds', typeId: 'b3tr', daily: true, progress: 'today:sold', of: 30, lockLevel: 0, order: 5, amount: 10 },
    g1: { name: 'Finish 3 goals', art: 'G_Water', typeId: 'b3tr', daily: false, progress: 'goals', of: 3, lockLevel: 0, order: 11, amount: 10 },
    g2: { name: 'Island level 10', art: 'Icon_Star', typeId: 'usdt', daily: false, progress: 'level', of: 10, lockLevel: 0, order: 12, amount: 1 },
    g3: { name: 'Grow 500 crops', art: 'Icon_Shovel', typeId: 'b3tr', daily: false, progress: 'harvested', of: 500, lockLevel: 0, order: 13, amount: 10 },
    g4: { name: 'Upgrade 10 buildings', art: 'Icon_GearWheels', typeId: 'b3tr', daily: false, progress: 'building_upgrades', of: 10, lockLevel: 10, order: 14, amount: 10 },
    g5: { name: 'Fill 50 orders', art: 'Icon_Chest', typeId: 'usdt', daily: false, progress: 'orders', of: 50, lockLevel: 12, order: 15, amount: 1 },
  },
  pass: {
    epochMs: Date.UTC(2026, 10, 1),
    seasonDays: 30,
    xpPerTier: 10,
    islandXpPerPassXp: 20,
    tiers: [
      { free: bundle(300, 0), premium: bundle(0, 10) },
      { free: bundle(0, 0, { fert: 1 }), premium: bundle(1_000, 0) },
      { free: bundle(500, 0), premium: bundle(0, 0, { shield: 1 }) },
      { free: bundle(0, 0, { fert: 1 }), premium: bundle(2_000, 0) },
      { free: bundle(0, 5), premium: bundle(0, 0, { instant: 2 }) },
      { free: bundle(0, 0, { rush: 1 }), premium: bundle(0, 20) },
      { free: bundle(800, 0), premium: bundle(0, 0, { fert: 3 }) },
      { free: bundle(0, 0, { fert: 2 }), premium: bundle(3_000, 0) },
      { free: bundle(0, 10), premium: bundle(0, 0, { shield: 2 }) },
      { free: bundle(1_200, 0), premium: bundle(0, 30) },
      { free: bundle(0, 0, { rush: 2 }), premium: bundle(5_000, 0) },
      { free: bundle(0, 15), premium: { ...bundle(2_000, 50, { instant: 3 }), art: 'ShopItem_SpecialChest_Purple', label: 'Season chest' } },
    ],
    premiumPerks: [
      { icon: 'Icon_Chest', text: 'A premium reward on every tier' },
      { icon: 'ResourceBar_Icon_Gem', text: '110 gems across the season' },
      { icon: 'Icon_Golds', text: '13,000 coins across the season' },
      { icon: 'Icon_Booster', text: 'Boosts and the Season chest' },
    ],
    pitch: 'Premium rewards on every tier!',
  },
  wheel: {
    spinsPerDay: 5,
    segments: [
      { prize: bundle(100, 0), weight: 22 },
      { prize: bundle(0, 3), weight: 10 },
      { prize: bundle(0, 0, { fert: 1 }), weight: 12 },
      { prize: bundle(500, 0), weight: 10 },
      { prize: bundle(300, 0), weight: 14 },
      { prize: bundle(0, 0, { shield: 1 }), weight: 8 },
      { prize: bundle(200, 0), weight: 20 },
      { prize: bundle(0, 25), weight: 4 },
    ],
  },
  starterPack: { windowHours: 48, contents: bundle(5_000, 300, { shield: 1 }) },
  referral: { level: 5, reward: bundle(600, 5) },
  profile: {
    avatarUnlockLevel: 10,
    farmTitles: [
      { minWorth: 0, title: 'Settler' },
      { minWorth: 1_000, title: 'Homesteader' },
      { minWorth: 10_000, title: 'Landholder' },
      { minWorth: 100_000, title: 'Manor Lord' },
      { minWorth: 1_000_000, title: 'Golden Barn' },
    ],
  },
  // X counts any link as 23 characters and an emoji as 2; each post stays well under a free account's 280
  sharePosts: [
    'Just harvested my first crops in Farmverse 🌾 A cozy farm game on @NEARProtocol where what you grow is yours. Farm with me: {link} #NEARHackathon #Web3Gaming',
    "Hooked on @farmverse_world 🚜 Plant, harvest and trade on @NEARProtocol, right in the browser. Join my farm: {link} #NEARHackathon",
    'My Farmverse island keeps growing 🏝️ Free to play, no download, built on @NEARProtocol. Come play: {link} #NEARHackathon #Web3Gaming',
    'Farming on-chain and loving it 🌱 In @farmverse_world my crops and buildings can become NFTs on @NEARProtocol. Try it: {link} #NEARHackathon',
    'Daily check-in on my Farmverse farm ✅ Chill game, real economy on @NEARProtocol. Start your own farm: {link} #NEARHackathon #GameFi',
    'Looking for farm neighbors 🐄 @farmverse_world is a free browser farm game built for the #NEARHackathon. Join me here: {link} #NEAR',
    "Can't stop playing Farmverse 📦 Crops, orders and trades, all on @NEARProtocol. Your farm is waiting: {link} #NEARHackathon #Web3Gaming",
    'Cozy game + real ownership = Farmverse 💚 Grow, build and trade $FARM on @NEARProtocol. Play free: {link} #NEARHackathon',
    'Spinning the Lucky Wheel on my Farmverse island 🎡 Fun farm game on @NEARProtocol, sign in with email. Come play: {link} #NEARHackathon',
    'Rooting for @farmverse_world in the #NEARHackathon 🏆 A browser farm game where your farm is really yours. Play with me: {link} #NEAR #Web3Gaming',
  ],
};

/** The wallet doc's parse, for both sides. */
export const WalletDoc = named(
  'WalletDoc',
  z.object({
    gems: z.number().int().min(0).max(100_000_000),
    /** The UTC day `bought` counts (YYYY-MM-DD). */
    boughtOn: z.string().max(10).optional(),
    /** OfferId → bought that day (daily-limited offers show "Bought"). */
    bought: z.record(z.string(), z.number().int().min(0)).optional(),
    /** The highest island level whose gems were paid (economy plan: 2 a level, 5 at a chapter's end, 3 past the last). */
    levelPaid: z.number().int().min(0).optional(),
    updatedAt: EpochMs,
  }),
  "`players/{uid}/state/wallet`: the player's gems (server-written).",
);
export type WalletDoc = z.infer<typeof WalletDoc>;

/** `players/{uid}/state/purchases`: lasting unlocks (server-written; granted by game/entitlements.ts). */
export const PurchasesDoc = named(
  'PurchasesDoc',
  z.object({
    /** Extra builders granted for good (capped by the server). */
    extraBuilders: z.number().int().min(0).max(10),
    /** Farm Passes used while premium was already on: each turns premium on for a later season. */
    passCredits: z.number().int().min(0).max(100).optional(),
    updatedAt: EpochMs,
  }),
  'Lasting unlocks.',
);
export type PurchasesDoc = z.infer<typeof PurchasesDoc>;

/** `players/{uid}/state/inventory`: boosts held and running (server-written). */
export const InventoryDoc = named(
  'InventoryDoc',
  z.object({
    /** BoostId → how many are held. */
    boosts: z.record(z.string(), z.number().int().min(0).max(100_000)),
    /** BoostId → when its effect ends (epoch ms); absent or past = not running. */
    activeUntil: z.record(z.string(), EpochMs),
    updatedAt: EpochMs,
  }),
  "The player's boosts.",
);
export type InventoryDoc = z.infer<typeof InventoryDoc>;

/** `players/{uid}/state/shop`: the shop's timers and daily claims (server-written). */
export const ShopDoc = named(
  'ShopDoc',
  z.object({
    /** When the free gift is ready again (epoch ms; 0 = now). */
    nextGiftAt: EpochMs,
    /** When the starter pack stops being offered (epoch ms). */
    starterEndsAt: EpochMs,
    /** ChestId → the UTC day (YYYY-MM-DD) it was last claimed. */
    chestDay: z.record(z.string(), z.string().max(10)),
    updatedAt: EpochMs,
  }),
  "The shop's timers for this player.",
);
export type ShopDoc = z.infer<typeof ShopDoc>;

/** `players/{uid}/state/goals`: achievements collected (server-written). */
export const GoalsDoc = named(
  'GoalsDoc',
  z.object({
    /** Achievement id → when it was collected (epoch ms). */
    achievements: z.record(z.string(), EpochMs),
    updatedAt: EpochMs,
  }),
  'Which achievements the player has collected.',
);
export type GoalsDoc = z.infer<typeof GoalsDoc>;
export type Achievement = z.infer<typeof Achievement>;

/** `players/{uid}/state/daily`: the login ladder and today's wheel spins (server-written). */
export const DailyDoc = named(
  'DailyDoc',
  z.object({
    /** The ladder day the next login claim pays (1..7). */
    loginDay: z.number().int().min(1).max(7),
    /** The UTC day (YYYY-MM-DD) of the last login claim. */
    loginOn: z.string().max(10),
    /** The UTC day the spins below count for. */
    wheelOn: z.string().max(10),
    /** Free spins used on wheelOn. */
    wheelUsed: z.number().int().min(0).max(1000),
    updatedAt: EpochMs,
  }),
  'Daily login and wheel progress.',
);
export type DailyDoc = z.infer<typeof DailyDoc>;

/** `players/{uid}/state/pass`: this season's Farm Pass for the player (server-written). */
export const PassDoc = named(
  'PassDoc',
  z.object({
    /** The season this doc is for (`s<n>`); an older one means a new season has started. */
    seasonId: z.string().max(16),
    /** Island XP when the player joined the season (pass XP counts from here). */
    baseXp: z.number().int().min(0),
    /** Tiers collected (1-based). */
    free: z.array(z.number().int().min(1).max(100)).max(100),
    premium: z.array(z.number().int().min(1).max(100)).max(100),
    /** Bought the premium pass this season. */
    premiumActive: z.boolean(),
    updatedAt: EpochMs,
  }),
  "The player's Farm Pass this season.",
);
export type PassDoc = z.infer<typeof PassDoc>;

/** The season a time falls in. */
export function passSeason(pass: z.infer<typeof PassDef>, nowMs: number): { id: string; index: number; startsAt: number; endsAt: number } {
  const len = pass.seasonDays * 86_400_000;
  const index = Math.max(0, Math.floor((nowMs - pass.epochMs) / len));
  const startsAt = pass.epochMs + index * len;
  return { id: `s${index}`, index, startsAt, endsAt: startsAt + len };
}

/** `players/{uid}/coupons/{taskId}_{day|once}`: a coupon code the server issued to the player. */
export const CouponDoc = named(
  'CouponDoc',
  z.object({
    code: z.string().min(4).max(40),
    typeId: z.string().max(24),
    taskId: z.string().max(24),
    /** The task's label when issued (e.g. "Daily · Harvest 20 crops"). */
    task: z.string().max(60),
    /** The UTC day (YYYY-MM-DD) it was earned for daily tasks; '' for goals. */
    day: z.string().max(10),
    issuedAt: EpochMs,
    expiresAt: EpochMs,
    /** What it is worth, in its type's unit. */
    amount: z.number().positive(),
  }),
  'A coupon code issued to the player.',
);
export type CouponDoc = z.infer<typeof CouponDoc>;
export type CouponTask = z.infer<typeof CouponTask>;

/**
 * `leaderboards/{weekId}/players/{uid}`: a player's row on the week's board, written by the server
 * whenever it runs their island (net worth and level as the engine computed them).
 */
export const LeaderboardEntryDoc = named(
  'LeaderboardEntryDoc',
  z.object({
    name: z.string().max(16),
    avatarId: z.string().max(48),
    /** The player's language (the board's flag and its Country tab). */
    locale: z.string().max(16),
    worth: z.number().int().min(0),
    level: z.number().int().min(1),
    updatedAt: EpochMs,
  }),
  "A player's row on the weekly leaderboard.",
);
export type LeaderboardEntryDoc = z.infer<typeof LeaderboardEntryDoc>;

/** Weeks run Monday 00:00 UTC to Monday; `w<n>` counts from 2026-01-05 (a Monday). */
export const WEEK_EPOCH_MS = Date.UTC(2026, 0, 5);
export function weekOf(nowMs: number): { id: string; index: number; startsAt: number; endsAt: number } {
  const len = 7 * 86_400_000;
  const index = Math.floor((nowMs - WEEK_EPOCH_MS) / len);
  const startsAt = WEEK_EPOCH_MS + index * len;
  return { id: `w${index}`, index, startsAt, endsAt: startsAt + len };
}
