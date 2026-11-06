import { Redis } from 'ioredis';
import type { NonceStore } from '../auth/sessions';
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
