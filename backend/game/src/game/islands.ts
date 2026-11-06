import type { IslandAwayReport } from '@farmverse/contracts';
import { AppError, fail } from '../core/errors';
import { paths } from '../infra/paths';
import { EngineRefusal, type EngineReport, type ExchangeOp, type IslandEngine, type IslandSave, type IslandSummary } from '../infra/islandEngine';
import { postToBoard } from './leaderboard';
import type { Env } from './services';

/**
 * Islands are never ticked. Each is a save plus the time it was simulated to; whoever
 * looks at it next (the player's `island-sync`, or a booked `island-wake`) asks the
 * engine to run it up to now. Wakes exist so the server notices things (storage full,
 * a raid due) while the player is away — they fold into one report the player sees
 * on their next sync.
 */
export interface IslandDoc {
  /** Bumped on every write; a write that read an older rev is retried. */
  rev: number;
  rulesVersion: number;
  simAtMs: number;
  /** The IslandState JSON, as the engine returned it. */
  state: string;
  nextWakeMs: number;
  /** The last time the player synced (the start of "while you were away"). */
  lastSyncMs: number;
  /** Work done by wakes since the last sync, reported and cleared on the next sync. */
  pending: PendingReport;
  /** Net worth, level and XP from the last engine run (leaderboard, profile). */
  summary?: IslandSummary;
  /** The device's own save files from the last mirror (cloud save: restored on a new phone). */
  deviceSave?: string;
  /** Set (and kept) when an upload looked implausible: minting stays locked until reviewed. */
  audit?: IslandAudit;
  updatedAt: number;
}

/** Why an upload was flagged (docs 13 §7.3). */
export interface IslandAudit {
  readonly flaggedAtMs: number;
  readonly reason: 'worth_jump' | 'first_upload_too_rich';
  readonly fromWorth: number;
  readonly toWorth: number;
  readonly allowedWorth: number;
  readonly hours: number;
}

/** Island changes the server owes the player (redeems, mint refunds), applied on their next sync. */
export interface GrantsDoc {
  items: { id: string; ops: ExchangeOp[]; at: number }[];
  updatedAt: number;
}

/**
 * The upload guard while the device is still authoritative (shadow mode). An honest island grows
 * by what its buildings make; a forged save jumps. Allowed growth per hour: a flat amount plus a
 * share of what the island was already worth (generous: honest play reaches ~6k coins/hour at
 * level 20). A flag never blocks play; it locks minting until someone looks.
 */
export const AUDIT_BASE_PER_HOUR = 20_000;
export const AUDIT_SHARE_PER_DAY = 0.5;
export const AUDIT_MIN_HOURS = 0.25;
export const AUDIT_MAX_HOURS = 72;
/** A brand-new island uploaded for the first time can't already be worth more than this. */
export const AUDIT_FIRST_WORTH = 25_000;

/** Pure: the audit of one upload, or undefined when it's plausible. */
export function auditUpload(fromWorth: number | undefined, toWorth: number, elapsedMs: number, nowMs: number): IslandAudit | undefined {
  const hours = Math.min(AUDIT_MAX_HOURS, Math.max(AUDIT_MIN_HOURS, elapsedMs / 3_600_000));
  if (fromWorth === undefined) {
    return toWorth > AUDIT_FIRST_WORTH
      ? { flaggedAtMs: nowMs, reason: 'first_upload_too_rich', fromWorth: 0, toWorth, allowedWorth: AUDIT_FIRST_WORTH, hours }
      : undefined;
  }
  const allowedWorth = Math.round(fromWorth + AUDIT_BASE_PER_HOUR * hours + fromWorth * AUDIT_SHARE_PER_DAY * (hours / 24));
  return toWorth > allowedWorth ? { flaggedAtMs: nowMs, reason: 'worth_jump', fromWorth, toWorth, allowedWorth, hours } : undefined;
}

interface PendingReport {
  workers: number;
  loads: number;
  seedCoins: number;
  storageFull: boolean;
  gathered: Record<string, number>;
  /** What each job did (workers run the farm); absent on reports from older docs. */
  jobs?: Record<string, number>;
  foodUsed?: number;
  /** Why work stopped first ('' | 'food' | 'storage') and when (seconds since 1970). */
  stopped?: string;
  stoppedAt?: number;
  capped?: boolean;
}

/** The per-job counts an engine run reports, folded into the pending report under these names. */
const JOBS = ['harvested', 'replanted', 'fed', 'collected', 'refilled', 'restocked', 'sold', 'soldCoins', 'crates'] as const;

/** Books a one-off wake for an island (Cloud Tasks in production). Duplicate bookings are harmless. */
export interface WakeScheduler {
  schedule(uid: string, atMs: number): Promise<void>;
}

export interface IslandDeps {
  readonly env: Env;
  readonly engine: IslandEngine;
  readonly wakes: WakeScheduler;
  /** A wake found the storage newly full while the player is away (a push, once). */
  readonly onStorageFull?: (uid: string) => Promise<unknown>;
  /** A wake found the workers newly out of food while the player is away (a push, once). */
  readonly onWorkersHungry?: (uid: string) => Promise<unknown>;
}

/** A wake earlier than this before the island's own nextWake is a stale booking, and is skipped. */
export const WAKE_SLACK_MS = 60_000;
/** Bound on engine-call-then-write races before giving up. */
const ATTEMPTS = 3;
/** The adopted save is the player's own device data; this caps how big it may be. */
export const MAX_SAVE_CHARS = 400_000;

const emptyPending = (): PendingReport => ({ workers: 0, loads: 0, seedCoins: 0, storageFull: false, gathered: {} });

/** Fold one engine run into the pending report (pure). */
export function fold(into: PendingReport, run: EngineReport): PendingReport {
  const gathered = { ...into.gathered };
  for (const [item, n] of Object.entries(run.gathered ?? {})) if (n > 0) gathered[item] = (gathered[item] ?? 0) + n;
  const jobs: Record<string, number> = { ...(into.jobs ?? {}) };
  for (const k of JOBS) {
    const n = run[k];
    if (typeof n === 'number' && n > 0) jobs[k] = (jobs[k] ?? 0) + n;
  }
  const first = into.stopped ? { stopped: into.stopped, stoppedAt: into.stoppedAt ?? 0 } : run.stopped ? { stopped: run.stopped, stoppedAt: run.stoppedAt ?? 0 } : {};
  return {
    workers: Math.max(into.workers, run.workers),
    loads: into.loads + run.loads,
    seedCoins: into.seedCoins + run.seedCoins,
    storageFull: run.storageFull,
    gathered,
    ...(Object.keys(jobs).length > 0 ? { jobs } : {}),
    ...((into.foodUsed ?? 0) + (run.foodUsed ?? 0) > 0 ? { foodUsed: Math.round(((into.foodUsed ?? 0) + (run.foodUsed ?? 0)) * 100) / 100 } : {}),
    ...first,
    ...(into.capped || run.capped ? { capped: true } : {}),
  };
}

function toReport(p: PendingReport, awayMs: number): IslandAwayReport {
  return {
    awaySeconds: Math.max(0, awayMs / 1000),
    workers: p.workers,
    loads: p.loads,
    seedCoins: p.seedCoins,
    storageFull: p.storageFull,
    gathered: Object.entries(p.gathered)
      .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
      .slice(0, 64)
      .map(([item, count]) => ({ item, count })),
    ...Object.fromEntries(JOBS.filter((k) => (p.jobs?.[k] ?? 0) > 0).map((k) => [k, Math.round(p.jobs![k]!)])),
    ...(p.foodUsed ? { foodUsed: p.foodUsed } : {}),
    ...(p.stopped ? { stopped: p.stopped, stoppedAt: p.stoppedAt ?? 0 } : {}),
    ...(p.capped ? { capped: true } : {}),
  };
}

/** Parse a device save for adoption; anything that is not an island save is refused. */
export function parseAdopted(json: string, nowMs: number): IslandSave {
  if (json.length > MAX_SAVE_CHARS) fail('INVALID_ARGUMENT');
  let raw: unknown;
  try {
    raw = JSON.parse(json);
  } catch {
    fail('INVALID_ARGUMENT');
  }
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) fail('INVALID_ARGUMENT');
  const save = raw as Record<string, unknown>;
  if (!Number.isInteger(save['rulesVersion']) || (save['rulesVersion'] as number) < 1) fail('INVALID_ARGUMENT');
  if (!Array.isArray(save['buildings'])) fail('INVALID_ARGUMENT');
  // The device's clock is not trusted: an adopted island starts "now", with no time owed.
  // (The engine counts away time from the economy's own clock, so that moves too.)
  const now = nowMs / 1000;
  const economy = save['economy'];
  const restamped = typeof economy === 'object' && economy !== null ? { ...(economy as object), now } : economy;
  return { ...save, rulesVersion: save['rulesVersion'] as number, simAt: now, economy: restamped };
}

/** The longest a mirrored (shadow) island may claim to have been away. */
export const MAX_MIRROR_AWAY_MS = 7 * 24 * 3_600_000;

/** Parse a device save for shadow mode: its own clock is kept (clamped), so both sides run the same span. */
export function parseMirrored(json: string, nowMs: number): IslandSave {
  const save = parseAdopted(json, nowMs) as IslandSave & { economy?: { now?: number } };
  const raw = JSON.parse(json) as { simAt?: unknown; economy?: { now?: unknown } };
  const clamp = (t: unknown) => Math.min(nowMs / 1000, Math.max((nowMs - MAX_MIRROR_AWAY_MS) / 1000, typeof t === 'number' && Number.isFinite(t) ? t : nowMs / 1000));
  const economy = save.economy ? { ...save.economy, now: clamp(raw.economy?.now) } : save.economy;
  return { ...save, simAt: clamp(raw.simAt), economy };
}

async function run(engine: IslandEngine, save: IslandSave, nowMs: number, adopting: boolean) {
  try {
    return await engine.advance(save, nowMs / 1000);
  } catch (e) {
    if (e instanceof EngineRefusal) {
      // On adoption the save is the client's: newer rules than the server runs, or a bad save.
      if (adopting) throw new AppError(e.status === 409 ? 'CONFLICT' : 'INVALID_ARGUMENT', {}, { cause: e });
      // A stored save the engine refuses is a deploy problem (engine older than the data).
      throw new AppError('INTERNAL', {}, { cause: e });
    }
    throw e;
  }
}

/**
 * Run the stored island up to now and write it back, retrying if someone else wrote
 * first. `onWrite` decides the pending report and lastSync for the new doc.
 */
async function advanceStored(
  deps: IslandDeps,
  uid: string,
  adopt: string | undefined,
  mirror: boolean,
  device: string | undefined,
  finish: (before: IslandDoc | undefined, run: EngineReport, nowMs: number) => Pick<IslandDoc, 'pending' | 'lastSyncMs'>,
  applyGrants = false,
): Promise<{ doc: IslandDoc; before: IslandDoc | undefined; serverChanged: boolean } | undefined> {
  const { env } = deps;
  const path = paths.island(uid);
  for (let attempt = 0; attempt < ATTEMPTS; attempt++) {
    const nowMs = env.clock.nowMs();
    const snap = await env.store.read(path);
    const before = snap.exists ? (snap.data as unknown as IslandDoc) : undefined;
    let save: IslandSave;
    if (mirror && adopt !== undefined) save = parseMirrored(adopt, nowMs);
    else if (before) save = JSON.parse(before.state) as IslandSave;
    else if (adopt !== undefined) save = parseAdopted(adopt, nowMs);
    else return undefined;
    const fromDevice = mirror || !before;
    // the Farm Pass's longer offline shift is the server's to grant, not the device's
    save = await withPass(env, uid, save);
    // Never run backwards (a skewed instance clock): the engine refuses now < simAt.
    const at = Math.max(nowMs, Math.ceil(save.simAt * 1000));
    let out = await run(deps.engine, save, at, fromDevice && adopt !== undefined);

    // Shadow-mode guard: compare what the device uploaded with what the server last held.
    let audit = before?.audit;
    if (fromDevice && adopt !== undefined && out.summary && !audit) {
      const elapsed = before ? at - before.updatedAt : 0;
      audit = auditUpload(before?.summary?.netWorth, out.summary.netWorth, elapsed, at);
      if (audit) env.log.warn('island upload flagged', { uid, ...audit });
    }

    // What the server owes the island (redeems, refunds): only while the player is here to load it.
    let applied: string[] = [];
    if (applyGrants && deps.engine.exchange) {
      const grants = await env.store.read(paths.grants(uid));
      const items = grants.exists ? ((grants.data as unknown as GrantsDoc).items ?? []) : [];
      if (items.length > 0) {
        try {
          const ex = await deps.engine.exchange(out.state, at / 1000, items.flatMap((g) => g.ops));
          out = { ...out, state: ex.state, nextWake: ex.nextWake, summary: ex.summary };
          applied = items.map((g) => g.id);
        } catch (e) {
          // e.g. no farmhouse to store goods in: they stay owed until the island can take them
          env.log.warn('grants not applied', { uid, error: String(e) });
        }
      }
    }

    const doc: IslandDoc = {
      rev: (before?.rev ?? 0) + 1,
      rulesVersion: out.state.rulesVersion,
      simAtMs: Math.round(out.state.simAt * 1000),
      state: JSON.stringify(out.state),
      nextWakeMs: Math.round(out.nextWake * 1000),
      ...(out.summary ? { summary: out.summary } : {}),
      ...(audit ? { audit } : {}),
      ...(mirror && device !== undefined ? { deviceSave: device } : before?.deviceSave ? { deviceSave: before.deviceSave } : {}),
      ...finish(before, out.report, at),
      updatedAt: at,
    };
    const written = await env.store.runTransaction(async (tx) => {
      const now = await tx.get(path);
      const owed = applied.length > 0 ? await tx.get(paths.grants(uid)) : undefined;
      const rev = now.exists ? Number((now.data as unknown as IslandDoc).rev) : 0;
      if (rev !== (before?.rev ?? 0)) return false;
      tx.set(path, doc as unknown as Record<string, unknown>);
      if (owed?.exists) {
        // drop exactly what was applied (a grant added meanwhile stays owed)
        const left = ((owed.data as unknown as GrantsDoc).items ?? []).filter((g) => !applied.includes(g.id));
        tx.set(paths.grants(uid), { items: left, updatedAt: at });
      }
      return true;
    });
    if (written) return { doc, before, serverChanged: applied.length > 0 };
    env.log.info('island write raced; retrying', { uid, attempt });
  }
  fail('CONFLICT');
}

/** Sets the save's `workPass` from the player's pass doc (premium bought this season). */
async function withPass(env: Env, uid: string, save: IslandSave): Promise<IslandSave> {
  const economy = save['economy'];
  if (typeof economy !== 'object' || economy === null) return save;
  let premium = false;
  try {
    const pass = await env.store.read(paths.pass(uid));
    premium = pass.exists && (pass.data as { premiumActive?: unknown }).premiumActive === true;
  } catch {
    premium = false;
  }
  if ((economy as { workPass?: unknown }).workPass === premium) return save;
  return { ...save, economy: { ...(economy as object), workPass: premium } };
}

/** Wakes stop this long after the player's last sync; the island then waits for them to come back. */
export const WAKE_HORIZON_MS = 3 * 24 * 3_600_000;
/** The soonest a wake is booked after the run that booked it. */
const MIN_WAKE_GAP_MS = 60_000;

async function book(deps: IslandDeps, uid: string, doc: IslandDoc): Promise<void> {
  const at = Math.max(doc.nextWakeMs, doc.updatedAt + MIN_WAKE_GAP_MS);
  if (at - doc.lastSyncMs > WAKE_HORIZON_MS) return;
  try {
    await deps.wakes.schedule(uid, at);
  } catch (e) {
    // A missed wake only delays notices; the next sync still catches the island up.
    deps.env.log.warn('island wake not booked', { uid, error: String(e) });
  }
}

/** `island-sync`: the island as of now, plus everything that happened since the last sync. */
export async function sync(deps: IslandDeps, uid: string, adopt?: string, mirror = false, device?: string) {
  const mirroring = mirror && adopt !== undefined;
  let ranSeconds = 0;
  const result = await advanceStored(
    deps,
    uid,
    adopt,
    mirroring,
    device,
    (before, report, nowMs) => {
      ranSeconds = report.seconds;
      return {
        // a mirrored run reports only its own span: the device's copy replaced whatever wakes did
        pending: fold(mirroring ? emptyPending() : before?.pending ?? emptyPending(), report),
        lastSyncMs: nowMs,
      };
    },
    true,
  );
  if (!result) fail('NOT_FOUND');
  const { doc, before } = result;
  // Shadow islands are replaced on every sync, so waking them would be wasted work.
  if (!mirroring) await book(deps, uid, doc);
  await postToBoard(deps.env, uid, doc.summary);
  const awayMs = mirroring ? ranSeconds * 1000 : before ? doc.updatedAt - before.lastSyncMs : 0;
  const response = {
    state: doc.state,
    rulesVersion: doc.rulesVersion,
    report: toReport(doc.pending, awayMs),
    nextWakeMs: doc.nextWakeMs,
    ...(result.serverChanged ? { serverChanged: true } : {}),
  };
  // The report is delivered: clear it without another engine run (a rev-checked write;
  // losing the race just means the next sync shows it again).
  await deps.env.store.runTransaction(async (tx) => {
    const now = await tx.get(paths.island(uid));
    if (!now.exists || (now.data as unknown as IslandDoc).rev !== doc.rev) return;
    // a whole write, not a merge: a merge keeps the old report's goods and jobs (maps merge key by key)
    tx.set(paths.island(uid), { ...(now.data as Record<string, unknown>), pending: emptyPending() });
  });
  return response;
}

export type WakeOutcome = 'missing' | 'early' | 'advanced';

/** `island-wake`: a booked look at an island while its player is away. */
export async function wake(deps: IslandDeps, uid: string): Promise<WakeOutcome> {
  const snap = await deps.env.store.read(paths.island(uid));
  if (!snap.exists) return 'missing';
  const stored = snap.data as unknown as IslandDoc;
  // Another sync or wake already moved it on and booked its own wake.
  if (stored.nextWakeMs > deps.env.clock.nowMs() + WAKE_SLACK_MS) return 'early';
  const result = await advanceStored(deps, uid, undefined, false, undefined, (before, report) => ({
    pending: fold(before?.pending ?? emptyPending(), report),
    lastSyncMs: before?.lastSyncMs ?? deps.env.clock.nowMs(),
  }));
  if (!result) return 'missing';
  if (result.doc.pending.storageFull && !result.before?.pending.storageFull && deps.onStorageFull) {
    try {
      await deps.onStorageFull(uid);
    } catch (e) {
      deps.env.log.warn('storage push failed', { uid, error: String(e) });
    }
  }
  if (result.doc.pending.stopped === 'food' && result.before?.pending.stopped !== 'food' && deps.onWorkersHungry) {
    try {
      await deps.onWorkersHungry(uid);
    } catch (e) {
      deps.env.log.warn('workers push failed', { uid, error: String(e) });
    }
  }
  await book(deps, uid, result.doc);
  await postToBoard(deps.env, uid, result.doc.summary);
  return 'advanced';
}
