import { randomUUID } from 'node:crypto';
import type { z } from 'zod';
import type { CallableSpec, Envelope, ErrorBody } from '@farmverse/contracts';
import type { Clock } from './clock';
import { AppError } from './errors';
import type { Logger } from './logger';
import { compareVersions } from './version';

export type AuthRequirement = 'required' | 'admin' | 'none';

/** Who is calling, as the platform verified it. */
export interface CallerIdentity {
  readonly uid: string | undefined;
  readonly isAdmin: boolean;
  readonly requestId: string | undefined;
  /** How the caller signed in (`anonymous`, `google.com`, `apple.com`…), when known. */
  readonly signInProvider?: string;
}

export interface CallableDeps {
  readonly clock: Clock;
  readonly log: Logger;
  /** The lowest client version accepted (Remote Config `min_client_version`). */
  readonly minClientVersion: () => string;
  /** Whether schema issues may be returned to the client (never in production). */
  readonly exposeIssues: boolean;
}

export interface HandlerInput<Req> {
  readonly uid: string;
  readonly data: Req;
  readonly requestId: string;
  readonly log: Logger;
  /** How the caller signed in ('' when unknown). */
  readonly signInProvider: string;
}

export interface CallableDefinition<S extends CallableSpec> {
  readonly spec: S;
  readonly auth: AuthRequirement;
  readonly handler: (input: HandlerInput<z.infer<S['request']>>) => Promise<z.input<S['response']>>;
}

/**
 * The callable pipeline (03 §2), independent of the Firebase SDK so it is unit
 * tested directly: authenticate → validate → version gate → handle → validate
 * the response → envelope.
 *
 * Refusals (`AppError`) become `ok: false` envelopes. Anything else is a defect:
 * logged with its stack and reported to the client only as INTERNAL.
 */
export async function runCallable<S extends CallableSpec>(
  definition: CallableDefinition<S>,
  rawData: unknown,
  caller: CallerIdentity,
  deps: CallableDeps,
): Promise<Envelope<z.output<S['response']>>> {
  const requestId = caller.requestId ?? randomUUID();
  const log = deps.log.with({ fn: definition.spec.name, requestId, uid: caller.uid });
  const started = deps.clock.nowMs();

  const envelopeError = (error: ErrorBody) =>
    ({ ok: false, error, serverTimeMs: deps.clock.nowMs(), requestId }) as const;

  try {
    if (definition.auth !== 'none' && !caller.uid) {
      throw new AppError('UNAUTHENTICATED');
    }
    if (definition.auth === 'admin' && !caller.isAdmin) {
      throw new AppError('PERMISSION_DENIED');
    }

    const parsed = definition.spec.request.safeParse(rawData);
    if (!parsed.success) {
      const issues = parsed.error.issues.slice(0, 20).map((issue) => ({
        path: issue.path.join('.'),
        message: issue.message.slice(0, 512),
      }));
      log.info('request rejected by schema', { issues });
      throw new AppError('INVALID_ARGUMENT', {}, deps.exposeIssues ? { issues } : {});
    }
    const data = parsed.data as z.infer<S['request']>;

    const meta = (data as { meta?: { clientVersion?: string } }).meta;
    if (meta?.clientVersion !== undefined) {
      const minimum = deps.minClientVersion();
      if (compareVersions(meta.clientVersion, minimum) < 0) {
        throw new AppError('CLIENT_OUTDATED', { minVersion: minimum });
      }
    }

    const result = await definition.handler({ uid: caller.uid ?? '', data, requestId, log, signInProvider: caller.signInProvider ?? '' });
    const response = definition.spec.response.safeParse(result);
    if (!response.success) {
      throw new Error(`${definition.spec.name} returned a response that breaks its contract: ${response.error.message}`);
    }

    log.info('ok', { durationMs: deps.clock.nowMs() - started });
    return { ok: true, data: response.data as z.output<S['response']>, serverTimeMs: deps.clock.nowMs(), requestId };
  } catch (error) {
    if (error instanceof AppError) {
      log.info('refused', { code: error.code, durationMs: deps.clock.nowMs() - started });
      return envelopeError(error.toBody());
    }
    log.error('defect', { error, durationMs: deps.clock.nowMs() - started });
    return envelopeError({ code: 'INTERNAL', params: {} });
  }
}
