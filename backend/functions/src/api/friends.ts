import { CALLABLES } from '@farmverse/contracts';
import * as friends from '../game/friends';
import { env } from './env';
import { defineCallable } from './runtime';

/** `friends-createInvite`: a code and link to share (R44 "Invite friends"). */
export const createInvite = defineCallable({
  spec: CALLABLES.friendsCreateInvite,
  auth: 'required',
  handler: async ({ uid, log }) => friends.createInvite(await env(log), uid),
});

/** `friends-accept`: open an invite link; both players become friends. */
export const accept = defineCallable({
  spec: CALLABLES.friendsAccept,
  auth: 'required',
  handler: async ({ uid, data, log }) => friends.acceptInvite(await env(log), uid, data.code),
});
