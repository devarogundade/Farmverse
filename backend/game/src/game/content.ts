import { ContentManifestDoc, type Platform, type PlatformContent } from '@farmverse/contracts';
import { AppError } from '../core/errors';
import { paths } from '../infra/paths';
import type { Env } from './services';

/** `content-manifest`: the art and audio bundles published for a platform (`content/current`). */
export async function contentFor(env: Env, platform: Platform): Promise<{ content?: PlatformContent; takenAt: number }> {
  const snap = await env.store.read(paths.contentManifest());
  const takenAt = env.clock.nowMs();
  if (!snap.exists) return { takenAt };
  const manifest = ContentManifestDoc.safeParse(snap.data);
  if (!manifest.success) {
    env.log.error('content manifest failed its schema', { issues: manifest.error.issues.slice(0, 5) });
    throw new AppError('DATA_CORRUPT');
  }
  const content = platform === 'android' ? manifest.data.android : platform === 'ios' ? manifest.data.ios : undefined;
  return content ? { content, takenAt } : { takenAt };
}
