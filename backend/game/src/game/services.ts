import type { Clock } from '../core/clock';
import type { Logger } from '../core/logger';
import type { Store } from '../infra/store';

/** What every application service needs; the api layer builds it, tests build it in memory. */
export interface Env {
  readonly store: Store;
  readonly clock: Clock;
  readonly log: Logger;
}
