import { httpApi } from './httpApi';
import type { MarketApi } from './types';

/** The marketplace read API on the game server (backend/server/src/market). */
export const api: MarketApi = httpApi;
export type * from './types';
