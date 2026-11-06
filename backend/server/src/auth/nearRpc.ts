/** The few NEAR RPC reads the server needs. An interface so tests run without a network. */
export interface NearRpc {
  /** Whether `publicKey` is a full-access key of `accountId` right now. */
  isFullAccessKey(accountId: string, publicKey: string): Promise<boolean>;
}

export function httpNearRpc(url: string, fetchImpl: typeof fetch = fetch): NearRpc {
  return {
    async isFullAccessKey(accountId, publicKey) {
      const response = await fetchImpl(url, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          jsonrpc: '2.0',
          id: 'farmverse',
          method: 'query',
          params: { request_type: 'view_access_key', finality: 'final', account_id: accountId, public_key: publicKey },
        }),
        signal: AbortSignal.timeout(8_000),
      });
      if (!response.ok) throw new Error(`NEAR RPC answered ${response.status}`);
      const body = (await response.json()) as { result?: { permission?: unknown }; error?: unknown };
      // Unknown account or key comes back as an error: not a full-access key.
      if (body.error || !body.result) return false;
      return body.result.permission === 'FullAccess';
    },
  };
}
