import type { FastifyInstance, FastifyReply } from 'fastify';
import { z } from 'zod';
import type { Logger } from '../../../functions/src/core/logger';
import { NFT_TYPES } from '../chain/items';
import type { MarketMessage } from '../chain/project';
import type { MarketBus, RateLimiter } from '../infra/redis';
import type { MarketReader } from './read';

/** When each account last opened its notifications (epoch ms). */
export interface NoteMarks {
  get(account: string): Promise<number>;
  set(account: string, atMs: number): Promise<void>;
}

export interface MarketOptions {
  readonly reader: MarketReader;
  readonly marks: NoteMarks;
  readonly limiter: RateLimiter;
  readonly hub: MarketHub;
  readonly now?: () => number;
}

const Account = z.string().min(2).max(64).regex(/^[a-z0-9._-]+$/);
const TokenId = z.string().min(1).max(64);
const Browse = z.object({
  kind: z.enum(NFT_TYPES as [string, ...string[]]).optional(),
  q: z.string().max(64).optional(),
  sort: z.enum(['newest', 'price_asc', 'price_desc']).optional(),
  cursor: z.string().max(16).optional(),
  limit: z.coerce.number().int().min(1).max(60).optional(),
});
const Ids = z.object({ ids: z.string().max(4096) });

function fail(reply: FastifyReply, status: number, code: string, message: string) {
  return reply.status(status).send({ code, message });
}

/**
 * The marketplace's public read API (docs 13 §9) over the indexer's tables, plus its live socket.
 * Mounted under /v1/market. Everything here is public chain data, so no sign-in is needed.
 */
export function marketRoutes(o: MarketOptions) {
  const now = o.now ?? Date.now;
  return async (app: FastifyInstance) => {
    app.addHook('onRequest', async (req, reply) => {
      if (req.method === 'OPTIONS' || req.url.endsWith('/live')) return;
      if (!(await o.limiter.hit(`market:${req.ip}`, 600, 60))) return fail(reply, 429, 'rate_limited', 'Too many requests. Try again in a minute.');
    });

    app.get('/listings', async (req, reply) => {
      const q = Browse.safeParse(req.query);
      if (!q.success) return fail(reply, 400, 'bad_query', 'That search isn’t valid.');
      return o.reader.browse({ ...q.data, type: q.data.kind as never });
    });

    app.get<{ Params: { account: string } }>('/owners/:account/tokens', async (req, reply) => {
      const account = Account.safeParse(req.params.account);
      const q = Browse.safeParse(req.query);
      if (!account.success || !q.success) return fail(reply, 400, 'bad_query', 'That search isn’t valid.');
      return o.reader.browse({ ...q.data, type: q.data.kind as never, owner: account.data });
    });

    app.get('/tokens', async (req, reply) => {
      const q = Ids.safeParse(req.query);
      if (!q.success) return fail(reply, 400, 'bad_query', 'Pass ids=a,b,c');
      const ids = [...new Set(q.data.ids.split(',').map((s) => s.trim()).filter((s) => TokenId.safeParse(s).success))].slice(0, 100);
      return o.reader.tokens(ids);
    });

    app.get<{ Params: { id: string } }>('/tokens/:id', async (req, reply) => {
      const id = TokenId.safeParse(req.params.id);
      if (!id.success) return fail(reply, 404, 'not_found', 'That item doesn’t exist.');
      const detail = await o.reader.token(id.data);
      if (!detail) return fail(reply, 404, 'not_found', 'That item doesn’t exist or was redeemed.');
      return detail;
    });

    // Offers are off-chain (docs 14 §3) and not built yet: none to show, none to make.
    app.get('/tokens/:id/offers', async () => []);
    const noOffers = async (_req: unknown, reply: FastifyReply) => fail(reply, 501, 'offers_closed', 'Offers aren’t open yet. You can buy listed items now.');
    app.post('/tokens/:id/offers', noOffers);
    app.delete('/offers/:id', noOffers);
    app.post('/offers/:id/decline', noOffers);
    app.post('/offers/:id/accept', noOffers);

    app.get<{ Params: { account: string } }>('/accounts/:account/notifications', async (req, reply) => {
      const account = Account.safeParse(req.params.account);
      if (!account.success) return fail(reply, 400, 'bad_account', 'That isn’t a NEAR account.');
      return o.reader.notes(account.data, await o.marks.get(account.data));
    });

    // Marking read changes only a display flag on public data, so it needs no sign-in.
    app.post<{ Params: { account: string } }>('/accounts/:account/notifications/read', async (req, reply) => {
      const account = Account.safeParse(req.params.account);
      if (!account.success) return fail(reply, 400, 'bad_account', 'That isn’t a NEAR account.');
      await o.marks.set(account.data, now());
      return { ok: true };
    });

    app.get<{ Querystring: { account?: string } }>('/live', { websocket: true }, (socket, req) => {
      const account = Account.safeParse(req.query.account);
      o.hub.attach(socket as unknown as HubSocket, account.success ? account.data : null);
    });
  };
}

export interface HubSocket {
  send(data: string): void;
  close(code?: number, reason?: string): void;
  on(event: 'close', listener: () => void): unknown;
  on(event: 'message', listener: (data: unknown) => void): unknown;
}

/**
 * Pushes the indexer's market messages to every open marketplace page on this instance. Notes go
 * only to sockets opened for their account; everything else goes to all.
 */
export class MarketHub {
  private readonly sockets = new Map<HubSocket, string | null>();
  private unsubscribe: (() => Promise<void>) | undefined;

  constructor(private readonly log: Logger) {}

  async start(bus: MarketBus): Promise<void> {
    this.unsubscribe = await bus.subscribe((messages) => this.broadcast(messages));
  }

  async stop(): Promise<void> {
    await this.unsubscribe?.();
    for (const s of this.sockets.keys()) s.close(1001, 'server shutting down');
  }

  get size(): number {
    return this.sockets.size;
  }

  attach(socket: HubSocket, account: string | null): void {
    this.sockets.set(socket, account);
    socket.on('close', () => this.sockets.delete(socket));
    socket.on('message', (raw) => {
      if (String(raw) === '{"type":"ping"}') this.send(socket, { type: 'pong' });
    });
  }

  broadcast(messages: readonly MarketMessage[]): void {
    for (const m of messages) {
      for (const [socket, account] of this.sockets) {
        if (m.type === 'note' && m.to !== account) continue;
        this.send(socket, m);
      }
    }
  }

  private send(socket: HubSocket, message: object): void {
    try {
      socket.send(JSON.stringify(message));
    } catch (error) {
      this.log.warn('market socket send failed', { error: String(error) });
    }
  }
}
