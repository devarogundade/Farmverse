import { z } from 'zod';
import { named } from '../registry';
import { EpochMs } from '../common/primitives';

export const NotificationKind = named(
  'NotificationKind',
  z.enum(['system', 'friend_joined', 'referral_reward', 'leaderboard_prize', 'mail']),
  'What a notification is about.',
);

export const NotificationButton = named(
  'NotificationButton',
  z.enum(['view', 'collect', 'open', 'claim']),
  'The button an inbox row shows (R50).',
);

/** `players/{uid}/notifications/{id}` (04 §4). Text is localized on the client from keys and params. */
export const NotificationDoc = named(
  'NotificationDoc',
  z.object({
    kind: NotificationKind,
    titleKey: z.string().max(96),
    bodyKey: z.string().max(96),
    params: z.record(z.string(), z.string().max(64)),
    route: z.string().max(48),
    routeArgs: z.record(z.string(), z.string().max(64)),
    /** The row's button (R50): view, collect, open, claim; absent = the row just opens its route. */
    actionKey: NotificationButton.optional(),
    createdAt: EpochMs,
    /** 0 until read. The only field a client may write (11 §1.4). */
    readAt: EpochMs,
  }),
  'One inbox row.',
);

export type NotificationDoc = z.infer<typeof NotificationDoc>;
