/**
 * Runtime configuration from Vite env (see .env.example). With no VITE_API_URL the whole app runs on
 * the in-memory mock (api/mockApi, chain via state/store), so it works before the backend is deployed.
 */
const env = import.meta.env;

export type Network = 'testnet' | 'mainnet';

export const config = {
  network: (env.VITE_NEAR_NETWORK === 'mainnet' ? 'mainnet' : 'testnet') as Network,
  apiUrl: (env.VITE_API_URL as string | undefined)?.replace(/\/$/, '') || null,
  wsUrl: (env.VITE_WS_URL as string | undefined) || null,
  rpcUrl:
    (env.VITE_NEAR_RPC as string | undefined) ||
    (env.VITE_NEAR_NETWORK === 'mainnet' ? 'https://rpc.mainnet.near.org' : 'https://rpc.testnet.near.org'),
  contracts: {
    items: (env.VITE_ITEMS_CONTRACT as string | undefined) || 'items.farmverse.testnet',
    market: (env.VITE_MARKET_CONTRACT as string | undefined) || 'market.farmverse.testnet',
    farm: (env.VITE_FARM_CONTRACT as string | undefined) || 'farm.farmverse.testnet',
  },
  /** Items per page for listings. */
  pageSize: 8,
};

/** 'api' talks to the backend + NEAR; 'mock' runs entirely in the browser. */
export const DATA_MODE: 'api' | 'mock' = config.apiUrl ? 'api' : 'mock';
