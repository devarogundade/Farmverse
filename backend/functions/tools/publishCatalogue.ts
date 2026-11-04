/**
 * Publish the game's tuning data (contracts/src/catalogue.ts, DEFAULT_CATALOGUE) to `config/catalogue`,
 * where every function and every client reads it. Run by an operator with project credentials, and by
 * tools/dev/start-backend.cmd against the local emulators:
 *
 *   npm run catalogue:publish -- --project farmverse-8deb6 [--dry-run] [--force]
 *
 * Refuses to replace a published catalogue with an older or equal version that differs (bump
 * `version` in catalogue.ts) unless --force. Remember `npm run contracts:gen` so new app builds carry
 * the same data offline.
 */
import { isDeepStrictEqual } from 'node:util';
import { initializeApp } from 'firebase-admin/app';
import { getFirestore } from 'firebase-admin/firestore';
import { CatalogueDoc, DEFAULT_CATALOGUE } from '@farmverse/contracts';
import { paths } from '../src/infra/paths';

async function main(): Promise<void> {
  const arg = (name: string) => {
    const i = process.argv.indexOf(name);
    return i > 0 ? process.argv[i + 1] : undefined;
  };
  const projectId = arg('--project');
  if (!projectId) throw new Error('usage: publishCatalogue --project <projectId> [--dry-run] [--force]');
  const dryRun = process.argv.includes('--dry-run');
  const force = process.argv.includes('--force');

  const next = CatalogueDoc.parse({ ...DEFAULT_CATALOGUE, publishedAt: Date.now() });
  initializeApp({ projectId });
  const db = getFirestore();
  const ref = db.doc(paths.catalogue());
  const current = await ref.get();
  if (current.exists) {
    const live = CatalogueDoc.safeParse(current.data());
    if (live.success) {
      const same = isDeepStrictEqual({ ...live.data, publishedAt: 0 }, { ...next, publishedAt: 0 });
      if (same) {
        console.log(`catalogue v${next.version} is already live on ${projectId}`);
        return;
      }
      if (live.data.version >= next.version && !force) {
        throw new Error(`${projectId} has catalogue v${live.data.version}; bump version in contracts/src/catalogue.ts (now v${next.version}) or pass --force`);
      }
    }
  }
  if (dryRun) {
    console.log(`would publish catalogue v${next.version} to ${projectId}: ${Object.keys(next.gemOffers).length} gem offers`);
    return;
  }
  await ref.set(next);
  console.log(`published catalogue v${next.version} to ${projectId}${process.env['FIRESTORE_EMULATOR_HOST'] ? ' (emulator)' : ''}`);
}

main().catch((error: unknown) => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
