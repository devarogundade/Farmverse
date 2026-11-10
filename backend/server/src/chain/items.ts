/** Small, pure helpers shared by the projector and the read API. */

/**
 * The marketplace renderer type for a contract item (web/shared/src/nft/metadata.ts `NftType`).
 * Keep in step with `ITEM_MODELS` there: animals are goods on chain but render as animals.
 */
const ANIMALS = new Set(['cow', 'chicken', 'sheep', 'pig']);

export type NftType = 'worker' | 'tractor' | 'building' | 'land' | 'good' | 'animal' | 'pack' | 'deed';
export const NFT_TYPES: readonly NftType[] = ['worker', 'tractor', 'building', 'land', 'good', 'animal', 'pack', 'deed'];

export function nftTypeOf(kind: string, itemId: string): NftType | null {
  switch (kind) {
    case 'worker':
      return 'worker';
    case 'building':
      return 'building';
    case 'land':
      return itemId === 'deed' ? 'deed' : 'land';
    case 'pack':
      return 'pack'; // passes and gem packs: flat art on the market
    case 'tool':
      return 'tractor';
    case 'good':
      return ANIMALS.has(itemId) ? 'animal' : 'good';
    default:
      return null; // boosts have no marketplace renderer yet
  }
}

/** "chicken_coop" ×1 → "Chicken coop"; "wheat" ×250 → "Wheat ×250". Used when a mint carried no title. */
export function fallbackTitle(itemId: string, quantity: number): string {
  const name = itemId.charAt(0).toUpperCase() + itemId.slice(1).replace(/_/g, ' ');
  return quantity > 1 ? `${name} ×${quantity}` : name;
}

const YOCTO = 10n ** 18n;

/** yocto-FARM → "12.5" (at most 2 decimals, for messages people read). */
export function formatFarm(yocto: string | bigint): string {
  const v = BigInt(yocto);
  const whole = v / YOCTO;
  const cents = ((v % YOCTO) * 100n) / YOCTO;
  return cents === 0n ? whole.toString() : `${whole}.${cents.toString().padStart(2, '0').replace(/0$/, '')}`;
}

/** yocto-FARM → a plain number of FARM (for charts; precision beyond ~15 digits doesn't matter there). */
export const farmNumber = (yocto: string | bigint): number => Number(BigInt(yocto) * 1_000_000n / YOCTO) / 1_000_000;

/** A JSON object from the contract's props string; anything else becomes {}. */
export function parseProps(props: unknown): Record<string, unknown> {
  if (props && typeof props === 'object' && !Array.isArray(props)) return props as Record<string, unknown>;
  if (typeof props !== 'string') return {};
  try {
    const v = JSON.parse(props) as unknown;
    return v && typeof v === 'object' && !Array.isArray(v) ? (v as Record<string, unknown>) : {};
  } catch {
    return {};
  }
}

/** A long wallet name (an implicit account, a 0x address) as its first and last four characters, as the market and game show it. */
export const shortAccount = (a: string) => (a.length > 24 ? `${a.slice(0, 4)}…${a.slice(-4)}` : a);
