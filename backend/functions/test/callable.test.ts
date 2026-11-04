import { describe, expect, it } from 'vitest';
import { CALLABLES } from '@farmverse/contracts';
import { runCallable, type CallableDeps } from '../src/core/callable';
import { FixedClock } from '../src/core/clock';
import { AppError } from '../src/core/errors';
import { memoryLogger } from '../src/core/logger';

const deps = (overrides: Partial<CallableDeps> = {}): CallableDeps => ({
  clock: new FixedClock(1_000),
  log: memoryLogger(),
  minClientVersion: () => '1.2.0',
  exposeIssues: true,
  ...overrides,
});

const caller = { uid: 'uidAAAAAAAAAAAAAAAAAAAA', isAdmin: false, requestId: 'req-1' };
const valid = { meta: { clientVersion: '1.2.0', platform: 'ios' }, displayName: 'Ada', avatarId: 'avatar_one' };

const definition = (handler: () => Promise<unknown>) => ({
  spec: CALLABLES.playerCreate,
  auth: 'required' as const,
  handler: handler as never,
});

describe('runCallable', () => {
  it('returns an ok envelope with server time and request id', async () => {
    const envelope = await runCallable(definition(async () => ({ uid: caller.uid })), valid, caller, deps());
    expect(envelope).toEqual({ ok: true, data: { uid: caller.uid }, serverTimeMs: 1_000, requestId: 'req-1' });
  });

  it('refuses unauthenticated callers', async () => {
    const envelope = await runCallable(definition(async () => ({})), valid, { ...caller, uid: undefined }, deps());
    expect(envelope).toMatchObject({ ok: false, error: { code: 'UNAUTHENTICATED' } });
  });

  it('rejects schema violations with issues outside production only', async () => {
    const bad = { ...valid, avatarId: 'Not An Id' };
    const dev = await runCallable(definition(async () => ({})), bad, caller, deps());
    expect(dev).toMatchObject({ ok: false, error: { code: 'INVALID_ARGUMENT' } });
    expect(dev.ok === false && dev.error.issues?.[0]?.path).toBe('avatarId');
    const prod = await runCallable(definition(async () => ({})), bad, caller, deps({ exposeIssues: false }));
    expect(prod.ok === false && prod.error.issues).toBeUndefined();
  });

  it('gates outdated clients', async () => {
    const old = { ...valid, meta: { clientVersion: '1.1.9', platform: 'ios' } };
    const envelope = await runCallable(definition(async () => ({})), old, caller, deps());
    expect(envelope).toMatchObject({ ok: false, error: { code: 'CLIENT_OUTDATED', params: { minVersion: '1.2.0' } } });
  });

  it('passes refusals through as codes', async () => {
    const envelope = await runCallable(
      definition(async () => { throw new AppError('NAME_TAKEN'); }),
      valid, caller, deps(),
    );
    expect(envelope).toMatchObject({ ok: false, error: { code: 'NAME_TAKEN', params: {} } });
  });

  it('hides defects behind INTERNAL and logs them', async () => {
    const log = memoryLogger();
    const envelope = await runCallable(
      definition(async () => { throw new TypeError('undefined is not a function'); }),
      valid, caller, deps({ log }),
    );
    expect(envelope).toMatchObject({ ok: false, error: { code: 'INTERNAL', params: {} } });
    expect(log.entries.some((e) => e.severity === 'ERROR' && e.message === 'defect')).toBe(true);
  });

  it('treats a response that breaks the contract as a defect', async () => {
    const envelope = await runCallable(definition(async () => ({ uid: 42 })), valid, caller, deps());
    expect(envelope).toMatchObject({ ok: false, error: { code: 'INTERNAL' } });
  });

  it('never lets the server raise a client-only code', () => {
    expect(() => new AppError('NETWORK_REQUIRED')).toThrow(/client only/);
  });
});
