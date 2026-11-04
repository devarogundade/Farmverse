import { onSchedule } from 'firebase-functions/v2/scheduler';
import { systemClock } from '../core/clock';
import { cloudLogger } from '../core/logger';
import { payWeek } from '../game/leaderboard';
import { services } from './services';
import { REGION } from './runtime';

/** `leaderboard-payWeek`: last week's prizes, every Monday just after the week closes. */
export const payWeekJob = onSchedule({ schedule: '10 0 * * 1', timeZone: 'Etc/UTC', region: REGION }, async () => {
  await payWeek({ store: services().store, clock: systemClock, log: cloudLogger });
});
