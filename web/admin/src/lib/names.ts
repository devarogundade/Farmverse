import { ITEM_MODELS } from '@farmverse/shared/nft/metadata';

/** Goods a grant can give (the game's goods with a model; the island stores them in its farmhouses). */
export const GOODS = Object.entries(ITEM_MODELS)
  .filter(([, m]) => m.type === 'good')
  .map(([id, m]) => ({ id, label: m.label }));

const BOOSTS: Record<string, string> = {
  shield: 'Shield',
  fert: 'Fertiliser',
  rush: 'Rush',
  instant: 'Instant finish',
  basket: 'Basket',
  nightshift: 'Night shift',
  lunch: 'Lunch break',
};

const title = (id: string) => id.charAt(0).toUpperCase() + id.slice(1).replace(/_/g, ' ');

export const boostName = (id: string) => BOOSTS[id] ?? title(id);
export const goodName = (id: string) => ITEM_MODELS[id]?.label ?? title(id);

/** Activity log actions in words. */
export const ACTIONS: Record<string, string> = {
  'player.grant': 'Sent a gift',
  'player.ban': 'Banned',
  'player.unban': 'Unbanned',
  'player.signout': 'Signed out everywhere',
  'coupon.redeem': 'Marked coupon redeemed',
  'coupons.publish': 'Changed coupon tasks',
  'ops.update': 'Changed game settings',
  'news.publish': 'Posted news',
  'news.delete': 'Removed news',
};

export const actionName = (a: string) => ACTIONS[a] ?? a;

/** What a grant held, in words: "500 coins, 20 gems, 40 Wheat, 2 Fertiliser". */
export function grantSummary(d: Record<string, unknown>): string {
  const parts: string[] = [];
  if (Number(d['coins']) > 0) parts.push(`${d['coins']} coins`);
  if (Number(d['gems']) > 0) parts.push(`${d['gems']} gems`);
  for (const [id, n] of Object.entries((d['goods'] as Record<string, number>) ?? {})) parts.push(`${n} ${goodName(id)}`);
  for (const [id, n] of Object.entries((d['boosts'] as Record<string, number>) ?? {})) parts.push(`${n} ${boostName(id)}`);
  return parts.join(', ');
}
