import { CLIENT_ONLY_ERRORS, type ErrorBody, type ErrorCodeName } from '@farmverse/contracts';

/**
 * An expected refusal: a contract error code plus the string parameters its
 * localized message needs. Handlers throw it; the callable pipeline turns it into
 * an `ok: false` envelope. The server never sends a sentence (05 §4).
 */
export class AppError extends Error {
  readonly code: ErrorCodeName;
  readonly params: Readonly<Record<string, string>>;
  readonly retryAfterMs: number | undefined;
  readonly issues: ErrorBody['issues'];

  constructor(
    code: ErrorCodeName,
    params: Readonly<Record<string, string | number>> = {},
    options: { retryAfterMs?: number; issues?: ErrorBody['issues']; cause?: unknown } = {},
  ) {
    super(code, options.cause === undefined ? undefined : { cause: options.cause });
    if (CLIENT_ONLY_ERRORS.has(code)) {
      throw new Error(`${code} is produced by the client only and must not be raised by the server.`);
    }
    this.name = 'AppError';
    this.code = code;
    this.params = Object.fromEntries(Object.entries(params).map(([key, value]) => [key, String(value)]));
    this.retryAfterMs = options.retryAfterMs;
    this.issues = options.issues;
  }

  toBody(): ErrorBody {
    const body: ErrorBody = { code: this.code, params: { ...this.params } };
    if (this.retryAfterMs !== undefined) body.retryAfterMs = this.retryAfterMs;
    if (this.issues !== undefined) body.issues = this.issues;
    return body;
  }
}

/** Throw an expected refusal. */
export function fail(code: ErrorCodeName, params: Readonly<Record<string, string | number>> = {}): never {
  throw new AppError(code, params);
}

/**
 * A stored document failed its schema: DATA_CORRUPT, carrying which document and what was wrong
 * (the issues reach the client in non-production backends and are always logged).
 */
export function corrupt(what: string, error: { issues: readonly { path: readonly PropertyKey[]; message: string }[] }): AppError {
  const issues = error.issues.slice(0, 5).map((i) => ({ path: `${what}.${i.path.map(String).join('.')}`.slice(0, 256), message: i.message.slice(0, 512) }));
  console.error(JSON.stringify({ severity: 'ERROR', message: 'stored document failed its schema', what, issues }));
  return new AppError('DATA_CORRUPT', {}, { issues });
}

/**
 * True for a document left by the old grid-farm game at a path the current game reuses (it has no
 * `updatedAt`): the readers then start fresh, and the next write replaces it.
 */
export function legacyDoc(what: string, data: Record<string, unknown> | undefined): boolean {
  if (!data || typeof data['updatedAt'] === 'number') return false;
  console.warn(JSON.stringify({ severity: 'WARNING', message: 'old-format document replaced', what, fields: Object.keys(data).slice(0, 10) }));
  return true;
}
