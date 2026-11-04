/**
 * Send mail to players' inboxes (with an optional reward they collect in the game).
 *
 *   npm run mail:send -- --project farmverse-8deb6 --to <uid> --title "Thanks!" --body "A gift from the team." [--coins 500] [--gems 10] [--route shop] [--days 30]
 *   npm run mail:send -- --project farmverse-8deb6 --all --title "Winter event" --body "It starts today!"  [--dry-run]
 *
 * Every message also pushes (push-onNotificationCreated), respecting each player's push settings.
 */
import { initializeApp } from 'firebase-admin/app';
import { getFirestore } from 'firebase-admin/firestore';
import { firestoreStore } from '../src/infra/firestoreStore';
import { UnitOfWork } from '../src/infra/unitOfWork';
import { writeMail, type Mail } from '../src/game/inbox';

async function main(): Promise<void> {
  const arg = (name: string) => {
    const i = process.argv.indexOf(name);
    return i > 0 ? process.argv[i + 1] : undefined;
  };
  const projectId = arg('--project');
  const title = arg('--title');
  const body = arg('--body');
  if (!projectId || !title || !body) throw new Error('usage: sendMail --project <id> (--to <uid> | --all) --title <t> --body <b> [--coins n] [--gems n] [--route screen] [--days n] [--dry-run]');
  const mail: Mail = {
    title, body,
    coins: Math.max(0, Number(arg('--coins') ?? 0)),
    gems: Math.max(0, Math.min(10_000, Number(arg('--gems') ?? 0))),
    route: arg('--route') ?? '',
    expireDays: Math.max(1, Math.min(90, Number(arg('--days') ?? 30))),
  };
  initializeApp({ projectId });
  const db = getFirestore();
  const store = firestoreStore(db);
  const to = arg('--to');
  let uids: string[];
  if (to) uids = [to];
  else if (process.argv.includes('--all')) uids = (await db.collection('players').select().get()).docs.map((d) => d.id);
  else throw new Error('--to <uid> or --all');
  if (process.argv.includes('--dry-run')) {
    console.log(`would send "${title}" to ${uids.length} player(s) on ${projectId}`);
    return;
  }
  for (let i = 0; i < uids.length; i += 200) {
    const chunk = uids.slice(i, i + 200);
    await store.runTransaction(async (tx) => {
      const uow = new UnitOfWork(tx);
      for (const uid of chunk) writeMail(uow, uid, mail, Date.now());
      uow.commit();
    });
  }
  console.log(`sent "${title}" to ${uids.length} player(s) on ${projectId}`);
}

main().catch((error: unknown) => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
