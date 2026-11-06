/**
 * Turns a NEAR block (neardata.xyz's JSON: the block plus every shard's receipt outcomes) into
 * the NEP-297 events our contracts emitted. Pure: no I/O, so it is unit tested on fixtures.
 */

/** The parts of a neardata block the indexer reads. Everything else is ignored. */
export interface NearBlock {
  block: { header: { height: number; timestamp_nanosec?: string; timestamp: number } };
  shards: Array<{
    shard_id: number;
    receipt_execution_outcomes?: Array<{
      tx_hash?: string | null;
      execution_outcome: {
        id: string;
        outcome: { executor_id: string; logs: string[]; status: unknown };
      };
      receipt: {
        receipt_id: string;
        receiver_id: string;
        predecessor_id: string;
        receipt: { Action?: { actions?: unknown[] } } | Record<string, unknown>;
      };
    }>;
  }>;
}

export type ReceiptOutcome = NonNullable<NearBlock['shards'][number]['receipt_execution_outcomes']>[number];

/** Which contracts we listen to, and which event standard each may emit. */
export interface Watched {
  readonly items: string;
  readonly market: string;
}

export interface ChainEvent {
  /** `<receipt_id>:<log index>` — unique and stable, so re-applying a block changes nothing. */
  readonly id: string;
  readonly blockHeight: number;
  readonly blockTime: Date;
  readonly txHash: string | null;
  readonly receiptId: string;
  readonly logIndex: number;
  readonly contractId: string;
  readonly standard: string;
  readonly event: string;
  /** One element of the NEP-297 `data` array (an event with several entries becomes several rows). */
  readonly data: Record<string, unknown>;
}

const PREFIX = 'EVENT_JSON:';

/** The standards each contract is trusted for. A log claiming another standard is ignored. */
function trusted(w: Watched, contractId: string, standard: string): boolean {
  if (contractId === w.items) return standard === 'nep171' || standard === 'farmverse_items';
  if (contractId === w.market) return standard === 'farmverse_market';
  return false;
}

/** Logs of a failed receipt describe changes that were rolled back: only successes count. */
export function succeeded(status: unknown): boolean {
  return typeof status === 'object' && status !== null && ('SuccessValue' in status || 'SuccessReceiptId' in status);
}

/** The function call a receipt made (the first one), with its JSON args, if any. */
export function functionCall(receipt: ReceiptOutcome): { method: string; args: Record<string, unknown> } | null {
  const action = (receipt.receipt.receipt as { Action?: { actions?: unknown[] } }).Action;
  for (const a of action?.actions ?? []) {
    const fc = (a as { FunctionCall?: { method_name: string; args: string } }).FunctionCall;
    if (!fc) continue;
    try {
      const args = JSON.parse(Buffer.from(fc.args, 'base64').toString('utf8')) as unknown;
      return { method: fc.method_name, args: args && typeof args === 'object' ? (args as Record<string, unknown>) : {} };
    } catch {
      return { method: fc.method_name, args: {} };
    }
  }
  return null;
}

/**
 * The token title and icon a mint call carried. `item_mint` doesn't repeat them, but the receipt
 * that emitted it is the `mint_with_voucher` / `studio_mint` call itself, whose args hold both.
 */
function mintDisplay(call: { method: string; args: Record<string, unknown> } | null): { title?: string; media?: string } {
  if (!call) return {};
  const src = call.method === 'mint_with_voucher' ? (call.args['voucher'] as Record<string, unknown> | undefined) : call.args;
  const str = (v: unknown) => (typeof v === 'string' ? v : undefined);
  return { title: str(src?.['title']), media: str(src?.['media']) };
}

export function blockTime(block: NearBlock): Date {
  const ns = block.block.header.timestamp_nanosec ?? String(block.block.header.timestamp);
  return new Date(Number(BigInt(ns) / 1_000_000n));
}

/** Every trusted event in the block, in execution order (shard, outcome, log). */
export function eventsIn(block: NearBlock, watched: Watched): ChainEvent[] {
  const height = block.block.header.height;
  const time = blockTime(block);
  const out: ChainEvent[] = [];
  for (const shard of block.shards) {
    for (const r of shard.receipt_execution_outcomes ?? []) {
      const { outcome } = r.execution_outcome;
      const contractId = outcome.executor_id;
      if (contractId !== watched.items && contractId !== watched.market) continue;
      if (!succeeded(outcome.status)) continue;
      let call: ReturnType<typeof functionCall> | undefined;
      outcome.logs.forEach((line, logIndex) => {
        if (!line.startsWith(PREFIX)) return;
        let body: { standard?: unknown; event?: unknown; data?: unknown };
        try {
          body = JSON.parse(line.slice(PREFIX.length)) as typeof body;
        } catch {
          return;
        }
        if (typeof body.standard !== 'string' || typeof body.event !== 'string') return;
        if (!trusted(watched, contractId, body.standard)) return;
        const entries = Array.isArray(body.data) ? body.data : [body.data ?? {}];
        entries.forEach((entry, n) => {
          if (!entry || typeof entry !== 'object') return;
          let data = entry as Record<string, unknown>;
          if (body.event === 'item_mint') {
            call ??= functionCall(r);
            data = { ...data, ...mintDisplay(call) };
          }
          out.push({
            id: entries.length > 1 ? `${r.receipt.receipt_id}:${logIndex}:${n}` : `${r.receipt.receipt_id}:${logIndex}`,
            blockHeight: height,
            blockTime: time,
            txHash: r.tx_hash ?? null,
            receiptId: r.receipt.receipt_id,
            logIndex,
            contractId,
            standard: body.standard as string,
            event: body.event as string,
            data,
          });
        });
      });
    }
  }
  return out;
}
