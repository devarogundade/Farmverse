/**
 * NEAR access: one NearConnector (wallets) and one RPC provider (reads), created on first use so
 * the first paint doesn't wait for wallet manifests.
 */
import type { NearConnector } from '@hot-labs/near-connect';
import { liftWalletPopups } from '@farmverse/shared/walletLayer';
import type { JsonRpcProvider } from 'near-api-js';
import { config } from '../config';
import type { PrivyNear } from './privyNear';
import { socialLogin } from './privyWallet';

let connector: Promise<NearConnector> | null = null;
let rpc: Promise<JsonRpcProvider> | null = null;
let privyNear: Promise<PrivyNear | null> | null = null;

/**
 * The "Email or Google" wallet (Privy, signing in this page: no popup), or null when the Privy app isn't set. Loaded
 * on first use, like the connector.
 */
export function getPrivyNear(): Promise<PrivyNear | null> {
  privyNear ??=
    socialLogin && config.gameApiUrl
      ? import('./privyNear').then(({ createPrivyNear }) => {
          return createPrivyNear({
            appId: import.meta.env.VITE_PRIVY_APP_ID ?? '',
            clientId: import.meta.env.VITE_PRIVY_CLIENT_ID ?? '',
            gameApiUrl: config.gameApiUrl!,
            rpcUrls: { [config.network]: config.rpcUrl },
            icon: `${location.origin}/ui/Icon_Clover.webp`,
          });
        })
      : Promise.resolve(null);
  return privyNear;
}

export function getConnector(): Promise<NearConnector> {
  connector ??= Promise.all([import('@hot-labs/near-connect'), getPrivyNear()]).then(([{ NearConnector }, privy]) => {
    // the wallet list opens above our <dialog> popups, not under them
    liftWalletPopups();
    const c = new NearConnector({
      network: config.network,
      // MyNearWallet, Meteor, HOT, Intear, … from the NEAR Connect manifest.
      footerBranding: null,
      autoConnect: true,
    });
    // social login: "Email or Google" next to them (plan 15 §13.2), handed over before a saved session is looked up
    privy?.inject();
    return c;
  });
  return connector;
}

export function getRpc(): Promise<JsonRpcProvider> {
  rpc ??= import('near-api-js').then(({ JsonRpcProvider }) => new JsonRpcProvider({ url: config.rpcUrl }));
  return rpc;
}

/** A read-only contract call (no wallet, no gas). */
export async function view<T>(contractId: string, method: string, args: Record<string, unknown> = {}): Promise<T> {
  const result = await (await getRpc()).callFunction<T & object>({ contractId, method, args, finality: 'optimistic' } as never);
  return result as T;
}
