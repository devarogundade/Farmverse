import { describe, expect, it } from 'vitest';
import { fold, MAX_MIRROR_AWAY_MS, nextEarned, parseAdopted, plusGiven, sync, wake, WAKE_HORIZON_MS, type IslandDeps, type IslandDoc } from '../../src/game/islands';
import { EngineRefusal, type EngineAdvance, type EngineEarned, type EngineExchange, type ExchangeOp, type IslandEngine, type IslandSave } from '../../src/infra/islandEngine';
import { paths } from '../../src/infra/paths';
import { FARMER, HOUR, MINUTE, World } from './world';

/**
 * A stand-in for the C# engine: one load of wood per started minute since simAt,
 * the next wake 10 minutes out. Enough to see time being owed, folded and paid once.
 */
class FakeEngine implements IslandEngine {
  calls = 0;
  refuse: EngineRefusal | undefined;
  async advance(state: IslandSave, now: number): Promise<EngineAdvance> {
    this.calls++;
    if (this.refuse) throw this.refuse;
    if (now < state.simAt) throw new EngineRefusal(400, 'backwards');
    // like IslandSim: away time runs from the economy's own clock
    const from = (state['economy'] as { now?: number } | undefined)?.now ?? state.simAt;
    const minutes = Math.floor((now - from) / 60);
    const wood = Number(state['wood'] ?? 0) + minutes;
    return {
      state: { ...state, simAt: now, wood, economy: { now } },
      report: { seconds: now - from, workers: minutes > 0 ? 2 : 0, loads: minutes, seedCoins: 0, storageFull: wood >= 100, gathered: minutes ? { wood: minutes } : {} },
      nextWake: now + 600,
    };
  }
}

function setup() {
  const w = new World();
  const engine = new FakeEngine();
  const booked: { uid: string; atMs: number }[] = [];
  const deps: IslandDeps = { env: w.env, engine, wakes: { schedule: async (uid, atMs) => void booked.push({ uid, atMs }) } };
  const save = JSON.stringify({ rulesVersion: 1, simAt: 0, seed: 7, buildings: [], wood: 0 });
  const doc = () => w.store.get(paths.island(FARMER)) as unknown as IslandDoc;
  return { w, engine, booked, deps, save, doc };
}

describe('island sync', () => {
  it('refuses a player with no island and nothing to adopt', async () => {
    const { deps } = setup();
    await expect(sync(deps, FARMER)).rejects.toMatchObject({ code: 'NOT_FOUND' });
  });

  it('adopts the device island at "now", owing no time for the device clock', async () => {
    const { w, deps, save, doc, booked } = setup();
    const res = await sync(deps, FARMER, save);
    expect(res.report).toMatchObject({ awaySeconds: 0, loads: 0, gathered: [] });
    expect(doc()).toMatchObject({ rev: 1, simAtMs: w.clock.nowMs(), lastSyncMs: w.clock.nowMs() });
    expect(booked).toEqual([{ uid: FARMER, atMs: w.clock.nowMs() + 10 * MINUTE }]);
  });

  it('ignores a second adopt once the server has the island', async () => {
    const { w, deps, save, doc } = setup();
    await sync(deps, FARMER, save);
    w.clock.advance(5 * MINUTE);
    const res = await sync(deps, FARMER, JSON.stringify({ rulesVersion: 1, simAt: 0, buildings: [], wood: 999 }));
    expect(JSON.parse(res.state).wood).toBe(5);
    expect(doc().rev).toBe(2);
  });

  it('refuses saves that are not islands', async () => {
    const { deps } = setup();
    for (const bad of ['nope', '[]', '{"rulesVersion":0,"buildings":[]}', '{"rulesVersion":1}']) {
      await expect(sync(deps, FARMER, bad)).rejects.toMatchObject({ code: 'INVALID_ARGUMENT' });
    }
    expect(() => parseAdopted('x'.repeat(400_001), 0)).toThrow();
  });

  it('maps an engine refusal of a newer device save to CONFLICT', async () => {
    const { deps, engine, save } = setup();
    engine.refuse = new EngineRefusal(409, 'newer rules');
    await expect(sync(deps, FARMER, save)).rejects.toMatchObject({ code: 'CONFLICT' });
  });

  it('reports wake work once, on the next sync, then clears it', async () => {
    const { w, deps, save, doc } = setup();
    await sync(deps, FARMER, save);
    w.clock.advance(10 * MINUTE);
    expect(await wake(deps, FARMER)).toBe('advanced');
    w.clock.advance(10 * MINUTE);
    expect(await wake(deps, FARMER)).toBe('advanced');
    expect(doc().pending).toMatchObject({ loads: 20, gathered: { wood: 20 } });
    w.clock.advance(3 * MINUTE);
    const back = await sync(deps, FARMER);
    expect(back.report).toMatchObject({ awaySeconds: 23 * 60, loads: 23, workers: 2, gathered: [{ item: 'wood', count: 23 }] });
    expect(doc().pending).toMatchObject({ loads: 0, gathered: {} });
    const again = await sync(deps, FARMER);
    expect(again.report).toMatchObject({ awaySeconds: 0, loads: 0 });
  });

  it('skips a stale wake and stops booking past the horizon', async () => {
    const { w, deps, save, engine, booked } = setup();
    await sync(deps, FARMER, save);
    const calls = engine.calls;
    w.clock.advance(MINUTE);
    expect(await wake(deps, FARMER)).toBe('early');
    expect(engine.calls).toBe(calls);
    expect(await wake(setup().deps, FARMER)).toBe('missing');

    booked.length = 0;
    w.clock.advance(WAKE_HORIZON_MS);
    await wake(deps, FARMER);
    expect(booked).toEqual([]);
  });

  it('retries when another writer gets in between the engine run and the write', async () => {
    const { w, deps, save, doc } = setup();
    await sync(deps, FARMER, save);
    w.clock.advance(HOUR);
    let raced = false;
    const racing: IslandDeps = {
      ...deps,
      engine: {
        advance: async (s, now) => {
          if (!raced) {
            raced = true;
            await w.store.runTransaction(async (tx) => tx.set(paths.island(FARMER), { rev: 7 }, { merge: true }));
          }
          return deps.engine.advance(s, now);
        },
      },
    };
    await sync(racing, FARMER);
    expect(doc().rev).toBe(8);
  });

  it('restamps the economy clock on adoption, so an old device save earns nothing', async () => {
    const { w, deps } = setup();
    const old = JSON.stringify({ rulesVersion: 1, simAt: 0, buildings: [], wood: 0, economy: { now: w.clock.nowMs() / 1000 - 86_400 } });
    const res = await sync(deps, FARMER, old);
    expect(res.report.loads).toBe(0);
    expect(JSON.parse(res.state).wood).toBe(0);
  });

  it('mirrors the device island in shadow mode: replaced each sync, device clock kept (clamped), no wakes', async () => {
    const { w, deps, doc, booked } = setup();
    const at = (msAgo: number, wood: number) =>
      JSON.stringify({ rulesVersion: 1, simAt: (w.clock.nowMs() - msAgo) / 1000, buildings: [], wood, economy: { now: (w.clock.nowMs() - msAgo) / 1000 } });
    const first = await sync(deps, FARMER, at(30 * MINUTE, 0), true);
    expect(first.report).toMatchObject({ awaySeconds: 30 * 60, loads: 30 });
    const second = await sync(deps, FARMER, at(5 * MINUTE, 500), true);
    expect(JSON.parse(second.state).wood).toBe(505);
    expect(doc().rev).toBe(2);
    const ancient = await sync(deps, FARMER, at(30 * 24 * HOUR, 0), true);
    expect(ancient.report.awaySeconds).toBe(MAX_MIRROR_AWAY_MS / 1000);
    expect(booked).toEqual([]);
  });

  it('folds reports: sums loads and goods, keeps the busiest crew and the latest storage state', () => {
    const a = fold({ workers: 3, loads: 1, seedCoins: 2, storageFull: true, gathered: { wood: 1 } }, { seconds: 1, workers: 1, loads: 2, seedCoins: 3, storageFull: false, gathered: { wood: 2, stone: 1 } });
    expect(a).toEqual({ workers: 3, loads: 3, seedCoins: 5, storageFull: false, gathered: { wood: 3, stone: 1 } });
  });

  it('folds the workers\' jobs, food and the first reason work stopped', () => {
    const empty = { workers: 0, loads: 0, seedCoins: 0, storageFull: false, gathered: {} };
    const run = (o: object) => ({ seconds: 1, workers: 2, loads: 1, seedCoins: 0, storageFull: false, gathered: {}, ...o });
    const a = fold(empty, run({ harvested: 8, fed: 4, foodUsed: 4.8 }));
    const b = fold(a, run({ harvested: 4, sold: 3, soldCoins: 30, foodUsed: 2.4, stopped: 'food', stoppedAt: 500 }));
    const c = fold(b, run({ stopped: 'storage', stoppedAt: 900, capped: true }));
    expect(c).toMatchObject({ jobs: { harvested: 12, fed: 4, sold: 3, soldCoins: 30 }, foodUsed: 7.2, stopped: 'food', stoppedAt: 500, capped: true });
  });

  it('tells the player once when a wake finds the workers out of food, and reports it on the next sync', async () => {
    const { w, save } = setup();
    let hungry = 0;
    const engine: IslandEngine = {
      async advance(state, now) {
        return { state: { ...state, simAt: now, economy: { now } }, report: { seconds: 60, workers: 2, loads: 1, seedCoins: 0, storageFull: false, gathered: {}, harvested: 4, stopped: 'food', stoppedAt: now }, nextWake: now + 600 };
      },
    };
    const deps: IslandDeps = { env: w.env, engine, wakes: { schedule: async () => undefined }, onWorkersHungry: async () => void hungry++ };
    await sync(deps, FARMER, save);
    w.clock.advance(HOUR);
    expect(await wake(deps, FARMER)).toBe('advanced');
    w.clock.advance(HOUR);
    expect(await wake(deps, FARMER)).toBe('advanced');
    expect(hungry).toBe(1);
    const back = await sync(deps, FARMER);
    expect(back.report).toMatchObject({ stopped: 'food', harvested: 12 });
  });

  it('grants the Farm Pass\'s longer shift from the server\'s pass doc, not the device\'s word', async () => {
    const { w, save } = setup();
    const seen: unknown[] = [];
    const engine: IslandEngine = {
      async advance(state, now) {
        seen.push((state['economy'] as { workPass?: unknown } | undefined)?.workPass);
        return { state: { ...state, simAt: now }, report: { seconds: 0, workers: 0, loads: 0, seedCoins: 0, storageFull: false, gathered: {} }, nextWake: now + 600 };
      },
    };
    const deps: IslandDeps = { env: w.env, engine, wakes: { schedule: async () => undefined } };
    const cheat = JSON.stringify({ ...JSON.parse(save), economy: { now: 0, workPass: true } });
    await sync(deps, FARMER, cheat);
    expect(seen.at(-1)).toBe(false);
    w.patch(paths.pass(FARMER), { premiumActive: true });
    w.clock.advance(HOUR);
    await sync(deps, FARMER);
    expect(seen.at(-1)).toBe(true);
  });
});

/** The fake engine plus the growth check: wood may grow by 10 a minute, the store holds 1,000. */
class EarningEngine extends FakeEngine {
  failEarned = false;
  async earned(before: IslandSave, after: IslandSave, seconds: number): Promise<EngineEarned> {
    if (this.failEarned) throw new Error('engine down');
    const was = Number(before['wood'] ?? 0);
    const now = Number(after['wood'] ?? 0);
    const eggs = Number(after['eggs'] ?? 0);
    const gain: Record<string, number> = {};
    if (now > was) gain['wood'] = Math.min(now - was, Math.ceil(10 * (seconds / 60)));
    return { gain, stock: { wood: now, ...(eggs ? { egg: eggs } : {}) }, room: 1_000 };
  }
  async exchange(state: IslandSave, now: number, ops: readonly ExchangeOp[]): Promise<EngineExchange> {
    const eggs = ops.reduce((a, o) => a + (o.op === 'give' && o.item === 'egg' ? o.n : 0), Number(state['eggs'] ?? 0));
    return { state: { ...state, simAt: now, eggs }, nextWake: now + 600, summary: { netWorth: 0, level: 1, xp: 0 }, done: [] };
  }
}

describe('mintable goods (shadow mode)', () => {
  const seen = (gain: Record<string, number>, stock: Record<string, number>, room = 1_000): EngineEarned => ({ gain, stock, room });

  it('adds growth the engine vouched for, and never more than is in store', () => {
    expect(nextEarned(undefined, seen({ wheat: 40 }, { wheat: 40 }))).toEqual({ wheat: 40 });
    expect(nextEarned({ wheat: 40 }, seen({ wheat: 10 }, { wheat: 50 }))).toEqual({ wheat: 50 });
    // sold 30 since: only what is left can still be minted
    expect(nextEarned({ wheat: 50 }, seen({}, { wheat: 20 }))).toEqual({ wheat: 20 });
    expect(nextEarned({ wheat: 50 }, seen({}, {}))).toEqual({});
  });

  it('counts growth only while the earlier island had room for it', () => {
    // a level-1 farmhouse holds 150: 100 already mintable leaves room for 50 more, taken in name order
    expect(nextEarned({ wheat: 100 }, seen({ egg: 30, milk: 30 }, { wheat: 100, egg: 30, milk: 30 }, 150))).toEqual({ wheat: 100, egg: 30, milk: 20 });
    expect(nextEarned({ wheat: 200 }, seen({ egg: 30 }, { wheat: 200, egg: 30 }, 150))).toEqual({ wheat: 200 });
  });

  it("counts the server's own gifts in full", () => {
    const ops: ExchangeOp[] = [{ op: 'give', item: 'wheat', n: 300 }, { op: 'credit', n: 9 }, { op: 'hold', id: 'barn', level: 1, price: 0 }];
    expect(plusGiven({ wheat: 5 }, ops)).toEqual({ wheat: 305 });
  });

  function earning() {
    const w = new World();
    const engine = new EarningEngine();
    const deps: IslandDeps = { env: w.env, engine, wakes: { schedule: async () => undefined } };
    const doc = () => w.store.get(paths.island(FARMER)) as unknown as IslandDoc;
    const upload = (wood: number) => JSON.stringify({ rulesVersion: 1, simAt: w.clock.nowMs() / 1000, buildings: [], wood, economy: { now: w.clock.nowMs() / 1000 } });
    return { w, engine, deps, doc, upload };
  }

  it("a first upload's goods never mint; goods grown after it do", async () => {
    const { w, deps, doc, upload } = earning();
    await sync(deps, FARMER, upload(500), true);
    expect(doc().earned).toBeUndefined();
    w.clock.advance(10 * MINUTE);
    await sync(deps, FARMER, upload(550), true);
    expect(doc().earned).toEqual({ wood: 50 });
  });

  it('a save that jumps earns only what the island could have made in the time', async () => {
    const { w, deps, doc, upload } = earning();
    await sync(deps, FARMER, upload(0), true);
    w.clock.advance(MINUTE);
    await sync(deps, FARMER, upload(5_000), true);
    expect(doc().earned).toEqual({ wood: 10 });
    expect(doc().audit).toBeUndefined();
  });

  it('goods the server gives (redeems, refunds) are mintable as they land', async () => {
    const { w, deps, doc, upload } = earning();
    await sync(deps, FARMER, upload(0), true);
    w.patch(paths.grants(FARMER), { items: [{ id: 'refund_x', ops: [{ op: 'give', item: 'egg', n: 7 }], at: w.clock.nowMs() }], updatedAt: w.clock.nowMs() });
    w.clock.advance(MINUTE);
    const res = await sync(deps, FARMER);
    expect(res.serverChanged).toBe(true);
    // the server's own minute of work counts too (one wood)
    expect(doc().earned).toEqual({ egg: 7, wood: 1 });
  });

  it("keeps the balance when the engine can't compare (a mint still needs the goods in store)", async () => {
    const { w, engine, deps, doc, upload } = earning();
    await sync(deps, FARMER, upload(0), true);
    w.clock.advance(10 * MINUTE);
    await sync(deps, FARMER, upload(40), true);
    engine.failEarned = true;
    w.clock.advance(10 * MINUTE);
    await sync(deps, FARMER, upload(90), true);
    expect(doc().earned).toEqual({ wood: 40 });
  });
});
