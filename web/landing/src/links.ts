/** Where the buttons go. Override per deploy with VITE_PLAY_URL / VITE_MARKET_URL. */
export const PLAY_URL: string = import.meta.env.VITE_PLAY_URL ?? 'https://play.farmverse.world';
export const MARKET_URL: string =
  import.meta.env.VITE_MARKET_URL ?? (import.meta.env.DEV ? 'http://localhost:5174' : 'https://marketplace.farmverse.world');
export const marketType = (type: string) => `${MARKET_URL}/#/?type=${type}`;
/**
 * The game server itself (the redeem page's API). The deploy scripts set VITE_GAME_API_URL to it; their
 * VITE_API_URL is the marketplace's /v1/market, so it isn't used here.
 */
export const API_URL: string = (
  import.meta.env.VITE_GAME_API_URL ?? (import.meta.env.DEV ? 'http://localhost:8787' : 'https://api.farmverse.world')
).replace(/\/$/, '');
