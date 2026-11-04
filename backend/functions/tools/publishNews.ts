/**
 * Publish (or remove) an item in the game's News. Run by an operator with project credentials;
 * players can only read news.
 *
 *   npx tsx tools/publishNews.ts --project farmverse-8deb6 --title "Winter is coming" \
 *     --body "Stock up on bread: everyone eats more in winter." [--art Icon_Megaphone] [--url https://…] [--id n_winter2026] \
 *     [--title-fr "L'hiver arrive" --body-fr "…"] [--title-ja "…" --body-ja "…"] [--push]
 *   npx tsx tools/publishNews.ts --project farmverse-8deb6 --remove n_winter2026
 *
 * The item is checked against the contract (NewsDoc) before it is written; the game shows the newest 20.
 * --push also tells every player seen in the last 30 days who left News on, with one push each, in their
 * language, at a good hour for them (push-onNewsCreated). Use it for news that matters; not every item.
 */
import { randomBytes } from 'node:crypto';
import { initializeApp } from 'firebase-admin/app';
import { getFirestore } from 'firebase-admin/firestore';
import { NewsDoc } from '@farmverse/contracts';
import { paths } from '../src/infra/paths';

async function main(): Promise<void> {
  const arg = (name: string) => {
    const i = process.argv.indexOf(name);
    return i > 0 ? process.argv[i + 1] : undefined;
  };
  const projectId = arg('--project');
  if (!projectId) throw new Error('usage: publishNews --project <projectId> (--title <t> --body <b> [--art <icon>] [--url <link>] [--id <id>] | --remove <id>)');
  initializeApp({ projectId });
  const db = getFirestore();

  const remove = arg('--remove');
  if (remove) {
    await db.doc(paths.newsItem(remove)).delete();
    console.log(`removed news ${remove} from ${projectId}`);
    return;
  }

  const id = arg('--id') ?? `n_${randomBytes(8).toString('hex')}`;
  const i18n: Record<string, { title: string; body: string }> = {};
  for (const lang of ['fr', 'ja']) {
    const title = arg(`--title-${lang}`);
    const body = arg(`--body-${lang}`);
    if (title && body) i18n[lang] = { title, body };
    else if (title || body) throw new Error(`--title-${lang} and --body-${lang} go together`);
  }
  const item = NewsDoc.parse({
    title: arg('--title'),
    body: arg('--body'),
    ...(arg('--art') ? { art: arg('--art') } : {}),
    ...(arg('--url') ? { url: arg('--url') } : {}),
    ...(Object.keys(i18n).length > 0 ? { i18n } : {}),
    ...(process.argv.includes('--push') ? { push: true } : {}),
    publishedAt: Date.now(),
  });
  await db.doc(paths.newsItem(id)).set(item);
  console.log(`published news ${id} to ${projectId}: ${item.title}${item.push ? ' (with a push)' : ''}`);
}

main().catch((error: unknown) => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
