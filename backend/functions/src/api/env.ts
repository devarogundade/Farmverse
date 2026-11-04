import { systemClock } from '../core/clock';
import type { Logger } from '../core/logger';
import type { Env } from '../game/services';
import { services } from './services';

/** The application environment for one request or job run. */
export async function env(log: Logger): Promise<Env> {
  return { store: services().store, clock: systemClock, log };
}
