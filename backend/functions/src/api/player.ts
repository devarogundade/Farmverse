import { CALLABLES } from '@farmverse/contracts';
import * as players from '../game/players';
import { env } from './env';
import { defineCallable } from './runtime';

/** `player-create`: onboarding — name, face, the starting farm (idempotent per uid: PLAYER_EXISTS). */
export const create = defineCallable({
  spec: CALLABLES.playerCreate,
  auth: 'required',
  handler: async ({ uid, data, log }) =>
    players.create(await env(log), uid, { displayName: data.displayName, avatarId: data.avatarId }),
});

/** `player-updateProfile`: rename (unique), cosmetics, settings. */
export const updateProfile = defineCallable({
  spec: CALLABLES.playerUpdateProfile,
  auth: 'required',
  handler: async ({ uid, data, log }) => players.updateProfile(await env(log), uid, data),
});

/** `player-suggestNames`: farm name ideas for the S07 dice, only names nobody has taken. */
export const suggestNames = defineCallable({
  spec: CALLABLES.playerSuggestNames,
  auth: 'required',
  handler: async ({ data, log }) => players.suggestNames(await env(log), data.count),
});
