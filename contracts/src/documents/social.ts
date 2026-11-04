import { z } from 'zod';
import { named } from '../registry';
import { CatalogueId } from '../common/ids';
import { EpochMs } from '../common/primitives';

/** `players/{uid}/friends/{friendUid}` — written by the server when an invite is accepted (R44 friends board). */
export const FriendDoc = named(
  'FriendDoc',
  z.object({
    displayName: z.string().max(16),
    avatarId: CatalogueId,
    shortId: z.string().max(10),
    since: EpochMs,
  }),
  'One friend.',
);

export type FriendDoc = z.infer<typeof FriendDoc>;

export const ReferralStatus = named('ReferralStatus', z.enum(['pending', 'rewarded']), 'Where a referral is.');
export const RewardReason = named('RewardReason', z.enum(['referral_inviter', 'referral_invitee', 'leaderboard_week', 'mail']), 'Why a reward was given.');

/**
 * `referrals/{inviteeUid}` (server only): who brought this player in. Written when a new player
 * accepts an invite; rewarded once, when the invitee's island reaches the referral level.
 */
export const ReferralDoc = named(
  'ReferralDoc',
  z.object({
    inviterUid: z.string().max(128),
    createdAt: EpochMs,
    status: ReferralStatus,
    rewardedAt: EpochMs,
  }),
  'A referral: who invited this player, and whether both have been rewarded.',
);

/** `players/{uid}/rewards/{id}`: something the server gives that the game collects once (coins land on the island). */
export const RewardDoc = named(
  'RewardDoc',
  z.object({
    reason: RewardReason,
    coins: z.number().int().min(0).max(1_000_000),
    gems: z.number().int().min(0).max(10_000),
    /** Who the reward is about (the friend's name), for the screen. */
    about: z.string().max(16),
    createdAt: EpochMs,
    /** 0 until the game collected it. */
    claimedAt: EpochMs,
  }),
  'A reward waiting to be collected.',
);

/** A news item's words in one language. */
export const NewsText = named('NewsText', z.object({ title: z.string().min(1).max(60), body: z.string().min(1).max(600) }), "A news item's title and body in one language.");

/** `news/{id}`: an announcement in the game's News, written by the team (tools/publishNews.ts). */
export const NewsDoc = named(
  'NewsDoc',
  z.object({
    title: z.string().min(1).max(60),
    body: z.string().min(1).max(600),
    /** One of the game's icon names (Icon_Megaphone…); the screen picks a default when absent. */
    art: z.string().max(64).optional(),
    /** An optional "Read more" link. */
    url: z.string().url().max(300).optional(),
    /** The title and body in other languages (ISO code → text); English (`title`, `body`) otherwise. */
    i18n: z.record(z.string().max(8), NewsText).optional(),
    /** Also tell players with a push (each at their own good hour, if they left News on). */
    push: z.boolean().optional(),
    publishedAt: EpochMs,
  }),
  'One news item.',
);

export type ReferralDoc = z.infer<typeof ReferralDoc>;
export type RewardDoc = z.infer<typeof RewardDoc>;
export type NewsDoc = z.infer<typeof NewsDoc>;
