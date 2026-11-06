import type { Logger } from '../../../game/src/core/logger';
import type { PrismaClient } from '../generated/prisma/client';
import type { BlockSource } from './neardata';
import { eventsIn, type ChainEvent, type NearBlock, type Watched } from './parse';
import { project, type MarketMessage } from './project';

export interface IndexerOptions {
  readonly prisma: PrismaClient;
  readonly source: BlockSource;
  readonly watched: Watched;
  readonly log: Logger;
  /** Cursor name; one per network/contract set. */
  readonly name?: string;
  /** First block to read when there is no cursor yet (the contracts' deploy block). Default: the current final block. */
  readonly startHeight?: number;
  /** Blocks per transaction. */
  readonly batch?: number;
  /** Blocks fetched at once. */
  readonly concurrency?: number;
  /** Called after each commit with what changed (never inside the transaction). */
  readonly publish?: (messages: MarketMessage[]) => void;
}

const sleep = (ms: number, signal?: AbortSignal) =>
  new Promise<void>((resolve) => {
    const t = setTimeout(resolve, ms);
    signal?.addEventListener('abort', () => (clearTimeout(t), resolve()), { once: true });
  });

/** Fetches `heights` with at most `n` requests in flight, keeping their order. */
async function fetchAll(source: BlockSource, heights: number[], n: number): Promise<(NearBlock | null)[]> {
  const out: (NearBlock | null)[] = new Array(heights.length);
  let next = 0;
  const worker = async () => {
    while (next < heights.length) {
      const i = next++;
      out[i] = await source.block(heights[i]!);
    }
  };
  await Promise.all(Array.from({ length: Math.min(n, heights.length) }, worker));
  return out;
}

/**
 * Follows final blocks and keeps the `chain` schema in step (docs 13 §9).
 *
 * Each batch is one transaction: insert the batch's events (a known id is skipped), project only
 * the new ones, move the cursor. A crash anywhere just repeats the batch. Final blocks never
 * reorganize, so there is nothing to undo. The cursor moves by compare-and-set, so a second
 * indexer started by mistake fails its batch instead of applying events twice out of order.
 */
export class ChainIndexer {
  private readonly name: string;
  private readonly batch: number;
  private readonly concurrency: number;
  private head = 0;

  constructor(private readonly o: IndexerOptions) {
    this.name = o.name ?? `items:${o.watched.items}|market:${o.watched.market}`;
    this.batch = o.batch ?? 100;
    this.concurrency = o.concurrency ?? 12;
  }

  /** The last fully applied block, creating the cursor on first run. */
  async cursor(): Promise<number> {
    const rows = await this.o.prisma.$queryRaw<{ h: string }[]>`SELECT block_height::text AS h FROM chain.cursors WHERE name = ${this.name}`;
    if (rows[0]) return Number(rows[0].h);
    const start = this.o.startHeight ?? (await this.o.source.finalHeight());
    await this.o.prisma.$executeRaw`
      INSERT INTO chain.cursors (name, block_height) VALUES (${this.name}, ${start - 1}) ON CONFLICT (name) DO NOTHING`;
    this.o.log.info('indexer cursor created', { name: this.name, startHeight: start });
    return this.cursor();
  }

  /** Applies the next batch of final blocks. Returns how many heights it covered (0 = caught up). */
  async step(): Promise<number> {
    const from = (await this.cursor()) + 1;
    if (from > this.head) this.head = await this.o.source.finalHeight();
    const to = Math.min(from + this.batch - 1, this.head);
    if (to < from) return 0;

    const heights = Array.from({ length: to - from + 1 }, (_, i) => from + i);
    const blocks = await fetchAll(this.o.source, heights, this.concurrency);
    const events = blocks.flatMap((b) => (b ? eventsIn(b, this.o.watched) : []));
    const messages = await this.apply(events, from - 1, to);
    if (events.length) this.o.log.info('indexed', { from, to, events: events.length, messages: messages.length });
    if (messages.length) this.o.publish?.(messages);
    return to - from + 1;
  }

  /** Writes a batch's events and moves the cursor from `prev` to `to`, atomically. */
  async apply(events: readonly ChainEvent[], prev: number, to: number): Promise<MarketMessage[]> {
    return this.o.prisma.$transaction(
      async (tx) => {
        const messages: MarketMessage[] = [];
        for (const e of events) {
          const inserted = await tx.$queryRaw<{ id: string }[]>`
            INSERT INTO chain.events (id, block_height, block_time, tx_hash, receipt_id, log_index, contract_id, standard, event, data)
            VALUES (${e.id}, ${e.blockHeight}, ${e.blockTime}, ${e.txHash}, ${e.receiptId}, ${e.logIndex}, ${e.contractId},
                    ${e.standard}, ${e.event}, ${JSON.stringify(e.data)}::jsonb)
            ON CONFLICT (id) DO NOTHING RETURNING id`;
          if (inserted.length) messages.push(...(await project(tx, e, this.o.watched)));
        }
        const moved = await tx.$executeRaw`
          UPDATE chain.cursors SET block_height = ${to}, updated_at = now() WHERE name = ${this.name} AND block_height = ${prev}`;
        if (moved !== 1) throw new Error(`indexer cursor ${this.name} moved underneath us (another indexer running?)`);
        return messages;
      },
      { timeout: 120_000, maxWait: 10_000 },
    );
  }

  /** Runs until `signal` aborts: catch up as fast as the source allows, then follow the chain. */
  async run(signal: AbortSignal, idleMs = 1_000): Promise<void> {
    let failures = 0;
    while (!signal.aborted) {
      try {
        const n = await this.step();
        failures = 0;
        if (n === 0) await sleep(idleMs, signal);
      } catch (error) {
        failures++;
        this.o.log.warn('indexer step failed', { error: String(error), failures });
        await sleep(Math.min(30_000, 1_000 * 2 ** Math.min(failures, 5)), signal);
      }
    }
  }
}
