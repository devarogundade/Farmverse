import { z } from 'zod';
import { named } from '../registry';
import { ErrorBody } from './errors';
import { EpochMs, SemVer } from './primitives';

/** The client platform. `editor` is accepted only by non-production backends. */
export const Platform = named('Platform', z.enum(['android', 'ios', 'editor']));

/** Sent with every callable request, for the version gate and for logs. */
export const RequestMeta = named(
  'RequestMeta',
  z.object({
    clientVersion: SemVer,
    platform: Platform,
    locale: z.string().max(16).optional(),
  }),
  'Who is calling: version, platform, locale.',
);

export type RequestMeta = z.infer<typeof RequestMeta>;
export type Platform = z.infer<typeof Platform>;

/**
 * The response of every callable.
 *
 * Business refusals are `ok: false` with an {@link ErrorBody}; only transport and
 * platform failures (App Check, authentication, malformed request, crash) are
 * raised as HTTPS errors. The C# side parses this shape by hand in the
 * infrastructure layer and hands `data` to the generated `FromWire` of the
 * expected response type, so the envelope itself is not a generated type.
 */
export function envelope<T extends z.ZodType>(data: T) {
  return z.discriminatedUnion('ok', [
    z.object({
      ok: z.literal(true),
      data,
      serverTimeMs: EpochMs,
      requestId: z.string().max(64),
    }),
    z.object({
      ok: z.literal(false),
      error: ErrorBody,
      serverTimeMs: EpochMs,
      requestId: z.string().max(64),
    }),
  ]);
}

export type Envelope<T> =
  | { readonly ok: true; readonly data: T; readonly serverTimeMs: number; readonly requestId: string }
  | { readonly ok: false; readonly error: ErrorBody; readonly serverTimeMs: number; readonly requestId: string };

/** An empty response payload. */
export const Empty = named('Empty', z.object({}), 'A response that carries no data.');
