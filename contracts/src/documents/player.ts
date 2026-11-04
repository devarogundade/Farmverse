import { z } from 'zod';
import { named } from '../registry';
import { CatalogueId } from '../common/ids';
import { EpochMs } from '../common/primitives';
import { Rev, SchemaVersion } from './common';

/** `players/{uid}` — the player's public name and face. The island itself is `state/island`. */
export const PlayerDoc = named(
  'PlayerDoc',
  z.object({
    schemaVersion: SchemaVersion,
    rev: Rev,
    displayName: z.string().min(3).max(16),
    avatarId: CatalogueId,
    frameId: CatalogueId.optional(),
    /** Short public number shown in Settings and Account ("Player ID 48211"). */
    shortId: z.string().regex(/^\d{5,10}$/),
    lastSeenAt: EpochMs,
    createdAt: EpochMs,
  }),
  'A player: their public name and face.',
);

/**
 * `players/{uid}/private/settings` — display preferences with no economic value;
 * the only player document a client writes directly (11 §1.4), field by field,
 * checked by security rules.
 */
export const SettingsDoc = named(
  'SettingsDoc',
  z.object({
    schemaVersion: SchemaVersion,
    allowVisitors: z.boolean(),
    pushEnabled: z.boolean(),
    /** Push topics the player turned off or on (PushTopic → on); absent = on. */
    pushTopics: z.record(z.string(), z.boolean()).optional(),
    quietStart: z.number().int().min(0).max(23),
    quietEnd: z.number().int().min(0).max(23),
    locale: z.string().max(16),
    timezone: z.string().max(64),
    reduceMotion: z.boolean(),
    textScale: z.number().int().min(80).max(150),
    music: z.boolean(),
    sound: z.boolean(),
    vibration: z.boolean(),
    hints: z.boolean(),
    /** S03: anonymous play analytics, off until the player opts in. */
    analyticsConsent: z.boolean(),
    /** S03: when the player accepted the terms and privacy policy; 0 = not yet. */
    termsAcceptedAt: EpochMs,
  }),
  'Player preferences.',
);

export type PlayerDoc = z.infer<typeof PlayerDoc>;
export type SettingsDoc = z.infer<typeof SettingsDoc>;
