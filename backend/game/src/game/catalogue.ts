import { CatalogueDoc, DEFAULT_CATALOGUE } from '@farmverse/contracts';
import { paths } from '../infra/paths';
import type { Env } from './services';

/** How long an instance trusts the catalogue it read before reading `config/catalogue` again. */
export const CATALOGUE_TTL_MS = 60_000;

let cached: { doc: CatalogueDoc; at: number } | undefined;

/**
 * The live catalogue (`config/catalogue`), or the designed one compiled into this build when nothing
 * is published yet or the published copy is older or broken. Cached per instance for a minute, so a
 * publish reaches every function within that time.
 */
export async function catalogue(env: Env): Promise<CatalogueDoc> {
  const now = env.clock.nowMs();
  if (cached && now - cached.at < CATALOGUE_TTL_MS) return cached.doc;
  let doc: CatalogueDoc = DEFAULT_CATALOGUE;
  const snap = await env.store.read(paths.catalogue());
  if (snap.exists) {
    const parsed = CatalogueDoc.safeParse(snap.data);
    if (!parsed.success) env.log.error('config/catalogue failed its schema; using the built-in one', { issues: parsed.error.issues.slice(0, 5) });
    else if (parsed.data.version >= DEFAULT_CATALOGUE.version) doc = parsed.data;
  }
  cached = { doc, at: now };
  return doc;
}

/** Tests: forget the cached catalogue. */
export function resetCatalogueCache(): void {
  cached = undefined;
}
