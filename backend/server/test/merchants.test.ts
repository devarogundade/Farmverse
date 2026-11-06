import { describe, expect, it } from 'vitest';
import { MERCHANTS, quote } from '../src/chain/merchants';

const wheat = MERCHANTS.find((m) => m.name === 'bea')!.goods.find((g) => g.item === 'wheat')!;
const farm = (yocto: bigint) => Number(yocto) / 1e18;

describe('merchant pricing', () => {
  it('pays the base price at its target stock', () => {
    const q = quote(wheat, { stock: wheat.target, sales24h: 0 });
    expect(farm(q.buyPerUnit)).toBeCloseTo(0.05, 3); // 5 coins at 100 coins a FARM
    expect(farm(q.sellPerUnit)).toBeCloseTo(0.0625, 4);
    expect(q.why).toBe('Steady');
  });

  it('pays more when stock is low and it sells, less when it has plenty, within 0.5-2x', () => {
    const low = quote(wheat, { stock: 10, sales24h: 4 });
    const lots = quote(wheat, { stock: 5000, sales24h: 0 });
    expect(farm(low.buyPerUnit)).toBeCloseTo(0.1, 3); // capped at 2x
    expect(low.why).toContain('paying more');
    expect(farm(lots.buyPerUnit)).toBeCloseTo(0.025, 3); // floored at 0.5x
    expect(lots.why).toContain('paying less');
  });

  it('every merchant has goods and sensible lots', () => {
    expect(MERCHANTS.map((m) => m.name)).toEqual(['bea', 'ruth', 'tomas', 'ivy']);
    for (const m of MERCHANTS) for (const g of m.goods) expect(g.item.startsWith('golden_') ? g.lot === 1 : g.lot === 100).toBe(true);
  });
});
