import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { getConnector } from './near';

export interface WalletState {
  /** Signed-in NEAR account, or null. */
  accountId: string | null;
  /** False until a saved session has been checked (avoid flashing "Connect"). */
  ready: boolean;
  /** Opens NEAR Connect's wallet picker; resolves with the account once one is connected. */
  connect: () => Promise<string>;
  disconnect: () => Promise<void>;
}

const WalletContext = createContext<WalletState | null>(null);

export function useWallet(): WalletState {
  const w = useContext(WalletContext);
  if (!w) throw new Error('useWallet outside WalletProvider');
  return w;
}

/** NEAR wallets through NEAR Connect (MyNearWallet, Meteor, HOT, Intear, …). */
export function WalletProvider({ children }: { children: ReactNode }) {
  const [accountId, setAccountId] = useState<string | null>(null);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    let live = true;
    getConnector().then(async (c) => {
      c.on('wallet:signIn', (e) => live && setAccountId(e.accounts[0]?.accountId ?? null));
      c.on('wallet:signOut', () => live && setAccountId(null));
      try {
        const { accounts } = await c.getConnectedWallet();
        if (live) setAccountId(accounts[0]?.accountId ?? null);
      } catch {
        // no saved session
      } finally {
        if (live) setReady(true);
      }
    });
    return () => {
      live = false;
    };
  }, []);

  const value = useMemo<WalletState>(
    () => ({
      accountId,
      ready,
      connect: async () => {
        const c = await getConnector();
        const w = await c.connect(); // NEAR Connect's own wallet picker
        const [acc] = await w.getAccounts();
        if (!acc) throw new Error('No account was connected.');
        setAccountId(acc.accountId);
        return acc.accountId;
      },
      disconnect: async () => {
        const c = await getConnector();
        await c.disconnect();
        setAccountId(null);
      },
    }),
    [accountId, ready],
  );
  return <WalletContext.Provider value={value}>{children}</WalletContext.Provider>;
}
