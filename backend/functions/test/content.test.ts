import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { nextManifest, platformContent, type ContentBuild } from '../tools/publishContent';

function build(version: number, crops: string): { dir: string; build: ContentBuild } {
  const dir = mkdtempSync(join(tmpdir(), 'content-'));
  writeFileSync(join(dir, `catalog_${version}.json`), '{"m_LocatorId":"x"}');
  writeFileSync(join(dir, `crops_${version}.bundle`), crops);
  return { dir, build: { contentVersion: version, catalogue: `catalog_${version}.json`, minAppVersion: '1.0.0', bundles: [{ file: `crops_${version}.bundle`, catalogueIds: ['crop_wheat', 'crop_corn'] }] } };
}

const url = (path: string) => `https://cdn.example.test/${path}`;

describe('content publishing', () => {
  it('describes each file by size and hash under a versioned path', () => {
    const { dir, build: b } = build(3, 'crop meshes');
    const { content, files } = platformContent(dir, b, url, 'android');
    expect(files).toHaveLength(2);
    expect(content.catalogueUrl).toBe('https://cdn.example.test/content/android/3/catalog_3.json');
    expect(content.bundles).toEqual([{
      name: 'crops_3.bundle',
      url: 'https://cdn.example.test/content/android/3/crops_3.bundle',
      bytes: 11,
      sha256: expect.stringMatching(/^[0-9a-f]{64}$/),
      catalogueIds: ['crop_corn', 'crop_wheat'],
    }]);
  });

  it('replaces one platform, never goes back, and never rewrites a version', () => {
    const v3 = platformContent(build(3, 'a').dir, build(3, 'a').build, url, 'android').content;
    const first = nextManifest(undefined, 'android', v3, '1.0.0', 1);
    expect(first).toMatchObject({ android: v3, minAppVersion: '1.0.0', publishedAt: 1 });
    const ios = nextManifest(first, 'ios', { ...v3, catalogueUrl: 'https://cdn.example.test/ios.json' }, '1.0.0', 2);
    expect(ios.android).toEqual(v3);
    expect(nextManifest(ios, 'android', v3, '1.0.0', 3).publishedAt).toBe(3); // same files again is fine
    const changed = platformContent(build(3, 'b').dir, build(3, 'b').build, url, 'android').content;
    expect(() => nextManifest(ios, 'android', changed, '1.0.0', 4)).toThrow(/bump "contentVersion"/);
    const v2 = platformContent(build(2, 'a').dir, build(2, 'a').build, url, 'android').content;
    expect(() => nextManifest(ios, 'android', v2, '1.0.0', 5)).toThrow(/go back/);
  });
});
