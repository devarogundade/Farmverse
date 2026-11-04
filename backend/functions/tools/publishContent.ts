/**
 * Publish a platform's art and audio (Addressables build output) so installed
 * apps download it after launch (backend plan step 5):
 *
 *   npm run content:publish -- --project farmverse-8deb6 --platform android --dir ../../ServerData/Android --bucket farmverse-8deb6.firebasestorage.app
 *   npm run content:publish -- --platform ios --dir ../../ServerData/iOS --dry-run
 *
 * `--dir` holds the bundles, the Addressables catalogue and `content.json`,
 * written by the Unity build step (Farmverse > Build Content):
 *
 *   { "contentVersion": 3, "catalogue": "catalog_3.json", "minAppVersion": "1.0.0",
 *     "bundles": [ { "file": "crops_3.bundle", "catalogueIds": ["crop_wheat", ...] } ] }
 *
 * Files go to Storage under `content/{platform}/{contentVersion}/` (readable by
 * signed-in players, storage.rules) and `content/current` is updated for that
 * platform only. A version, once published, is immutable: publishing it again
 * with different files is refused (bump `contentVersion`).
 */
import { createHash } from 'node:crypto';
import { readFileSync, statSync } from 'node:fs';
import { basename, resolve } from 'node:path';
import { ContentManifestDoc, PlatformContent } from '@farmverse/contracts';

export interface ContentBuild {
  readonly contentVersion: number;
  readonly catalogue: string;
  readonly minAppVersion: string;
  readonly bundles: readonly { readonly file: string; readonly catalogueIds: readonly string[] }[];
}

export interface LocalFile {
  readonly path: string;
  readonly name: string;
  readonly bytes: number;
  readonly sha256: string;
}

export const contentPrefix = (platform: string, version: number) => `content/${platform}/${version}`;

export function describeFile(path: string): LocalFile {
  const data = readFileSync(path);
  return { path, name: basename(path), bytes: statSync(path).size, sha256: createHash('sha256').update(data).digest('hex') };
}

/** The manifest entry for one platform's build, from the files on disk. */
export function platformContent(dir: string, build: ContentBuild, publicUrl: (objectPath: string) => string, platform: string): { content: PlatformContent; files: LocalFile[] } {
  const prefix = contentPrefix(platform, build.contentVersion);
  const catalogue = describeFile(resolve(dir, build.catalogue));
  const files = [catalogue];
  const bundles = build.bundles.map((b) => {
    const file = describeFile(resolve(dir, b.file));
    files.push(file);
    return { name: file.name, url: publicUrl(`${prefix}/${file.name}`), bytes: file.bytes, sha256: file.sha256, catalogueIds: [...b.catalogueIds].sort() };
  });
  const content = PlatformContent.parse({ contentVersion: build.contentVersion, catalogueUrl: publicUrl(`${prefix}/${catalogue.name}`), bundles });
  return { content, files };
}

/**
 * The next `content/current`: this platform replaced, the other kept. Refuses
 * to go back a version, and to republish a version with different bundles.
 */
export function nextManifest(current: ContentManifestDoc | undefined, platform: 'android' | 'ios', content: PlatformContent, minAppVersion: string, nowMs: number): ContentManifestDoc {
  const previous = current?.[platform];
  if (previous && previous.contentVersion > content.contentVersion) {
    throw new Error(`${platform} is already at content version ${previous.contentVersion}; refusing to go back to ${content.contentVersion}.`);
  }
  if (previous && previous.contentVersion === content.contentVersion && JSON.stringify(previous.bundles) !== JSON.stringify(content.bundles)) {
    throw new Error(`${platform} content version ${content.contentVersion} is already published with different files; bump "contentVersion".`);
  }
  return ContentManifestDoc.parse({ ...(current ?? {}), [platform]: content, minAppVersion, publishedAt: nowMs });
}

function arg(name: string): string | undefined {
  const index = process.argv.indexOf(`--${name}`);
  return index > 0 ? process.argv[index + 1] : undefined;
}

async function main(): Promise<void> {
  const platform = arg('platform');
  const dir = arg('dir');
  const projectId = arg('project');
  const dryRun = process.argv.includes('--dry-run');
  if ((platform !== 'android' && platform !== 'ios') || !dir || (!dryRun && !projectId)) {
    throw new Error('usage: publishContent --platform android|ios --dir <build folder> (--project <projectId> [--bucket <bucket>] | --dry-run)');
  }
  const build = JSON.parse(readFileSync(resolve(dir, 'content.json'), 'utf8')) as ContentBuild;
  const bucketName = arg('bucket') ?? `${projectId ?? 'demo-farmverse'}.firebasestorage.app`;
  const publicUrl = (objectPath: string) => `https://firebasestorage.googleapis.com/v0/b/${bucketName}/o/${encodeURIComponent(objectPath)}?alt=media`;
  const { content, files } = platformContent(dir, build, publicUrl, platform);

  if (dryRun) {
    console.log(JSON.stringify(content, null, 2));
    console.log(`${files.length} files, ${files.reduce((n, f) => n + f.bytes, 0)} bytes (dry run: nothing uploaded)`);
    return;
  }

  const { initializeApp } = await import('firebase-admin/app');
  const { getFirestore } = await import('firebase-admin/firestore');
  const { getStorage } = await import('firebase-admin/storage');
  initializeApp({ projectId, storageBucket: bucketName });
  const db = getFirestore();
  const manifestRef = db.doc('content/current');
  const snap = await manifestRef.get();
  const current = snap.exists ? ContentManifestDoc.parse(snap.data()) : undefined;
  const manifest = nextManifest(current, platform, content, build.minAppVersion, Date.now());

  const bucket = getStorage().bucket(bucketName);
  const prefix = contentPrefix(platform, build.contentVersion);
  for (const file of files) {
    await bucket.upload(file.path, {
      destination: `${prefix}/${file.name}`,
      // versioned paths never change, so clients and the CDN may cache them for good
      metadata: { cacheControl: 'public, max-age=31536000, immutable', metadata: { sha256: file.sha256 } },
    });
    console.log(`uploaded ${prefix}/${file.name} (${file.bytes} bytes)`);
  }
  await manifestRef.set(manifest);
  console.log(`published ${platform} content v${build.contentVersion} to ${projectId}`);
}

if (typeof require !== 'undefined' && require.main === module) {
  main().catch((error: unknown) => {
    console.error(error instanceof Error ? error.message : error);
    process.exit(1);
  });
}
