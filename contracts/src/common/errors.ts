import { z } from 'zod';
import { named } from '../registry';

/**
 * Every error the game can report, with the parameters each one carries.
 *
 * The server never sends a sentence: it sends a code and string parameters, and
 * the client renders the localized message `error.<CODE>`. Adding a code here
 * adds it to the C# enum on the next `contracts:gen`.
 *
 * The parameter list is the contract for localization: a translator can rely on
 * exactly these placeholders being present.
 */
export const ERROR_CATALOGUE = {
  // ---- transport / platform -------------------------------------------------
  UNAUTHENTICATED: [],
  PERMISSION_DENIED: [],
  INVALID_ARGUMENT: [],
  NOT_FOUND: [],
  RATE_LIMITED: [],
  CLIENT_OUTDATED: ['minVersion'],
  MAINTENANCE: [],
  INTERNAL: [],
  DATA_CORRUPT: [],
  CONFLICT: [],
  // ---- client only (never produced by the server) ----------------------------
  NETWORK_REQUIRED: [],
  TIMEOUT: [],
  CANCELLED: [],
  /** The Google or Apple account already belongs to another player. */
  ACCOUNT_IN_USE: [],
  /** The feature needs a device capability this platform does not have (linking in the editor). */
  UNSUPPORTED: [],
  // ---- player ----------------------------------------------------------------
  PLAYER_EXISTS: [],
  PLAYER_NOT_FOUND: [],
  NAME_TAKEN: [],
  // ---- friends ---------------------------------------------------------------
  INVITE_INVALID: [],
  ALREADY_FRIENDS: [],
  // ---- wallet ----------------------------------------------------------------
  NOT_ENOUGH_GEMS: ['need'],
  OFFER_UNKNOWN: [],
  OFFER_SOLD_OUT: [],
  // ---- shop ------------------------------------------------------------------
  BOOST_NONE: [],
  GIFT_NOT_READY: ['readyAt'],
  CHEST_CLAIMED: [],
  /** Today's orders or daily goals aren't done on the server's copy of the island. */
  CHEST_LOCKED: ['have', 'need'],
  // ---- goals -----------------------------------------------------------------
  ACHIEVEMENT_LOCKED: ['have', 'need'],
  ACHIEVEMENT_CLAIMED: [],
  // ---- daily ------------------------------------------------------------------
  DAILY_CLAIMED: [],
  WHEEL_NO_SPINS: [],
  // ---- pass -------------------------------------------------------------------
  PASS_LOCKED: ['have', 'need'],
  PASS_CLAIMED: [],
  PASS_PREMIUM_REQUIRED: [],
  // ---- coupons ----------------------------------------------------------------
  COUPON_LOCKED: ['have', 'need'],
  /** The coupon type needs a Google or Apple sign-in. */
  COUPON_LINK_REQUIRED: [],
  /** No such coupon code (redeeming). */
  COUPON_UNKNOWN: [],
  /** The code was already redeemed, or has expired. */
  COUPON_USED: [],
  // ---- visits -----------------------------------------------------------------
  /** The owner doesn't take visitors (settings) or has no island on the server yet. */
  VISITS_CLOSED: [],
  // ---- chain (mint / redeem) ----------------------------------------------------
  /** Minting is locked for this account (an implausible island upload is under review). */
  MINT_LOCKED: [],
  /** Today's mint value limit is reached (coins of market value). */
  MINT_LIMIT: ['limit', 'left'],
  /** The account is too new to mint (hours since it started). */
  MINT_TOO_SOON: ['hours'],
  /** Not enough of the goods in store (or boosts held). */
  MINT_NOT_ENOUGH: ['have'],
  /** More than the server saw the island make (or gave it): `earned` of the good may be minted now. */
  MINT_UNEARNED: ['earned'],
  /** The building can't be picked up: `reason` is not_empty, busy, core_building, under_construction, ruined. */
  MINT_BUILDING_BUSY: ['reason'],
  /** That item can't be minted. */
  MINT_NOT_ALLOWED: [],
  /** The transaction isn't a successful redeem by this wallet (yet, or at all). */
  REDEEM_NOT_FOUND: [],
  /** Goods mint in lots of at least `min` units (golden goods: 1). */
  MINT_LOT_TOO_SMALL: ['min'],
  /** The island hasn't the coins for the mint fee. */
  MINT_NO_COINS: ['fee'],
  /** A land deed can't be used: every wild plot on the island is open already. */
  DEED_ALL_OPEN: [],
  // ---- faucet (testnet) ---------------------------------------------------------
  /** Today's test FARM was claimed; `nextAt` is when the next claim opens (epoch ms). */
  FAUCET_WAIT: ['nextAt'],
  /** The faucet is empty or switched off. */
  FAUCET_CLOSED: [],
} as const satisfies Record<string, readonly string[]>;

export type ErrorCodeName = keyof typeof ERROR_CATALOGUE;

const ERROR_CODES = Object.keys(ERROR_CATALOGUE) as [ErrorCodeName, ...ErrorCodeName[]];

/** Codes the server must never send: the client produces them for its own failures. */
export const CLIENT_ONLY_ERRORS: ReadonlySet<ErrorCodeName> = new Set<ErrorCodeName>([
  'NETWORK_REQUIRED',
  'TIMEOUT',
  'CANCELLED',
  'ACCOUNT_IN_USE',
  'UNSUPPORTED',
]);

export const ErrorCode = named('ErrorCode', z.enum(ERROR_CODES), 'Every error the game can report.');

export const ValidationIssue = named(
  'ValidationIssue',
  z.object({
    path: z.string().max(256),
    message: z.string().max(512),
  }),
  'One schema violation. Sent only by non-production backends.',
);

export const ErrorBody = named(
  'ErrorBody',
  z.object({
    code: ErrorCode,
    /** Localization arguments. Always strings, so they format the same everywhere. */
    params: z.record(z.string(), z.string()),
    /** Present for RATE_LIMITED and MAINTENANCE. */
    retryAfterMs: z.number().int().min(0).max(86_400_000).optional(),
    /** Present for INVALID_ARGUMENT in non-production environments. */
    issues: z.array(ValidationIssue).max(20).optional(),
  }),
  'A refusal: a code and the parameters its message needs.',
);

export type ErrorBody = z.infer<typeof ErrorBody>;
