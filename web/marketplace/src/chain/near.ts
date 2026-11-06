/**
 * NEAR access: one NearConnector (wallets) and one RPC provider (reads). Created lazily so the mock
 * mode never loads wallet manifests or touches the network.
 */
import type { NearConnector } from '@hot-labs/near-connect';
import type { JsonRpcProvider } from 'near-api-js';
import { config } from '../config';

let connector: Promise<NearConnector> | null = null;
let rpc: Promise<JsonRpcProvider> | null = null;

export function getConnector(): Promise<NearConnector> {
  connector ??= import('@hot-labs/near-connect').then(
    ({ NearConnector }) =>
      new NearConnector({
        network: config.network,
        // MyNearWallet, Meteor, HOT, Intear, … from the NEAR Connect manifest.
        footerBranding: null,
        autoConnect: true,
      }),
  );
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
