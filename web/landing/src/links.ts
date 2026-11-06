/** Where the buttons go. Override per deploy with VITE_PLAY_URL / VITE_MARKET_URL. */
export const PLAY_URL: string = import.meta.env.VITE_PLAY_URL ?? 'https://play.farmverse.world';
export const MARKET_URL: string =
  import.meta.env.VITE_MARKET_URL ?? (import.meta.env.DEV ? 'http://localhost:5174' : 'https://marketplace.farmverse.world');
export const marketType = (type: string) => `${MARKET_URL}/#/?type=${type}`;
