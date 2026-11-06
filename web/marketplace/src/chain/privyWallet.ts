/**
 * Social login (plan 15 §13.2): a Privy wallet in near-connect, next to the usual ones. Players sign in with an
 * emailed code; the wallet signs in a popup on our own sign page (sign.html). Registered only when the Privy app
 * is set (VITE_PRIVY_APP_ID); the game page builds the same manifest (farmverse-page.js).
 */
import type { NearConnector } from '@hot-labs/near-connect';

/** The privy-near-connect executor, pinned to a commit (near-connect runs it without an integrity check). */
export const PRIVY_EXECUTOR =
  'https://raw.githubusercontent.com/beneviolabs/privy-near-connect/3d56b782669d242a1ea432921e291abc26b4ebc7/executor.js';

export function privyWalletManifest(signSite: string, allowsOpen: string[]) {
  return {
    id: 'farmverse-email',
    name: 'Email or Google',
    icon: `${signSite}/ui/Icon_Clover.webp`,
    description: 'Sign in with your email. No wallet app needed.',
    website: signSite,
    version: '1.0.0',
    executor: PRIVY_EXECUTOR,
    type: 'sandbox',
    platform: { web: signSite },
    features: {
      signMessage: true,
      signInWithoutAddKey: true,
      signInAndSignMessage: true,
      signInWithFunctionCallKey: true,
      signAndSendTransaction: true,
      signAndSendTransactions: true,
      mainnet: true,
      testnet: true,
    },
    permissions: { isPrivyConnect: true, storage: true, allowsOpen },
    metadata: { signPageURL: `${signSite}/sign.html`, debug: false },
  };
}

export async function registerPrivyWallet(connector: NearConnector, signSite: string): Promise<void> {
  try {
    await connector.registerWallet(privyWalletManifest(signSite, [signSite]) as never);
  } catch {
    // the usual wallets still work
  }
}
