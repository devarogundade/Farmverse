import { Redis } from 'ioredis';
import type { NonceStore } from '../auth/sessions';
import type { MarketMessage } from '../chain/project';
import type { DocChange } from '../store/prismaStore';

export function redisClient(url: string, name: string): Redis {
  // BullMQ needs maxRetriesPerRequest: null on the connections it uses.
  return new Redis(url, { connectionName: `farmverse-${name}`, maxRetriesPerRequest: null, enableReadyCheck: true });
}

export function redisNonces(redis: Redis): NonceStore {
  return {
    async put(nonce, ttlSec) {
      await redis.set(`auth:nonce:${nonce}`, '1', 'EX', ttlSec, 'NX');
    },
    async take(nonce) {
      return (await redis.getdel(`auth:nonce:${nonce}`)) !== null;
    },
  };
}

export interface RateLimiter {
  /** Counts a hit; false once `limit` hits happened in the current `windowSec` window. */
  hit(key: string, limit: number, windowSec: number): Promise<boolean>;
}

export function redisRateLimiter(redis: Redis): RateLimiter {
  return {
    async hit(key, limit, windowSec) {
      const bucket = `rl:${key}:${Math.floor(Date.now() / 1000 / windowSec)}`;
      const [[, count]] = (await redis.multi().incr(bucket).expire(bucket, windowSec + 1).exec()) as [[unknown, number]];
      return count <= limit;
    },
  };
}

/** Document changes travel between server instances on one channel. */
export const CHANGES_CHANNEL = 'fv:changes';

export interface ChangeBus {
  publish(changes: readonly DocChange[]): void;
  subscribe(listener: (changes: readonly DocChange[]) => void): Promise<() => Promise<void>>;
}

export function redisChangeBus(publisher: Redis, url: string, onError: (error: unknown) => void): ChangeBus {
  return {
    publish(changes) {
      publisher.publish(CHANGES_CHANNEL, JSON.stringify(changes)).catch(onError);
    },
    async subscribe(listener) {
      const sub = redisClient(url, 'changes-sub');
      sub.on('message', (_channel, message) => {
        try {
          listener(JSON.parse(message) as DocChange[]);
        } catch (error) {
          onError(error);
        }
      });
      await sub.subscribe(CHANGES_CHANNEL);
      return async () => {
        await sub.quit();
      };
    },
  };
}

/** An in-process bus (tests, and single-process dev without Redis fan-out). */
export function localChangeBus(): ChangeBus {
  const listeners = new Set<(changes: readonly DocChange[]) => void>();
  return {
    publish(changes) {
      for (const l of listeners) l(changes);
    },
    async subscribe(listener) {
      listeners.add(listener);
      return async () => {
        listeners.delete(listener);
      };
    },
  };
}

/** Marketplace updates from the indexer to every server's market socket. */
export const MARKET_CHANNEL = 'fv:market';

export interface MarketBus {
  publish(messages: readonly MarketMessage[]): void;
  subscribe(listener: (messages: readonly MarketMessage[]) => void): Promise<() => Promise<void>>;
}

export function redisMarketBus(publisher: Redis, url: string, onError: (error: unknown) => void): MarketBus {
  return {
    publish(messages) {
      publisher.publish(MARKET_CHANNEL, JSON.stringify(messages)).catch(onError);
    },
    async subscribe(listener) {
      const sub = redisClient(url, 'market-sub');
      sub.on('message', (_channel, message) => {
        try {
          listener(JSON.parse(message) as MarketMessage[]);
        } catch (error) {
          onError(error);
        }
      });
      await sub.subscribe(MARKET_CHANNEL);
      return async () => {
        await sub.quit();
      };
    },
  };
}

export function localMarketBus(): MarketBus {
  const listeners = new Set<(messages: readonly MarketMessage[]) => void>();
  return {
    publish(messages) {
      for (const l of listeners) l(messages);
    },
    async subscribe(listener) {
      listeners.add(listener);
      return async () => {
        listeners.delete(listener);
      };
    },
  };
}
