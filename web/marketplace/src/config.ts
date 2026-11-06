/**
 * Runtime configuration from Vite env (see .env.example). The marketplace always talks to the game
 * API (backend/server: the read API and live socket the indexer feeds) and to NEAR.
 */
const env = import.meta.env;

export type Network = 'testnet' | 'mainnet';

const str = (v: unknown) => (typeof v === 'string' && v.trim() ? v.trim() : undefined);

/** The game API's marketplace routes. Local dev falls back to the server's default port. */
const apiUrl = (str(env.VITE_API_URL) ?? (env.DEV ? 'http://localhost:8787/v1/market' : '')).replace(/\/$/, '');

export const config = {
  network: (env.VITE_NEAR_NETWORK === 'mainnet' ? 'mainnet' : 'testnet') as Network,
  apiUrl: apiUrl || null,
  /** The game server itself (sign-in, crediting redeemed items); by default the API URL's origin part. */
  gameApiUrl: str(env.VITE_GAME_API_URL) ?? (apiUrl ? apiUrl.replace(/\/v1\/market$/, '') : null),
  /** Live market updates; by default the API's /live socket. */
  wsUrl: str(env.VITE_WS_URL) ?? (apiUrl ? `${apiUrl.replace(/^http/, 'ws')}/live` : null),
  rpcUrl: str(env.VITE_NEAR_RPC) ?? (env.VITE_NEAR_NETWORK === 'mainnet' ? 'https://rpc.mainnet.near.org' : 'https://rpc.testnet.near.org'),
  contracts: {
    items: str(env.VITE_ITEMS_CONTRACT) ?? 'items.farmverse.testnet',
    market: str(env.VITE_MARKET_CONTRACT) ?? 'market.farmverse.testnet',
    farm: str(env.VITE_FARM_CONTRACT) ?? 'farm.farmverse.testnet',
  },
  /** The game (deep links: "Use in game" → `?open=nfts`, after a redeem → `?redeemed=<token>`). */
  playUrl: (str(env.VITE_PLAY_URL) ?? (env.DEV ? 'http://localhost:8000' : 'https://play.farmverse.world')).replace(/\/$/, ''),
  /** A token to show under "Featured" in the menu; the item is hidden without one. */
  featuredToken: str(env.VITE_FEATURED_TOKEN) ?? null,
  /** Items per page for listings. */
  pageSize: 8,
};

/** Where the game opens for a token: its NFT tab, or the island after a redeem. */
export const gameLink = {
  nfts: () => `${config.playUrl}/?open=nfts`,
  redeemed: (tokenId: string) => `${config.playUrl}/?redeemed=${encodeURIComponent(tokenId)}`,
};
