import { config } from '../config';

/** An API failure with a message fit to show. */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly code?: string,
    readonly field?: string,
  ) {
    super(message);
  }
}

export interface Tokens {
  accessToken: string;
  accessExpiresAt: number;
  refreshToken: string;
  accountId: string;
}

/*
 * The session. The access token lives in memory only; the refresh token in sessionStorage, so a
 * reload keeps you signed in but closing the tab signs you out.
 */
const KEY = 'fv_admin_refresh';
let access: { token: string; expiresAt: number } | null = null;
let refreshing: Promise<boolean> | null = null;
const listeners = new Set<() => void>();

export const onSessionLost = (fn: () => void) => {
  listeners.add(fn);
  return () => void listeners.delete(fn);
};

function storedRefresh(): string | null {
  try {
    return sessionStorage.getItem(KEY);
  } catch {
    return null;
  }
}

export function setSession(t: Tokens | null): void {
  access = t ? { token: t.accessToken, expiresAt: t.accessExpiresAt } : null;
  try {
    if (t) sessionStorage.setItem(KEY, t.refreshToken);
    else sessionStorage.removeItem(KEY);
  } catch {
    /* storage blocked: the session lasts until reload */
  }
}

export const hasStoredSession = () => storedRefresh() !== null;

async function raw(path: string, init: RequestInit & { json?: unknown; token?: string | null } = {}): Promise<Response> {
  if (!config.apiUrl) throw new ApiError('No server is set for this site.', 0, 'no_api');
  const headers: Record<string, string> = {};
  if (init.json !== undefined) headers['content-type'] = 'application/json';
  if (init.token) headers['authorization'] = `Bearer ${init.token}`;
  try {
    return await fetch(config.apiUrl + path, {
      ...init,
      headers,
      body: init.json === undefined ? undefined : JSON.stringify(init.json),
      signal: init.signal ?? AbortSignal.timeout(15_000),
    });
  } catch {
    throw new ApiError('Can’t reach the server. Check your connection and try again.', 0, 'network');
  }
}

async function read<T>(res: Response): Promise<T> {
  const text = await res.text();
  const json = text ? JSON.parse(text) : null;
  if (!res.ok) throw new ApiError(json?.message ?? json?.error?.code ?? 'Something went wrong.', res.status, json?.code ?? json?.error?.code, json?.field);
  return json as T;
}

/** Swaps the stored refresh token for a new session (once at a time). */
export function refresh(): Promise<boolean> {
  refreshing ??= (async () => {
    const token = storedRefresh();
    if (!token) return false;
    try {
      const res = await raw('/v1/auth/refresh', { method: 'POST', json: { refreshToken: token } });
      if (!res.ok) {
        setSession(null);
        return false;
      }
      setSession(await read<Tokens>(res));
      return true;
    } catch {
      return false;
    }
  })().finally(() => (refreshing = null));
  return refreshing;
}

/** A public game-server call (sign-in, config). */
export async function pub<T>(path: string, method: 'GET' | 'POST' = 'GET', json?: unknown): Promise<T> {
  return read<T>(await raw(path, { method, json }));
}

/** An admin API call (/v1/admin…), refreshing the session when the access token has run out. */
export async function admin<T>(
  path: string,
  opts: { method?: 'GET' | 'POST' | 'PUT' | 'DELETE'; body?: unknown; query?: Record<string, string | number | undefined> } = {},
): Promise<T> {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(opts.query ?? {})) if (v !== undefined && v !== '') q.set(k, String(v));
  const url = `/v1/admin${path}${q.size ? `?${q}` : ''}`;
  if (!access || access.expiresAt - Date.now() < 30_000) await refresh();
  let res = await raw(url, { method: opts.method ?? 'GET', json: opts.body, token: access?.token });
  if (res.status === 401 && (await refresh())) res = await raw(url, { method: opts.method ?? 'GET', json: opts.body, token: access?.token });
  if (res.status === 401) {
    setSession(null);
    listeners.forEach((fn) => fn());
  }
  return read<T>(res);
}

export async function signOut(): Promise<void> {
  const token = storedRefresh();
  setSession(null);
  if (token) await raw('/v1/auth/logout', { method: 'POST', json: { refreshToken: token } }).catch(() => undefined);
}
