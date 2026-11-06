/** Runtime configuration from Vite env (see .env.example). */
const env = import.meta.env;

const str = (v: unknown) => (typeof v === 'string' && v.trim() ? v.trim() : undefined);

export const config = {
  /** The game server (backend/server); its admin API is under /v1/admin. */
  apiUrl: (str(env.VITE_API_URL) ?? (env.DEV ? 'http://localhost:8787' : '')).replace(/\/$/, ''),
  network: (env.VITE_NEAR_NETWORK === 'mainnet' ? 'mainnet' : 'testnet') as 'testnet' | 'mainnet',
};
