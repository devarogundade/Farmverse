/**
 * Every document path in the game (04 §1). No string path is written anywhere
 * else, so a rename or a restructure is one edit and a typo is a type error.
 */
export const paths = {
  player: (uid: string) => `players/${uid}`,
  settings: (uid: string) => `players/${uid}/private/settings`,
  /** The island save the engine runs (IslandDoc), server-owned. */
  island: (uid: string) => `players/${uid}/state/island`,
  /** The player's gems (WalletDoc): server-written, client-read. */
  wallet: (uid: string) => `players/${uid}/state/wallet`,
  /** Today's gem earnings per source and purchases per offer (server only, for the daily limits). */
  walletDay: (uid: string) => `players/${uid}/state/walletDay`,
  /** Boosts held and running (InventoryDoc): server-written, client-read. */
  inventory: (uid: string) => `players/${uid}/state/inventory`,
  /** The shop's timers and daily chest claims (ShopDoc): server-written, client-read. */
  shop: (uid: string) => `players/${uid}/state/shop`,
  /** Achievements collected (GoalsDoc): server-written, client-read. */
  goals: (uid: string) => `players/${uid}/state/goals`,
  /** The login ladder and today's wheel spins (DailyDoc): server-written, client-read. */
  daily: (uid: string) => `players/${uid}/state/daily`,
  /** This season's Farm Pass (PassDoc): server-written, client-read. */
  pass: (uid: string) => `players/${uid}/state/pass`,
  /** A coupon code issued to the player (CouponDoc), keyed `{taskId}_{day|once}`: client-read. */
  coupon: (uid: string, id: string) => `players/${uid}/coupons/${id}`,
  coupons: (uid: string) => `players/${uid}/coupons`,
  /** Every coupon code the server made (server only): CouponDoc + { uid, unit, redeemedAt }. */
  couponCode: (code: string) => `couponCodes/${code}`,
  /** A player's row on a week's board (LeaderboardEntryDoc): server-written, readable by players. */
  boardEntry: (weekId: string, uid: string) => `leaderboards/${weekId}/players/${uid}`,
  board: (weekId: string) => `leaderboards/${weekId}/players`,
  /** That a week's prizes were paid (server only). */
  boardPaid: (weekId: string) => `leaderboards/${weekId}`,
  /** An island's like count (server only): { likes }. */
  visits: (ownerUid: string) => `visits/${ownerUid}`,
  /** The day a visitor last liked the island (server only). */
  visitLike: (ownerUid: string, visitorUid: string) => `visits/${ownerUid}/likes/${visitorUid}`,
  /** One gem movement, keyed by the client's operation key (server only; makes retries safe). */
  ledger: (uid: string, id: string) => `players/${uid}/ledger/${id}`,
  /** Lasting unlocks (PurchasesDoc, game/entitlements.ts): server-written, client-read. */
  purchases: (uid: string) => `players/${uid}/state/purchases`,
  /** When the game was last open, its UTC offset and the hours the player usually plays (server only). */
  presence: (uid: string) => `players/${uid}/state/presence`,
  /** Today's direct pushes: how many, and when each kind last went (server only). */
  pushLog: (uid: string) => `players/${uid}/state/pushLog`,
  /** The day's raid (RaidDoc, server only; the game gets it from session-ping). */
  raid: (uid: string) => `players/${uid}/state/raid`,
  /** The live tuning data (CatalogueDoc), written by tools/publishCatalogue.ts. */
  catalogue: () => 'config/catalogue',
  friend: (uid: string, friendUid: string) => `players/${uid}/friends/${friendUid}`,
  friends: (uid: string) => `players/${uid}/friends`,
  invite: (code: string) => `invites/${code}`,
  /** Who invited this player (server only). */
  referral: (inviteeUid: string) => `referrals/${inviteeUid}`,
  reward: (uid: string, id: string) => `players/${uid}/rewards/${id}`,
  rewards: (uid: string) => `players/${uid}/rewards`,
  newsItem: (id: string) => `news/${id}`,
  notification: (uid: string, id: string) => `players/${uid}/notifications/${id}`,
  pushToken: (uid: string, installationId: string) => `players/${uid}/pushTokens/${installationId}`,
  username: (lowerName: string) => `usernames/${lowerName}`,
  pushTokens: (uid: string) => `players/${uid}/pushTokens`,
  notifications: (uid: string) => `players/${uid}/notifications`,
  /** Where the app finds downloadable content (model bundles) for its platform. */
  contentManifest: () => 'content/current',
  /** The counter that hands out short player numbers (`shortId`). */
  playerNumbers: () => 'counters/playerNumbers',
} as const;
