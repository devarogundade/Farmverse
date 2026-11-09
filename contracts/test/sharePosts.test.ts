import { describe, expect, it } from 'vitest';
import { CatalogueDoc, DEFAULT_CATALOGUE } from '../src';

/** X's count: a link is 23, an emoji 2, CJK 2, other characters 1 (twitter-text's weighting, simplified). */
function xLength(post: string): number {
  const text = post.replace(/\{link\}/g, 'x'.repeat(23));
  let n = 0;
  for (const { segment } of new Intl.Segmenter('en', { granularity: 'grapheme' }).segment(text)) {
    if (/\p{Extended_Pictographic}/u.test(segment)) n += 2;
    else for (const ch of segment) n += /[ᄀ-￿]/.test(ch) && !/[ -⁯️]/.test(ch) ? 2 : 1;
  }
  return n;
}

describe('daily X share posts', () => {
  const posts = DEFAULT_CATALOGUE.sharePosts;

  it('the built-in catalogue passes its schema with the share chest', () => {
    expect(CatalogueDoc.safeParse(DEFAULT_CATALOGUE).success).toBe(true);
    expect(DEFAULT_CATALOGUE.chests['share']).toEqual({ coins: 100, gems: 5, boosts: {} });
  });

  it('has ten distinct posts, each with the invite link once', () => {
    expect(posts).toHaveLength(10);
    expect(new Set(posts).size).toBe(10);
    for (const p of posts) expect(p.match(/\{link\}/g)).toHaveLength(1);
  });

  it('every post fits a free X account (280) with room to spare', () => {
    for (const p of posts) expect(xLength(p), p).toBeLessThanOrEqual(200);
  });

  it('every post names Farmverse, NEAR and the hackathon', () => {
    for (const p of posts) {
      expect(p, p).toMatch(/farmverse/i);
      expect(p, p).toMatch(/NEAR/);
      expect(p, p).toMatch(/#NEARHackathon/);
    }
  });
});
