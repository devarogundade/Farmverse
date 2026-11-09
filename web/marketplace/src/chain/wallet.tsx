import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { getConnector } from './near';
import { PRIVY_WALLET_ID } from './privyWallet';
import { offerSignInHint, type SignInHint } from './signInHint';

/** How the player signs in: Google or their email (our social wallet), or a NEAR wallet from the list. */
export type SignInWay = SignInHint | { method: 'wallet' };

export interface WalletState {
  /** Signed-in NEAR account, or null. */
  accountId: string | null;
  /** False until a saved session has been checked (avoid flashing "Connect"). */
  ready: boolean;
  /**
   * Signs in the chosen way: Google or email open our sign page in a popup (it skips straight to that way), a NEAR
   * wallet opens NEAR Connect's wallet list. Resolves with the account once one is connected.
   */
  connect: (way?: SignInWay) => Promise<string>;
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
      connect: async (way = { method: 'wallet' }) => {
        const c = await getConnector();
        let w;
        if (way.method === 'wallet') w = await c.connect(); // NEAR Connect's own wallet list
        else {
          offerSignInHint(way, location.origin);
          try {
            w = await c.connect({ walletId: PRIVY_WALLET_ID });
          } finally {
            offerSignInHint(null, location.origin);
          }
        }
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
