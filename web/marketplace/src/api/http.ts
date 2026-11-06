import type { z } from 'zod';
import { config } from '../config';

/** An API failure with a message fit to show people. */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly code?: string,
  ) {
    super(message);
  }
}

let authToken: string | null = null;
/** Session JWT from wallet sign-in (NEP-413 → POST /auth/near), sent on every request. */
export const setAuthToken = (t: string | null) => (authToken = t);

interface Options<S extends z.ZodType> {
  method?: 'GET' | 'POST' | 'DELETE';
  query?: Record<string, string | number | null | undefined>;
  body?: unknown;
  schema?: S;
  signal?: AbortSignal;
  timeoutMs?: number;
}

/** fetch + JSON + zod. Responses are parsed against `schema`, never trusted as-is. */
export async function http<S extends z.ZodType>(path: string, opts: Options<S> = {}): Promise<z.infer<S>> {
  if (!config.apiUrl) throw new ApiError('The market is offline.', 0, 'no_api');
  const url = new URL(config.apiUrl + path);
  for (const [k, v] of Object.entries(opts.query ?? {})) if (v != null && v !== '') url.searchParams.set(k, String(v));

  const timeout = AbortSignal.timeout(opts.timeoutMs ?? 12_000);
  const signal = opts.signal ? AbortSignal.any([opts.signal, timeout]) : timeout;
  let res: Response;
  try {
    res = await fetch(url, {
      method: opts.method ?? 'GET',
      headers: { 'content-type': 'application/json', ...(authToken ? { authorization: `Bearer ${authToken}` } : {}) },
      body: opts.body === undefined ? undefined : JSON.stringify(opts.body),
      signal,
    });
  } catch (e) {
    if ((e as Error).name === 'AbortError' && opts.signal?.aborted) throw e;
    throw new ApiError('Can’t reach the market. Check your connection and try again.', 0, 'network');
  }
  const text = await res.text();
  const json = text ? JSON.parse(text) : null;
  if (!res.ok) throw new ApiError(json?.message ?? 'Something went wrong. Please try again.', res.status, json?.code);
  if (!opts.schema) return json;
  const parsed = opts.schema.safeParse(json);
  if (!parsed.success) throw new ApiError('The market sent something unexpected.', res.status, 'bad_response');
  return parsed.data;
}
