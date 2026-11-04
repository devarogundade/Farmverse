import { describe, expect, it } from 'vitest';
import { nudgeTask, notePing, ping, COMEBACK_GEMS } from '../../src/game/engagement';
import { atLocal, gate, preferredHour, readPresence, sendDirect, type PresenceDoc } from '../../src/game/notify';
import { finishRaid, raidTask, type PushTask, type RaidDoc } from '../../src/game/raids';
import type { PushMessage } from '../../src/game/push';
import type { EngineRaidRun, IslandEngine, IslandSave } from '../../src/infra/islandEngine';
import { paths } from '../../src/infra/paths';
import { DAY, FARMER, HOUR, MAKER, MINUTE, World } from './world';
import { fanOutNews, newsText, shortBody } from '../../src/game/news';
import { levelGems } from '../../src/game/levels';

class FakeEngine implements IslandEngine {
  raids = 0;
  async advance(): Promise<never> {
    throw new Error('not used');
  }
  async raid(state: IslandSave, now: number): Promise<EngineRaidRun> {
    this.raids++;
    return {
      state: { ...state, simAt: now },
      raid: { size: 3, ratio: 0.2, won: false, stolen: [{ item: 'wheat', n: 7 }], coins: 40, trampled: [12], ruined: [] },
    };
  }
}

async function setup() {
  const w = new World();
  await w.createPlayer(FARMER);
  w.store.put(paths.pushToken(FARMER, 'install1'), { token: 'tok1', updatedAt: w.clock.nowMs() });
  const sent: PushMessage[] = [];
  const booked: { task: PushTask; atMs: number }[] = [];
  const engine = new FakeEngine();
  const deps = {
    env: w.env,
    engine,
    messenger: { send: async (m: readonly PushMessage[]) => (sent.push(...m), []) },
    tasks: { schedule: async (_uid: string, task: PushTask, atMs: number) => void booked.push({ task, atMs }) },
  };
  const raid = () => w.store.get(paths.raid(FARMER)) as unknown as RaidDoc;
  return { w, deps, sent, booked, engine, raid };
}

const island = (buildings: number) => ({
  rev: 1,
  state: JSON.stringify({ rulesVersion: 1, simAt: 0, seed: 1, buildings: Array.from({ length: buildings }, (_, i) => ({ id: 'field', uid: i + 1 })) }),
});

describe('push gate', () => {
  const settings = { pushEnabled: true, quietStart: 22, quietEnd: 8 };
  const log = { day: '', count: 0, last: {}, offerMs: 0 };
  const away: PresenceDoc = { seenMs: 0, sessionMs: 0, offsetMin: 0, hours: new Array(24).fill(0), updatedAt: 0 };
  const noon = Date.UTC(2026, 9, 21, 12, 0);

  it('needs the opt-in and the topic switched on', () => {
    expect(gate('daily_ready', noon, { ...settings, pushEnabled: false }, away, log)).toBe('disabled');
    expect(gate('daily_ready', noon, { ...settings, pushTopics: { rewards: false } }, away, log)).toBe('topic_off');
    expect(gate('daily_ready', noon, settings, away, log)).toBeUndefined();
  });

  it('never pushes while the game is open or in quiet hours (local time)', () => {
    expect(gate('daily_ready', noon, settings, { ...away, seenMs: noon - MINUTE }, log)).toBe('online');
    expect(gate('daily_ready', Date.UTC(2026, 9, 21, 23, 0), settings, away, log)).toBe('quiet');
    // 23:00 UTC is 08:00 in Tokyo: fine there
    expect(gate('daily_ready', Date.UTC(2026, 9, 21, 23, 0), settings, { ...away, offsetMin: 540 }, log)).toBeUndefined();
  });

  it('caps the day, spaces each kind and offers, and lets raid warnings through the cap', () => {
    const today = '2026-10-21';
    expect(gate('likes', noon, settings, away, { ...log, day: today, count: 3 })).toBe('capped');
    expect(gate('raid_soon', noon, settings, away, { ...log, day: today, count: 3 })).toBeUndefined();
    expect(gate('likes', noon, settings, away, { ...log, last: { likes: noon - HOUR } })).toBe('cooldown');
    expect(gate('starter_ending', noon, settings, away, { ...log, offerMs: noon - DAY })).toBe('cooldown');
  });

  it('plans for the hour the player usually plays, outside quiet hours', () => {
    const hours = new Array(24).fill(0);
    expect(preferredHour({ ...away, hours }, settings)).toBe(19);
    hours[13] = 4;
    hours[23] = 9; // too late, and quiet
    expect(preferredHour({ ...away, hours }, settings)).toBe(13);
  });

  it('counts a new session in its local hour', () => {
    const first = notePing(undefined, noon, 60);
    expect(first.newSession).toBe(true);
    expect(first.presence.hours[13]).toBe(1);
    const again = notePing(first.presence, noon + 5 * MINUTE, 60);
    expect(again.newSession).toBe(false);
    expect(again.presence.hours[13]).toBe(1);
  });
});

describe('sendDirect', () => {
  it('sends in the player\'s language with the route, and counts it', async () => {
    const { w, deps, sent } = await setup();
    w.patch(paths.settings(FARMER), { locale: 'fr' });
    w.clock.advance(4 * HOUR); // 12:00 UTC
    expect(await sendDirect(w.env, deps.messenger, FARMER, 'storage_full')).toBe('sent');
    expect(sent[0]).toMatchObject({ token: 'tok1', title: 'Entrepôt plein', data: { route: 'storage', kind: 'storage_full' }, collapseKey: 'storage_full' });
    expect(w.store.get(paths.pushLog(FARMER))).toMatchObject({ count: 1 });
    expect(await sendDirect(w.env, deps.messenger, FARMER, 'storage_full')).toBe('cooldown');
  });
});

describe('the daily raid', () => {
  it('plans today at the usual hour and books three warnings and the raid', async () => {
    const { w, deps, booked } = await setup();
    const res = await ping(deps, FARMER, 0, false);
    expect(res.raid).toMatchObject({ day: '2026-10-21', done: false, by: 'none', result: null });
    const due = res.raid.dueMs;
    expect(due).toBeGreaterThanOrEqual(atLocal('2026-10-21', 19, 0, 0));
    expect(booked.filter((b) => b.task.kind === 'raid').map((b) => [b.task.stage, due - b.atMs])).toEqual([
      ['w60', 60 * MINUTE],
      ['w30', 30 * MINUTE],
      ['w5', 5 * MINUTE],
      ['due', 0],
    ]);
    // the same day again: same plan, nothing booked again
    const n = booked.length;
    w.clock.advance(MINUTE);
    expect((await ping(deps, FARMER, 0, false)).raid.dueMs).toBe(due);
    expect(booked.length).toBe(n);
  });

  it('warns an away player, then runs the raid on the server and keeps the losses for the game', async () => {
    const { w, deps, sent, raid, engine } = await setup();
    w.store.put(paths.island(FARMER), island(6));
    const { raid: plan } = await ping(deps, FARMER, 0, false);
    w.clock.set(plan.dueMs - 60 * MINUTE);
    expect(await raidTask(deps, FARMER, 'w60', plan.day)).toBe('warned');
    expect(sent.at(-1)).toMatchObject({ title: 'Raiders spotted!', body: expect.stringContaining('60 min'), collapseKey: 'raid' });
    w.clock.set(plan.dueMs - 5 * MINUTE);
    expect(await raidTask(deps, FARMER, 'w5', plan.day)).toBe('warned');
    w.clock.set(plan.dueMs);
    expect(await raidTask(deps, FARMER, 'due', plan.day)).toBe('raided');
    expect(engine.raids).toBe(1);
    expect(raid()).toMatchObject({ done: true, by: 'server', result: { size: 3, won: false, coins: 40, stolen: [{ item: 'wheat', n: 7 }] } });
    expect(raid().next?.day).toBe('2026-10-22');
    expect(sent.at(-1)).toMatchObject({ title: 'Your farm was raided' });
    // a second run of the same task does nothing
    expect(await raidTask(deps, FARMER, 'due', plan.day)).toBe('done');

    // the game comes back: it gets the result, applies it, acks it
    w.clock.advance(HOUR);
    expect((await ping(deps, FARMER, 0, false)).raid.result).not.toBeNull();
    expect((await ping(deps, FARMER, 0, true)).raid.result).toBeNull();
  });

  it('leaves the raid to the game while the player is playing, and looks again later', async () => {
    const { w, deps, booked, engine } = await setup();
    w.store.put(paths.island(FARMER), island(6));
    const { raid: plan } = await ping(deps, FARMER, 0, false);
    w.clock.set(plan.dueMs);
    await ping(deps, FARMER, 0, false);
    expect(await raidTask(deps, FARMER, 'due', plan.day)).toBe('online');
    expect(booked.at(-1)).toMatchObject({ task: { kind: 'raid', stage: 'due' }, atMs: plan.dueMs + 10 * MINUTE });
    expect(engine.raids).toBe(0);
    // the game ran it: settled, tomorrow booked, later tasks do nothing
    const done = await finishRaid(deps, FARMER, plan.day, 'won');
    expect(done.raid).toMatchObject({ done: true, by: 'client' });
    w.clock.advance(10 * MINUTE);
    expect(await raidTask(deps, FARMER, 'due', plan.day)).toBe('done');
  });

  it('skips small islands and shielded ones', async () => {
    const { w, deps, engine } = await setup();
    w.store.put(paths.island(FARMER), island(2));
    const { raid: plan } = await ping(deps, FARMER, 0, false);
    w.clock.set(plan.dueMs);
    expect(await raidTask(deps, FARMER, 'due', plan.day)).toBe('skipped');

    const b = await setup();
    b.w.store.put(paths.island(FARMER), island(6));
    const p2 = (await ping(b.deps, FARMER, 0, false)).raid;
    b.w.store.put(paths.inventory(FARMER), { boosts: {}, activeUntil: { shield: p2.dueMs + HOUR }, updatedAt: 0 });
    b.w.clock.set(p2.dueMs);
    expect(await raidTask(b.deps, FARMER, 'due', p2.day)).toBe('shield');
    expect(b.engine.raids + engine.raids).toBe(0);
  });

  it('runs tomorrow\'s raid even if the player never opened the game in between', async () => {
    const { w, deps } = await setup();
    w.store.put(paths.island(FARMER), island(6));
    const { raid: plan } = await ping(deps, FARMER, 0, false);
    w.clock.set(plan.dueMs);
    await raidTask(deps, FARMER, 'due', plan.day);
    const next = (w.store.get(paths.raid(FARMER)) as unknown as RaidDoc).next!;
    w.clock.set(next.dueMs);
    expect(await raidTask(deps, FARMER, 'due', next.day)).toBe('raided');
  });
});

describe('nudges', () => {
  it('reminds about the daily reward only when unclaimed', async () => {
    const { w, deps, sent } = await setup();
    await ping(deps, FARMER, 0, false);
    w.clock.advance(DAY + 11 * HOUR); // next day 19:00
    expect(await nudgeTask(deps, FARMER, 'daily')).toBe('sent');
    expect(sent.at(-1)).toMatchObject({ title: 'Day 1 reward is ready', data: { route: 'daily' } });
    w.patch(paths.daily(FARMER), { loginOn: '2026-10-22', loginDay: 2 });
    expect(await nudgeTask(deps, FARMER, 'daily')).toBe('not_needed');
  });

  it('climbs the comeback ladder only for players really away, and welcomes them back with gems', async () => {
    const { w, deps, sent } = await setup();
    await ping(deps, FARMER, 0, false);
    w.clock.advance(DAY);
    expect(await nudgeTask(deps, FARMER, 'comeback', '3')).toBe('not_needed');
    w.clock.advance(6 * DAY + 4 * HOUR);
    expect(await nudgeTask(deps, FARMER, 'comeback', '7')).toBe('sent');
    expect(sent.at(-1)?.body).toContain(String(COMEBACK_GEMS));
    await ping(deps, FARMER, 0, false);
    const mail = w.store.paths(paths.notifications(FARMER) + '/').map((p) => w.store.get(p)!);
    expect(mail.some((m) => (m['params'] as Record<string, string>)['gems'] === String(COMEBACK_GEMS))).toBe(true);
  });

  it('nudges about the starter pack once, before it ends', async () => {
    const { w, deps, sent } = await setup();
    w.clock.set(Date.UTC(2026, 9, 22, 12, 0));
    w.patch(paths.shop(FARMER), { starterEndsAt: w.clock.nowMs() + 3 * HOUR });
    expect(await nudgeTask(deps, FARMER, 'starter')).toBe('not_needed'); // never pinged: no presence yet
    await ping(deps, FARMER, 0, false);
    w.clock.advance(5 * MINUTE);
    expect(await nudgeTask(deps, FARMER, 'starter')).toBe('sent');
    expect(sent.at(-1)?.title).toBe('Starter Pack ends soon');
  });

  it('reads presence safely', () => {
    expect(readPresence(undefined)).toBeUndefined();
    expect(readPresence({ seenMs: 5 })?.hours).toHaveLength(24);
  });
});

describe('news pushes', () => {
  const item = (extra: Record<string, unknown> = {}) => ({
    title: 'Winter is coming',
    body: 'Stock up on bread: everyone eats more in winter.',
    i18n: { fr: { title: "L'hiver arrive", body: 'Fais des réserves de pain.' } },
    push: true,
    publishedAt: Date.UTC(2026, 9, 21, 8, 0),
    ...extra,
  });

  it('books one push for each recent player who wants news, then sends it in their language', async () => {
    const { w, deps, booked, sent } = await setup();
    await ping(deps, FARMER, 0, false); // seen today
    w.patch(paths.settings(FARMER), { locale: 'fr' });
    await w.createPlayer(MAKER);
    w.patch(paths.player(MAKER), { lastSeenAt: w.clock.nowMs() - 40 * DAY }); // long gone
    expect(await fanOutNews(deps, 'n1', item())).toBe(1);
    const task = booked.find((b) => b.task.kind === 'news')!;
    expect(task.task).toEqual({ kind: 'news', stage: 'n1' });
    w.store.put(paths.newsItem('n1'), item());
    w.clock.set(task.atMs);
    expect(await nudgeTask(deps, FARMER, 'news', 'n1')).toBe('sent');
    expect(sent.at(-1)).toMatchObject({ title: "L'hiver arrive", body: 'Fais des réserves de pain.', data: { route: 'news', kind: 'news' } });
  });

  it('books nothing for an item without push, or for players who turned News off', async () => {
    const { w, deps } = await setup();
    await ping(deps, FARMER, 0, false);
    expect(await fanOutNews(deps, 'n1', item({ push: false }))).toBe(0);
    w.patch(paths.settings(FARMER), { pushTopics: { news: false } });
    expect(await fanOutNews(deps, 'n1', item())).toBe(0);
  });

  it('drops old or removed news', async () => {
    const { w, deps } = await setup();
    await ping(deps, FARMER, 0, false);
    w.clock.advance(HOUR);
    expect(await nudgeTask(deps, FARMER, 'news', 'gone')).toBe('not_needed');
    w.store.put(paths.newsItem('old'), item());
    w.clock.advance(4 * DAY);
    expect(await nudgeTask(deps, FARMER, 'news', 'old')).toBe('not_needed');
  });

  it('cuts long bodies at a word', () => {
    expect(shortBody('a '.repeat(100), 20).length).toBeLessThanOrEqual(20);
    expect(newsText(item() as never, 'ja').title).toBe('Winter is coming');
  });
});

describe('level gems', () => {
  it('pays 2 a level, 5 at a chapter end, once, from the island mirror', async () => {
    const { w, deps } = await setup();
    w.store.put(paths.island(FARMER), { ...island(6), summary: { netWorth: 0, level: 3, xp: 0 } });
    const before = Number(w.store.get(paths.wallet(FARMER))?.['gems'] ?? 0);
    const first = await ping(deps, FARMER, 0, false);
    expect(first).toMatchObject({ levelGems: 4, levelPaid: 3 }); // levels 2 and 3
    expect(Number(w.store.get(paths.wallet(FARMER))?.['gems'])).toBe(before + 4);
    expect((await ping(deps, FARMER, 0, false)).levelGems).toBe(0);
    w.patch(paths.island(FARMER), { summary: { netWorth: 0, level: 10, xp: 0 } });
    expect((await ping(deps, FARMER, 0, false)).levelGems).toBe(6 * 2 + 5);
    expect(levelGems(51)).toBe(3);
  });
});
