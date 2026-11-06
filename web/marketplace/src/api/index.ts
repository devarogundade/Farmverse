import { DATA_MODE } from '../config';
import { httpApi } from './httpApi';
import { mockApi } from './mockApi';
import type { MarketApi } from './types';

/** The backend when VITE_API_URL is set, otherwise the in-browser mock. */
export const api: MarketApi = DATA_MODE === 'api' ? httpApi : mockApi;
export type * from './types';
