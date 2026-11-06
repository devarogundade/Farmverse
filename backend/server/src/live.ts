import { z } from 'zod';
import type { Logger } from '../../functions/src/core/logger';
import type { DocData, QueryRow } from '../../functions/src/infra/store';
import { canRead, listLimit } from './access';
import type { Caller } from './auth/sessions';
import type { ChangeBus } from './infra/redis';
import type { DocChange } from './store/prismaStore';
import type { DocQuery } from './store/query';

/** Reads with revisions (the hub tags every push so clients can drop stale ones). */
export interface DocReader {
  get(path: string): Promise<{ exists: boolean; rev: number; data: DocData | undefined }>;
  query(q: DocQuery): Promise<QueryRow[]>;
}

/** The part of a WebSocket the hub uses (ws / @fastify/websocket sockets fit). */
export interface LiveSocket {
  send(data: string): void;
  close(code?: number, reason?: string): void;
  on(event: 'message', listener: (data: unknown) => void): unknown;
  on(event: 'close', listener: () => void): unknown;
}

const Scalar = z.union([z.string().max(256), z.number(), z.boolean()]);
/** A client's query: Firestore semantics (ties break by document id in the last order's direction). */
export const QuerySpec = z
  .object({
    collection: z.string().min(1).max(512),
    filters: z
      .array(z.object({ field: z.string().max(64), op: z.enum(['==', '<', '<=', '>', '>=', 'in']), value: z.union([Scalar, z.array(Scalar).max(30)]) }))
      .max(5)
      .default([]),
    orders: z.array(z.object({ field: z.string().max(64), direction: z.enum(['asc', 'desc']) })).max(3).default([]),
    limit: z.number().int().min(1).max(50),
    /** One value per order, then optionally the last document's id. */
    startAfter: z.array(Scalar).max(4).optional(),
  })
  .transform((q) => ({ ...q, tieBreak: 'last' as const }));

const ClientMessage = z.discriminatedUnion('type', [
  z.object({ type: z.literal('auth'), token: z.string().max(4096) }),
  z.object({ type: z.literal('sub'), id: z.string().max(64), path: z.string().max(512) }),
  z.object({ type: z.literal('subq'), id: z.string().max(64), query: QuerySpec }),
  z.object({ type: z.literal('unsub'), id: z.string().max(64) }),
  z.object({ type: z.literal('ping') }),
]);

export const MAX_SUBSCRIPTIONS = 100;
const QUERY_DEBOUNCE_MS = 100;
const AUTH_DEADLINE_MS = 10_000;

type Sub = { kind: 'doc'; path: string } | { kind: 'query'; query: DocQuery; timer?: ReturnType<typeof setTimeout> };

class Connection {
  caller: Caller | null = null;
  readonly subs = new Map<string, Sub>();
  constructor(readonly socket: LiveSocket) {}
  send(message: object): void {
    try {
      this.socket.send(JSON.stringify(message));
    } catch {
      // closed underneath us; the close handler cleans up
    }
  }
}

/**
 * Live documents and queries for every socket on this instance. A commit on any instance reaches
 * every hub through the change bus; the hub re-reads and pushes to subscribers allowed to read.
 */
export class LiveHub {
  private readonly connections = new Set<Connection>();
  private unsubscribe: (() => Promise<void>) | undefined;

  constructor(
    private readonly reader: DocReader,
    private readonly verify: (token: string) => Promise<Caller | null>,
    private readonly log: Logger,
  ) {}

  async start(bus: ChangeBus): Promise<void> {
    this.unsubscribe = await bus.subscribe((changes) => this.onChanges(changes));
  }

  async stop(): Promise<void> {
    await this.unsubscribe?.();
    for (const c of this.connections) c.socket.close(1001, 'server shutting down');
  }

  get size(): number {
    return this.connections.size;
  }

  attach(socket: LiveSocket): void {
    const conn = new Connection(socket);
    this.connections.add(conn);
    const deadline = setTimeout(() => {
      if (!conn.caller) socket.close(4401, 'auth required');
    }, AUTH_DEADLINE_MS);
    socket.on('message', (raw) => void this.onMessage(conn, raw));
    socket.on('close', () => {
      clearTimeout(deadline);
      for (const sub of conn.subs.values()) if (sub.kind === 'query' && sub.timer) clearTimeout(sub.timer);
      this.connections.delete(conn);
    });
  }

  private async onMessage(conn: Connection, raw: unknown): Promise<void> {
    let message: z.infer<typeof ClientMessage>;
    try {
      message = ClientMessage.parse(JSON.parse(String(raw)));
    } catch {
      conn.send({ type: 'error', code: 'INVALID_ARGUMENT' });
      return;
    }
    if (message.type === 'ping') return conn.send({ type: 'pong' });
    if (message.type === 'auth') {
      const caller = await this.verify(message.token);
      if (!caller || (conn.caller && conn.caller.uid !== caller.uid)) {
        conn.send({ type: 'error', code: 'UNAUTHENTICATED' });
        conn.socket.close(4401, 'bad token');
        return;
      }
      conn.caller = caller;
      return conn.send({ type: 'ready', uid: caller.uid });
    }
    const caller = conn.caller;
    if (!caller) return conn.send({ type: 'error', code: 'UNAUTHENTICATED' });
    if (message.type === 'unsub') {
      const sub = conn.subs.get(message.id);
      if (sub?.kind === 'query' && sub.timer) clearTimeout(sub.timer);
      conn.subs.delete(message.id);
      return;
    }
    if (!conn.subs.has(message.id) && conn.subs.size >= MAX_SUBSCRIPTIONS) {
      return conn.send({ type: 'error', id: message.id, code: 'RATE_LIMITED' });
    }
    if (message.type === 'sub') {
      if (!canRead(caller.uid, message.path)) return conn.send({ type: 'error', id: message.id, code: 'PERMISSION_DENIED' });
      conn.subs.set(message.id, { kind: 'doc', path: message.path });
      return this.pushDoc(conn, message.id, message.path);
    }
    const max = listLimit(caller.uid, message.query.collection);
    if (max === 0 || message.query.limit > max) return conn.send({ type: 'error', id: message.id, code: 'PERMISSION_DENIED' });
    conn.subs.set(message.id, { kind: 'query', query: message.query });
    return this.pushQuery(conn, message.id, message.query);
  }

  private onChanges(changes: readonly DocChange[]): void {
    for (const change of changes) {
      for (const conn of this.connections) {
        for (const [id, sub] of conn.subs) {
          if (sub.kind === 'doc' && sub.path === change.path) void this.pushDoc(conn, id, sub.path);
          if (sub.kind === 'query' && sub.query.collection === change.collection && !sub.timer) {
            sub.timer = setTimeout(() => {
              sub.timer = undefined;
              if (conn.subs.get(id) === sub) void this.pushQuery(conn, id, sub.query);
            }, QUERY_DEBOUNCE_MS);
          }
        }
      }
    }
  }

  private async pushDoc(conn: Connection, id: string, path: string): Promise<void> {
    try {
      const doc = await this.reader.get(path);
      conn.send({ type: 'doc', id, path, exists: doc.exists, rev: doc.rev, data: doc.data ?? null });
    } catch (error) {
      this.log.warn('live doc read failed', { path, error: String(error) });
      conn.send({ type: 'error', id, code: 'INTERNAL' });
    }
  }

  private async pushQuery(conn: Connection, id: string, query: DocQuery): Promise<void> {
    try {
      const rows = await this.reader.query(query);
      conn.send({ type: 'query', id, docs: rows });
    } catch (error) {
      this.log.warn('live query failed', { collection: query.collection, error: String(error) });
      conn.send({ type: 'error', id, code: 'INTERNAL' });
    }
  }
}
