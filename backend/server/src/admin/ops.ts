import { z } from 'zod';
import type { Logger } from '../../../functions/src/core/logger';
import type { Store } from '../../../functions/src/infra/store';

/** `config/ops` (server only): the switches the admin web changes without a deploy. */
export const OPS_PATH = 'config/ops';

export const Version = z.string().regex(/^\d{1,4}\.\d{1,4}\.\d{1,6}$/, 'a version like 1.4.0');

export const OpsDoc = z.object({
  /** While on, the game shows its maintenance screen and every game call is refused (admins excepted). */
  maintenance: z.boolean(),
  /** What players are told while maintenance is on ('' = the game's own words). */
  message: z.string().max(200),
  /** Clients older than this are asked to update ('' = the server's MIN_CLIENT_VERSION). */
  minClientVersion: z.union([Version, z.literal('')]),
  updatedAt: z.number().int().min(0),
  /** The admin account that last changed it. */
  updatedBy: z.string().max(64),
});
export type OpsDoc = z.infer<typeof OpsDoc>;

export interface Ops {
  readonly maintenance: boolean;
  readonly message: string;
  readonly minClientVersion: string;
}

/**
 * The live ops switches, read from `config/ops` and kept in memory so every request can check them
 * without a database read. Reloaded every few seconds (and at once after an admin writes), so a
 * change reaches every server process within {@link refreshMs}.
 */
export class OpsConfig {
  private doc: OpsDoc | undefined;
  private timer: NodeJS.Timeout | undefined;

  constructor(
    private readonly store: Store,
    private readonly fallbackMinVersion: string,
    private readonly log: Logger,
    readonly refreshMs = 10_000,
  ) {}

  async start(): Promise<void> {
    await this.reload();
    this.timer = setInterval(() => void this.reload(), this.refreshMs);
    this.timer.unref();
  }

  stop(): void {
    if (this.timer) clearInterval(this.timer);
  }

  async reload(): Promise<void> {
    try {
      const snap = await this.store.read(OPS_PATH);
      if (!snap.exists) {
        this.doc = undefined;
        return;
      }
      const parsed = OpsDoc.safeParse(snap.data);
      if (parsed.success) this.doc = parsed.data;
      else this.log.error('config/ops failed its schema; keeping the last good copy', { issues: parsed.error.issues.slice(0, 5) });
    } catch (error) {
      this.log.error('config/ops not read', { error: String(error) });
    }
  }

  /** What is in force now. */
  get current(): Ops {
    return {
      maintenance: this.doc?.maintenance ?? false,
      message: this.doc?.message ?? '',
      minClientVersion: this.doc?.minClientVersion || this.fallbackMinVersion,
    };
  }

  /** The stored document, for the admin web (undefined when nothing was ever saved). */
  get stored(): OpsDoc | undefined {
    return this.doc;
  }

  get fallbackMinClientVersion(): string {
    return this.fallbackMinVersion;
  }
}
