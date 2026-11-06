import { describe, expect, it } from 'vitest';
import { awardCrewXp, levelFor, withCrewSave, workerPath, type Farmhand } from '../../src/game/crew';
import { World } from './world';

const hand = (token: string): Farmhand => ({ token, name: token, specialty: 'fields', speed: 2, carry: 2, stamina: 2, level: 1, rented: false });

describe('Farmhands', () => {
  it('levels need 100 × n more XP each, up to 10', () => {
    expect(levelFor(0)).toBe(1);
    expect(levelFor(99)).toBe(1);
    expect(levelFor(100)).toBe(2);
    expect(levelFor(300)).toBe(3);
    expect(levelFor(1e9)).toBe(10);
  });

  it('the crew is written into the save only when it changed', () => {
    const save = { simAt: 1, economy: { coins: 5, crew: [hand('a')] } };
    expect(withCrewSave(save, undefined)).toBe(save);
    expect(withCrewSave(save, [hand('a')])).toBe(save);
    const next = withCrewSave(save, [hand('a'), hand('b')]);
    expect((next.economy as { crew: Farmhand[] }).crew.map((f) => f.token)).toEqual(['a', 'b']);
    expect((next.economy as { coins: number }).coins).toBe(5);
  });

  it('a server run shares its loads out as XP, per token', async () => {
    const w = new World();
    await awardCrewXp(w.env, [hand('s-fh-2'), hand('s-fh-1')], 4, 450);
    expect(w.store.get(workerPath('s-fh-1'))).toMatchObject({ xp: 112, level: 2 });
    expect(w.store.get(workerPath('s-fh-2'))).toMatchObject({ xp: 112 });
    await awardCrewXp(w.env, [hand('s-fh-1')], 4, 0);
    expect(w.store.get(workerPath('s-fh-1'))?.['xp']).toBe(112);
  });
});
