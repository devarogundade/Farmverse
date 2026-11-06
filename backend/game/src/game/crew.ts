import type { Env } from './services';

/**
 * Farmhands (plan 15 §7): worker NFTs work on the island of whoever holds them (or rents them). The
 * server reads the crew from the chain on every sync and writes it into the island save; the island
 * engine gives them beds and work. XP is earned on the server's own runs of the island, per token, so
 * a Farmhand keeps its level when it's sold.
 */

/** As the C# `Farmhand` (Domain/Gameplay/IslandEconomy.Crew.cs). */
export interface Farmhand {
  token: string;
  name: string;
  specialty: string;
  speed: number;
  carry: number;
  stamina: number;
  level: number;
  rented: boolean;
}

/** `workers/{tokenId}`: a Farmhand's experience, whoever holds it. */
export interface WorkerStatsDoc {
  xp: number;
  level: number;
  updatedAt: number;
}

export const FARMHAND_MAX_LEVEL = 10;
/** As GameplayRules.MaxCrew. */
export const MAX_CREW = 6;
export const workerPath = (tokenId: string) => `workers/${tokenId}`;

/** Level n needs 100 × n XP more than level n − 1: 100, 300, 600 … (plan 15 §7.4). */
export function levelFor(xp: number): number {
  let level = 1;
  let need = 100;
  let left = xp;
  while (level < FARMHAND_MAX_LEVEL && left >= need) {
    left -= need;
    level++;
    need = 100 * level;
  }
  return level;
}

/** Puts the crew into an island save's economy (returns the same object when nothing changed). */
export function withCrewSave<T extends Record<string, unknown>>(save: T, crew: readonly Farmhand[] | undefined): T {
  if (!crew) return save;
  const economy = save['economy'];
  if (typeof economy !== 'object' || economy === null) return save;
  const now = JSON.stringify((economy as { crew?: unknown }).crew ?? []);
  if (now === JSON.stringify(crew)) return save;
  return { ...save, economy: { ...(economy as object), crew } };
}

/** Gives each working Farmhand the loads of a server run, shared out by hands (whole XP only). */
export async function awardCrewXp(env: Env, crew: readonly Farmhand[] | undefined, workers: number, loads: number): Promise<void> {
  if (!crew || crew.length === 0 || loads <= 0 || workers <= 0) return;
  const each = Math.floor(loads / workers);
  if (each <= 0) return;
  const nowMs = env.clock.nowMs();
  // Beds go in token order and never past MAX_CREW: those are the ones who can have worked.
  const working = [...crew].sort((a, b) => (a.token < b.token ? -1 : a.token > b.token ? 1 : 0)).slice(0, Math.min(MAX_CREW, workers));
  await env.store.runTransaction(async (tx) => {
    const snaps = await Promise.all(working.map((f) => tx.get(workerPath(f.token))));
    working.forEach((f, i) => {
      const before = snaps[i]?.data as WorkerStatsDoc | undefined;
      const xp = (before?.xp ?? 0) + each;
      tx.set(workerPath(f.token), { xp, level: levelFor(xp), updatedAt: nowMs });
    });
  });
}
