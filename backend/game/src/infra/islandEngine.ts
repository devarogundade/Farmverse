/**
 * The island engine (Cloud Run, tools/dotnet/Farmverse.IslandEngine): the C# island rules
 * behind one pure call. The gateway owns storage, auth and scheduling; the engine only
 * turns "this island at simAt" into "this island at now".
 */

/** What the engine did while nobody watched (the engine's ReportDto). */
export interface EngineReport {
  readonly seconds: number;
  readonly workers: number;
  readonly loads: number;
  readonly seedCoins: number;
  readonly storageFull: boolean;
  readonly gathered: Readonly<Record<string, number>>;
  // workers run the farm (engines from rules v2 on; older ones leave these out)
  readonly harvested?: number;
  readonly replanted?: number;
  readonly fed?: number;
  readonly collected?: number;
  readonly refilled?: number;
  readonly restocked?: number;
  readonly sold?: number;
  readonly soldCoins?: number;
  readonly crates?: number;
  readonly foodUsed?: number;
  /** Why work stopped: '' | 'food' | 'storage'. */
  readonly stopped?: string;
  readonly stoppedAt?: number;
  readonly capped?: boolean;
}

export interface EngineAdvance {
  /** The island save as of `now` (IslandState). */
  readonly state: IslandSave;
  readonly report: EngineReport;
  /** Seconds since 1970 when something next changes (capped at an hour ahead). */
  readonly nextWake: number;
  /** Net worth, level and XP as the rules compute them (older engines leave it out). */
  readonly summary?: IslandSummary;
}

export interface IslandSummary {
  readonly netWorth: number;
  readonly level: number;
  readonly xp: number;
}

/** The island save, as the engine reads it. Only the fields the gateway checks are typed. */
export interface IslandSave {
  readonly rulesVersion: number;
  /** Seconds since 1970. */
  readonly simAt: number;
  readonly [field: string]: unknown;
}

/** The losses of a raid the engine ran (the engine's RaidOutcome). */
export interface EngineRaid {
  readonly size: number;
  readonly ratio: number;
  readonly won: boolean;
  readonly stolen: readonly { item: string; n: number }[];
  readonly coins: number;
  readonly trampled: readonly number[];
  readonly ruined: readonly number[];
}

export interface EngineRaidRun {
  readonly state: IslandSave;
  readonly raid: EngineRaid;
  readonly summary?: IslandSummary;
}

/** One change to an island's contents for the chain (the engine's ExchangeOp). */
export type ExchangeOp =
  | { readonly op: 'take' | 'give'; readonly item: string; readonly n: number }
  | { readonly op: 'pickUp'; readonly uid: number }
  | { readonly op: 'hold'; readonly id: string; readonly level: number; readonly price: number };

/** What an op did (the engine's ExchangeDone). */
export interface ExchangeDone {
  readonly op: string;
  readonly item: string;
  readonly n: number;
  /** Market price of one unit (goods) or the building's price. */
  readonly unitValue: number;
  readonly uid: number;
  readonly id: string;
  readonly level: number;
  readonly price: number;
}

export interface EngineExchange {
  readonly state: IslandSave;
  readonly nextWake: number;
  readonly summary: IslandSummary;
  readonly done: readonly ExchangeDone[];
}

export interface IslandEngine {
  /** Run the island from its simAt to `nowSeconds`. Throws EngineRefusal when the engine says no. */
  advance(state: IslandSave, nowSeconds: number): Promise<EngineAdvance>;
  /**
   * Run it to `nowSeconds`, then apply every op or none (mint takes, redeem gives). A refusal
   * (not enough goods, building busy …) throws EngineRefusal with status 422 and a `code`.
   */
  exchange?(state: IslandSave, nowSeconds: number, ops: readonly ExchangeOp[]): Promise<EngineExchange>;
  /** Run it to `nowSeconds`, then the day's raid (`salt`: the day number). */
  raid?(state: IslandSave, nowSeconds: number, salt: number): Promise<EngineRaidRun>;
}

/** The engine refused the island (409: newer rules; 400: bad save or clock). Not worth retrying. */
export class EngineRefusal extends Error {
  constructor(readonly status: number, message: string, readonly code: string = '') {
    super(message);
    this.name = 'EngineRefusal';
  }
}

type Fetch = typeof fetch;

const METADATA = 'http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/identity';

/**
 * An ID token for the engine's URL, from the metadata server (Cloud Run only accepts
 * callers holding run.invoker). Cached for 50 minutes; tokens live 60.
 */
export function metadataIdToken(audience: string, fetchImpl: Fetch = fetch): () => Promise<string> {
  let cached: { token: string; until: number } | undefined;
  return async () => {
    if (cached && cached.until > Date.now()) return cached.token;
    const res = await fetchImpl(`${METADATA}?audience=${encodeURIComponent(audience)}`, { headers: { 'Metadata-Flavor': 'Google' } });
    if (!res.ok) throw new Error(`metadata identity token: HTTP ${res.status}`);
    cached = { token: await res.text(), until: Date.now() + 50 * 60_000 };
    return cached.token;
  };
}

/** The engine over HTTP. A local engine (http://localhost…) is called without a token. */
export function httpEngine(baseUrl: string, options: { fetch?: Fetch; token?: () => Promise<string>; timeoutMs?: number } = {}): IslandEngine {
  const base = baseUrl.replace(/\/+$/, '');
  const fetchImpl = options.fetch ?? fetch;
  const local = /^http:\/\/(localhost|127\.0\.0\.1)/.test(base);
  const token = options.token ?? (local ? undefined : metadataIdToken(base, fetchImpl));
  async function post<T>(route: string, body: unknown): Promise<T> {
    const headers: Record<string, string> = { 'Content-Type': 'application/json' };
    if (token) headers['Authorization'] = `Bearer ${await token()}`;
    const res = await fetchImpl(`${base}${route}`, {
      method: 'POST',
      headers,
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(options.timeoutMs ?? 10_000),
    });
    if (res.status === 400 || res.status === 409 || res.status === 422) {
      const err = (await res.json().catch(() => ({}))) as { error?: string; code?: string };
      throw new EngineRefusal(res.status, err.error ?? `HTTP ${res.status}`, err.code ?? '');
    }
    if (!res.ok) throw new Error(`island engine: HTTP ${res.status}`);
    return (await res.json()) as T;
  }
  return {
    advance: (state, nowSeconds) => post<EngineAdvance>('/v1/advance', { state, now: nowSeconds }),
    raid: (state, nowSeconds, salt) => post<EngineRaidRun>('/v1/raid', { state, now: nowSeconds, salt }),
    exchange: (state, nowSeconds, ops) => post<EngineExchange>('/v1/exchange', { state, now: nowSeconds, ops }),
  };
}
