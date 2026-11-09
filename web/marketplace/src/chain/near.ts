/**
 * NEAR access: one NearConnector (wallets) and one RPC provider (reads), created on first use so
 * the first paint doesn't wait for wallet manifests.
 */
import type { NearConnector } from '@hot-labs/near-connect';
import { liftWalletPopups } from '@farmverse/shared/walletLayer';
import type { JsonRpcProvider } from 'near-api-js';
import { config } from '../config';
import { registerPrivyWallet, socialLogin } from './privyWallet';

let connector: Promise<NearConnector> | null = null;
let rpc: Promise<JsonRpcProvider> | null = null;

export function getConnector(): Promise<NearConnector> {
  connector ??= import('@hot-labs/near-connect').then(async ({ NearConnector }) => {
    // the wallet list opens above our <dialog> popups, not under them
    liftWalletPopups();
    const c = new NearConnector({
      network: config.network,
      // MyNearWallet, Meteor, HOT, Intear, … from the NEAR Connect manifest.
      footerBranding: null,
      autoConnect: true,
    });
    // social login: "Email or Google" next to them, when the Privy app is set (plan 15 §13.2)
    if (socialLogin) await registerPrivyWallet(c, location.origin);
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
