import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import type { RateLimiter } from '../infra/redis';
import { redeemForPayout, type RedeemDeps } from './redeem';

export interface CouponRouteOptions extends RedeemDeps {
  readonly limiter: RateLimiter;
  /** Where a payout's transaction can be seen: the hash is appended. */
  readonly explorerTxUrl: string;
}

const Body = z.object({ code: z.string().max(40), wallet: z.string().max(64) });

/** The public redeem page's API (farmverse.world/redeem): no sign-in, a code and a wallet. */
export function couponRoutes(o: CouponRouteOptions) {
  return async (app: FastifyInstance) => {
    app.post('/redeem', async (req, reply) => {
      if (!(await o.limiter.hit(`redeem:${req.ip}`, 10, 60)))
        return reply.status(429).send({ ok: false, code: 'rate_limited' });
      const body = Body.safeParse(req.body);
      if (!body.success) return reply.status(400).send({ ok: false, code: 'bad_code' });
      // a code can be tried a few times a minute from anywhere, so guessing codes stays slow
      if (!(await o.limiter.hit(`redeem:code:${body.data.code.replace(/\s+/g, '').toUpperCase()}`, 5, 60)))
        return reply.status(429).send({ ok: false, code: 'rate_limited' });
      const { status, ...out } = await redeemForPayout(o, body.data.code, body.data.wallet);
      return reply.status(status).send('tx' in out && out.tx ? { ...out, txUrl: o.explorerTxUrl + out.tx } : out);
    });
  };
}
