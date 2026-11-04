import { getAuth } from 'firebase-admin/auth';
import { CALLABLES, type PlayerDoc } from '@farmverse/contracts';
import { exportAccount } from '../game/export';
import { usernameKey } from '../game/onboarding';
import { env } from './env';
import { paths } from '../infra/paths';
import { adminApp, db, defineCallable } from './runtime';

/**
 * `account-delete` (store requirement): free the player's name, delete every
 * document under `players/{uid}` (the island included), then the Auth user.
 */
export const remove = defineCallable(
  {
    spec: CALLABLES.accountDelete,
    auth: 'required',
    handler: async ({ uid, log }) => {
      const firestore = db();
      const player = (await firestore.doc(paths.player(uid)).get()).data() as PlayerDoc | undefined;
      const nowMs = Date.now();
      if (player) await firestore.doc(paths.username(usernameKey(player.displayName))).delete();
      await firestore.recursiveDelete(firestore.doc(paths.player(uid)));
      await getAuth(adminApp()).deleteUser(uid).catch((error: unknown) => log.warn('auth user already gone', { error }));
      log.info('account deleted');
      return { deletedAt: nowMs };
    },
  },
  { timeoutSeconds: 120 },
);

/** `account-export`: everything stored about the caller, as one JSON file. */
export const exportData = defineCallable(
  {
    spec: CALLABLES.accountExport,
    auth: 'required',
    handler: async ({ uid, log }) => exportAccount(await env(log), uid),
  },
  { memory: '512MiB', timeoutSeconds: 60 },
);
