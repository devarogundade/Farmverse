/**
 * Social login (plan 15 §13.2): "Email or Google", a Privy NEAR wallet next to the usual ones. It signs in the page
 * itself (privyNear.ts), so this stays light: the main bundle only needs to know whether it's on.
 */

/** The wallet's id in NEAR Connect (kept: players' saved sessions point at it). Same as privyNear's PRIVY_NEAR_ID. */
export const PRIVY_WALLET_ID = 'farmverse-email';

/** "Your farm": the game's guest farm, signed for by the game site's key bridge (guestBridge.ts). */
export const GUEST_WALLET_ID = 'farmverse-guest';

/** Our own wallets, which sign in this page: the market's calls go through the game server's relay (no gas). */
export const IN_PAGE_WALLETS: ReadonlySet<string> = new Set([PRIVY_WALLET_ID, GUEST_WALLET_ID]);

/** Social login is on when the Privy app is set. */
export const socialLogin = !!import.meta.env.VITE_PRIVY_APP_ID;
