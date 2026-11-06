/** FARM amounts: 18 decimals on chain (chain/contracts/farm_token), plain numbers in the UI. */
export const FARM_DECIMALS = 18;
const ONE = 10n ** BigInt(FARM_DECIMALS);

/** "12.5" or 12.5 → "12500000000000000000" (yocto-FARM string, as the contracts take it). */
export function toYocto(farm: number | string): string {
  const [whole, frac = ''] = String(farm).trim().split('.');
  if (!/^\d+$/.test(whole || '0') || !/^\d*$/.test(frac)) throw new Error(`Not an amount: ${farm}`);
  const padded = (frac + '0'.repeat(FARM_DECIMALS)).slice(0, FARM_DECIMALS);
  return (BigInt(whole || '0') * ONE + BigInt(padded || '0')).toString();
}

/** yocto-FARM string → number of FARM (fine for display; keep yocto strings for anything sent on chain). */
export function fromYocto(yocto: string | bigint): number {
  const v = BigInt(yocto);
  const whole = v / ONE;
  const frac = (v % ONE).toString().padStart(FARM_DECIMALS, '0').slice(0, 6);
  return Number(`${whole}.${frac}`);
}

/** NEAR → yoctoNEAR string (24 decimals) for deposits. */
export function nearToYocto(near: string): string {
  const [whole, frac = ''] = near.split('.');
  return (BigInt(whole || '0') * 10n ** 24n + BigInt((frac + '0'.repeat(24)).slice(0, 24) || '0')).toString();
}

export const ONE_YOCTO = '1';
export const TGAS = (n: number) => (BigInt(n) * 10n ** 12n).toString();
