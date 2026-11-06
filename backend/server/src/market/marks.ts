import type { Redis } from 'ioredis';
import type { NoteMarks } from './routes';

const key = (account: string) => `market:notes-read:${account}`;
const TTL_SEC = 180 * 24 * 3600;

/** Notification read marks in Redis (kept 180 days after the last read). */
export function redisNoteMarks(redis: Redis): NoteMarks {
  return {
    async get(account) {
      return Number((await redis.get(key(account))) ?? 0);
    },
    async set(account, atMs) {
      await redis.set(key(account), String(atMs), 'EX', TTL_SEC);
    },
  };
}

export function memoryNoteMarks(): NoteMarks {
  const marks = new Map<string, number>();
  return { get: async (a) => marks.get(a) ?? 0, set: async (a, at) => void marks.set(a, at) };
}
