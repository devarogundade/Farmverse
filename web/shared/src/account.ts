/**
 * Wallet names for display. A long one (a 64-character implicit NEAR account from email sign-in,
 * a 0x address) shows as its first and last four characters, "1ef6…f1b0"; a short one such as
 * bea.farmverse.testnet shows whole. Copy, links and tooltips keep the full name.
 * The game (GameUi.Chain.cs) and the server's notes (market/read.ts) do the same.
 */
export const LONG_ACCOUNT = 24;

export function shortAccount(account: string | null | undefined): string {
  if (!account) return '';
  return account.length > LONG_ACCOUNT ? `${account.slice(0, 4)}…${account.slice(-4)}` : account;
}
