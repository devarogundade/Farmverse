import type { NearConnector } from '@hot-labs/near-connect';
import { useQueryClient } from '@tanstack/react-query';
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { admin, ApiError, hasStoredSession, onSessionLost, pub, refresh, setSession, signOut as endSession, type Tokens } from './api/http';
import { config } from './config';

type Status = 'checking' | 'signed-out' | 'signed-in';

interface Session {
  status: Status;
  accountId: string | null;
  /** The server runs in development: a wallet-free sign-in is offered. */
  devServer: boolean;
  signInWithWallet: () => Promise<void>;
  signInDev: (accountId: string) => Promise<void>;
  signOut: () => Promise<void>;
}

const Ctx = createContext<Session | null>(null);

export function useSession(): Session {
  const s = useContext(Ctx);
  if (!s) throw new Error('useSession outside SessionProvider');
  return s;
}

let connector: Promise<NearConnector> | null = null;
const getConnector = () =>
  (connector ??= import('@hot-labs/near-connect').then(({ NearConnector }) => new NearConnector({ network: config.network, footerBranding: null })));

const fromBase64 = (s: string) => Uint8Array.from(atob(s), (c) => c.charCodeAt(0));

/** Signs in, then checks this wallet is an admin; a wallet that isn't is signed straight out. */
async function finish(tokens: Tokens): Promise<string> {
  setSession(tokens);
  try {
    const me = await admin<{ accountId: string }>('/me');
    return me.accountId;
  } catch (e) {
    await endSession();
    if (e instanceof ApiError && e.status === 403) throw new ApiError(`${tokens.accountId} is not an admin wallet.`, 403);
    throw e;
  }
}

export function SessionProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient();
  const [status, setStatus] = useState<Status>(hasStoredSession() ? 'checking' : 'signed-out');
  const [accountId, setAccountId] = useState<string | null>(null);
  const [devServer, setDevServer] = useState(false);

  useEffect(() => {
    pub<{ environment: string }>('/v1/config')
      .then((c) => setDevServer(c.environment === 'dev'))
      .catch(() => setDevServer(false));
    if (hasStoredSession()) {
      refresh()
        .then(async (ok) => {
          if (!ok) return setStatus('signed-out');
          const me = await admin<{ accountId: string }>('/me');
          setAccountId(me.accountId);
          setStatus('signed-in');
        })
        .catch(() => setStatus('signed-out'));
    }
    return onSessionLost(() => {
      setAccountId(null);
      setStatus('signed-out');
    });
  }, []);

  const value = useMemo<Session>(
    () => ({
      status,
      accountId,
      devServer,
      signInWithWallet: async () => {
        const c = await getConnector();
        const wallet = await c.connect();
        const { nonce, message, recipient } = await pub<{ nonce: string; message: string; recipient: string }>('/v1/auth/nonce', 'POST');
        const signed = await wallet.signMessage({ message, recipient, nonce: fromBase64(nonce) });
        const tokens = await pub<Tokens>('/v1/auth/wallet', 'POST', { accountId: signed.accountId, publicKey: signed.publicKey, signature: signed.signature, nonce });
        setAccountId(await finish(tokens));
        setStatus('signed-in');
      },
      signInDev: async (id: string) => {
        const tokens = await pub<Tokens>('/v1/auth/dev', 'POST', { accountId: id.trim() });
        setAccountId(await finish(tokens));
        setStatus('signed-in');
      },
      signOut: async () => {
        await endSession();
        await getConnector()
          .then((c) => c.disconnect())
          .catch(() => undefined);
        qc.clear();
        setAccountId(null);
        setStatus('signed-out');
      },
    }),
    [status, accountId, devServer, qc],
  );

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}
