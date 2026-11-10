import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { getConnector, getPrivyNear } from './near';
import { PRIVY_WALLET_ID } from './privyWallet';

/**
 * How the player signs in: Google (a trip to Google and back), an emailed code (sent first with `sendEmailCode`),
 * or a NEAR wallet from the list. Google and email are our social wallet, which signs in this page: no popup.
 */
export type SignInWay = { method: 'google' } | { method: 'email'; email: string; code: string } | { method: 'wallet' };

export interface WalletState {
  /** Signed-in NEAR account, or null. */
  accountId: string | null;
  /** False until a saved session has been checked (avoid flashing "Connect"). */
  ready: boolean;
  /** Signed in with Google or email: actions sign on this page, with nothing to approve in a wallet. */
  social: boolean;
  /** Emails the player a sign-in code (the email way's first step). */
  sendEmailCode: (email: string) => Promise<void>;
  /**
   * Signs in the chosen way. Google leaves for Google and comes back signed in (this page picks it up on load); the
   * email way checks the code; a NEAR wallet opens NEAR Connect's wallet list. Resolves with the account.
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

/** NEAR wallets through NEAR Connect (MyNearWallet, Meteor, HOT, Intear, …) and our "Email or Google" one. */
export function WalletProvider({ children }: { children: ReactNode }) {
  const [accountId, setAccountId] = useState<string | null>(null);
  const [ready, setReady] = useState(false);
  const [social, setSocial] = useState(false);

  useEffect(() => {
    let live = true;
    (async () => {
      const c = await getConnector();
      c.on('wallet:signIn', (e) => {
        if (!live) return;
        setAccountId(e.accounts[0]?.accountId ?? null);
        setSocial(e.wallet.manifest.id === PRIVY_WALLET_ID);
      });
      c.on('wallet:signOut', () => live && setAccountId(null));
      try {
        // back from Google: finish signing in with the social wallet
        const privy = await getPrivyNear();
        if (privy && (await privy.finishGoogle().catch(() => false))) {
          const w = await c.connect({ walletId: PRIVY_WALLET_ID });
          const [acc] = await w.getAccounts();
          if (live) setAccountId(acc?.accountId ?? null);
          return;
        }
        const { wallet, accounts } = await c.getConnectedWallet();
        if (live) {
          setAccountId(accounts[0]?.accountId ?? null);
          setSocial(wallet.manifest.id === PRIVY_WALLET_ID);
        }
      } catch {
        // no saved session
      } finally {
        if (live) setReady(true);
      }
    })();
    return () => {
      live = false;
    };
  }, []);

  const value = useMemo<WalletState>(
    () => ({
      accountId,
      ready,
      social,
      sendEmailCode: async (email) => {
        const privy = await getPrivyNear();
        if (!privy) throw new Error('Email sign-in is not available here.');
        await privy.sendEmailCode(email);
      },
      connect: async (way = { method: 'wallet' }) => {
        const c = await getConnector();
        let w;
        if (way.method === 'wallet') w = await c.connect(); // NEAR Connect's own wallet list
        else {
          const privy = await getPrivyNear();
          if (!privy) throw new Error('Email and Google sign-in are not available here.');
          if (way.method === 'google') {
            await privy.startGoogle(); // leaves the page; the provider finishes on the way back
            return new Promise<string>(() => {});
          }
          await privy.loginWithEmailCode(way.email, way.code);
          w = await c.connect({ walletId: PRIVY_WALLET_ID });
        }
        const [acc] = await w.getAccounts();
        if (!acc) throw new Error('No account was connected.');
        setAccountId(acc.accountId);
        setSocial(w.manifest.id === PRIVY_WALLET_ID);
        return acc.accountId;
      },
      disconnect: async () => {
        const c = await getConnector();
        await c.disconnect();
        setAccountId(null);
      },
    }),
    [accountId, ready, social],
  );
  return <WalletContext.Provider value={value}>{children}</WalletContext.Provider>;
}
