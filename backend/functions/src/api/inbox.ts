import { CALLABLES } from '@farmverse/contracts';
import { deleteMessages } from '../game/inbox';
import { env } from './env';
import { defineCallable } from './runtime';

/** `inbox-delete`: remove messages (not ones with a reward still to collect). */
export const remove = defineCallable({ spec: CALLABLES.inboxDelete, auth: 'required', handler: async ({ uid, data, log }) => deleteMessages(await env(log), uid, data.ids) });
