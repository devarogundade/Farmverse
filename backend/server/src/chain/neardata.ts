import type { NearBlock } from './parse';

/**
 * Where finalized blocks come from. neardata.xyz (FastNEAR) serves every block with its shards'
 * receipt outcomes and logs as plain JSON, for testnet and mainnet. Goldsky's NEAR datasets cover
 * mainnet only, so they can't feed the testnet build; this source can be swapped for another that
 * yields the same blocks.
 */
export interface BlockSource {
  /** Height of the latest final block. */
  finalHeight(): Promise<number>;
  /** The block at `height`, or null if no block was produced there (heights can be skipped). */
  block(height: number): Promise<NearBlock | null>;
}

export const NEARDATA_URL = { testnet: 'https://testnet.neardata.xyz', mainnet: 'https://mainnet.neardata.xyz' } as const;

/** neardata's free tier: 180 requests a minute per IP. Stay a little under it. */
export const FREE_TIER_RPM = 170;

export interface NeardataOptions {
  /** A FastNEAR subscription key (raises the rate limit). */
  readonly apiKey?: string;
  /** Requests per minute this process may make. */
  readonly requestsPerMinute?: number;
  readonly fetch?: typeof fetch;
  readonly attempts?: number;
  readonly timeoutMs?: number;
  readonly sleep?: (ms: number) => Promise<void>;
  readonly now?: () => number;
  /** Called on every 429, so a too-high NEARDATA_RPM shows up in the logs. */
  readonly onRateLimited?: (path: string, waitMs: number) => void;
}

const realSleep = (ms: number) => new Promise<void>((r) => setTimeout(r, ms));

/** Spaces request starts evenly so a burst of parallel fetches stays under the rate limit. */
export function pacer(perMinute: number, now: () => number, sleep: (ms: number) => Promise<void>) {
  const gap = 60_000 / perMinute;
  let next = 0;
  return async () => {
    const at = Math.max(now(), next);
    next = at + gap;
    const wait = at - now();
    if (wait > 0) await sleep(wait);
  };
}

export function neardataSource(baseUrl: string, opts: NeardataOptions = {}): BlockSource {
  const doFetch = opts.fetch ?? fetch;
  const attempts = opts.attempts ?? 8;
  const timeoutMs = opts.timeoutMs ?? 20_000;
  const sleep = opts.sleep ?? realSleep;
  const base = baseUrl.replace(/\/$/, '');
  const turn = pacer(opts.requestsPerMinute ?? FREE_TIER_RPM, opts.now ?? Date.now, sleep);
  const key = opts.apiKey ? `?apiKey=${encodeURIComponent(opts.apiKey)}` : '';

  async function get<T>(path: string): Promise<T> {
    let last: unknown;
    for (let i = 0; i < attempts; i++) {
      await turn();
      let retryAfterMs = Math.min(8_000, 500 * 2 ** i);
      try {
        const res = await doFetch(`${base}${path}${key}`, { signal: AbortSignal.timeout(timeoutMs), redirect: 'follow' });
        if (res.ok) return (await res.json()) as T;
        last = new Error(`neardata ${path}: HTTP ${res.status}`);
        if (res.status === 429) {
          // Rate limited: wait as long as asked (or a good while), without giving up quickly.
          const asked = Number(res.headers.get('retry-after'));
          retryAfterMs = Number.isFinite(asked) && asked > 0 ? asked * 1000 : 15_000;
          opts.onRateLimited?.(path, retryAfterMs);
        } else if (res.status < 500) {
          break;
        }
      } catch (error) {
        last = error;
      }
      await sleep(retryAfterMs);
    }
    throw last instanceof Error ? last : new Error(String(last));
  }

  return {
    async finalHeight() {
      const b = await get<NearBlock>('/v0/last_block/final');
      return b.block.header.height;
    },
    block: (height) => get<NearBlock | null>(`/v0/block/${height}`),
  };
}
