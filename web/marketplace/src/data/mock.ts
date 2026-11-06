/**
 * Mock tokens and market data in the shape the read API (docs §9: GET /listings, /tokens/{id})
 * will return. Replace `src/api/market.ts` internals with fetches when the API is up.
 */
import type { NftMetadata } from '@farmverse/shared/nft/metadata';

export type EventKind = 'minted' | 'listed' | 'bought' | 'delisted' | 'redeemed';

export interface TokenEvent {
  kind: EventKind;
  account: string;
  price?: number; // FARM
  at: number; // ms
}

export interface Token {
  token_id: string;
  title: string;
  owner_id: string;
  metadata: NftMetadata;
  /** Active listing price in FARM, if listed. */
  price?: number;
  /** Daily average sale price, oldest first. */
  priceHistory: number[];
  events: TokenEvent[];
}

const DAY = 86_400_000;
const NOW = Date.UTC(2026, 10, 6, 9, 0);

function rng(seed: number) {
  return () => {
    seed = (seed * 1664525 + 1013904223) % 4294967296;
    return seed / 4294967296;
  };
}

function series(seed: number, end: number, n = 24): number[] {
  const r = rng(seed);
  const out: number[] = [];
  let v = end * (0.55 + r() * 0.25);
  for (let i = 0; i < n; i++) {
    v += (end - v) * 0.12 + (r() - 0.45) * end * 0.08;
    out.push(Math.max(0.1, +v.toFixed(2)));
  }
  out[n - 1] = end;
  return out;
}

const PEOPLE = ['jenny.near', 'floyd.near', 'markfisher.near', 'tilly.near', 'oak-and-acre.near', 'bram.near', 'ines.near', 'kofi.near'];

function events(seed: number, price: number | undefined, mintedFrom: string): TokenEvent[] {
  const r = rng(seed * 7 + 3);
  const p = (f: number) => +(Math.max(1, (price ?? 10) * f)).toFixed(2);
  const who = () => PEOPLE[Math.floor(r() * PEOPLE.length)];
  const list: TokenEvent[] = [
    { kind: 'minted', account: mintedFrom, at: NOW - 20 * DAY },
    { kind: 'listed', account: mintedFrom, price: p(0.6), at: NOW - 16 * DAY },
    { kind: 'bought', account: who(), price: p(0.6), at: NOW - 12 * DAY },
    { kind: 'bought', account: who(), price: p(0.85), at: NOW - 4 * DAY },
  ];
  if (price) list.push({ kind: 'listed', account: who(), price, at: NOW - 2 * DAY + Math.floor(r() * DAY) });
  return list.reverse();
}

function token(seed: number, token_id: string, title: string, metadata: NftMetadata, price?: number): Token {
  const mintedFrom = metadata.minted_from ?? 'farmverse.near';
  const evs = events(seed, price, mintedFrom);
  return {
    token_id,
    title,
    owner_id: evs.find((e) => e.kind === 'bought')?.account ?? mintedFrom,
    metadata: { minted_at: NOW - 20 * DAY, ...metadata, minted_from: mintedFrom },
    price,
    priceHistory: series(seed, price ?? 12),
    events: evs,
  };
}

export const TOKENS: Token[] = [
  token(1, 'w-1245', 'Bram Haybale', {
    type: 'worker',
    outfit: { hair: 'Hairstyle_Male_005', face: 'Male_emotion_happy_002', top: 'Outwear_043', bottom: 'Pants_010', glove: 'Gloves_006', shoes: 'Shoe_Sneakers_009' },
    props: { specialty: 'fields', speed: 7, stamina: 9, level: 4 },
  }, 21.3),
  token(2, 'w-0981', 'Tilly Furrow', {
    type: 'worker',
    outfit: { hair: 'Hairstyle_Male_001', hat: 'Hat_Single_013', face: 'Male_emotion_usual_001', top: 'Outwear_004', bottom: 'Shorts_003', socks: 'Socks_008', shoes: 'Shoe_Slippers_005' },
    props: { specialty: 'animals', speed: 8, stamina: 6, level: 2 },
  }, 14.8),
  token(3, 'w-0402', 'Otis Haywire', {
    type: 'worker',
    outfit: { hat: 'Hat_010', face: 'Male_emotion_angry_003', glasses: 'Glasses_004', accessory: 'Mustache_003', top: 'Outfit_010', bottom: 'Pants_009', glove: 'Gloves_014', shoes: 'Shoe_Sneakers_009' },
    props: { specialty: 'machines', speed: 5, stamina: 10, level: 6 },
  }, 32.5),
  token(4, 'w-0077', 'Juniper Beat', {
    type: 'worker',
    outfit: { hair: 'Hairstyle_Male_Single_006', face: 'Male_emotion_happy_002', accessory: 'Headphones_002', top: 'Outwear_050', bottom: 'Pants_010', shoes: 'Shoe_Slippers_002' },
    props: { specialty: 'fields', speed: 9, stamina: 5, level: 3 },
  }, 18.0),
  token(5, 'w-0013', 'Mr. Whiskers', {
    type: 'worker',
    outfit: { costume: 'Mascot_002' },
    props: { specialty: 'animals', speed: 4, stamina: 8, level: 9, edition: 'genesis' },
  }, 88.0),
  token(6, 'w-0310', 'Pip Sprocket', {
    type: 'worker',
    outfit: { face: 'Male_emotion_usual_001', costume: 'Costume_13_001', glasses: 'Glasses_006' },
    props: { specialty: 'machines', speed: 6, stamina: 7, level: 5 },
  }, 26.4),
  token(7, 's-0021', 'Red Tractor', { type: 'tractor', item_id: 'tractor_red', props: { speed: 3 } }, 45.0),
  token(8, 's-0034', 'Combine Harvester', { type: 'tractor', item_id: 'harvester', props: { speed: 2 } }, 120.0),
  token(9, 's-0102', 'Barn', { type: 'building', item_id: 'barn', props: { level: 3, storage: 400 } }, 64.0),
  token(10, 's-0140', 'Greenhouse', { type: 'building', item_id: 'greenhouse', props: { level: 1 } }, 38.5),
  token(11, 's-0007', 'Field Plot', { type: 'land', item_id: 'field', props: { size: '4×4' } }, 72.0),
  token(12, 'g-01JD7Q3K8M', 'Wheat ×250', { type: 'good', item_id: 'wheat', quantity: 250, props: { quality: 1 }, minted_from: 'tilly.near' }, 6.2),
  token(13, 'g-01JD7R9X2P', 'Milk ×40', { type: 'good', item_id: 'milk', quantity: 40, minted_from: 'bram.near' }, 4.1),
  token(14, 'g-01JD7S0A1C', 'Eggs ×120', { type: 'good', item_id: 'egg', quantity: 120, minted_from: 'ines.near' }, 3.3),
  token(15, 's-0220', 'Dairy Cow', { type: 'animal', item_id: 'cow', props: { yield: 'milk' } }, 19.9),
  token(16, 's-0231', 'Hen', { type: 'animal', item_id: 'chicken', props: { yield: 'egg' } }, 7.5),
  token(17, 's-0250', 'Blue Tractor', { type: 'tractor', item_id: 'tractor_blue', props: { speed: 4 } }, 52.0),
  token(18, 'g-01JD7T4B6E', 'Honey ×30', { type: 'good', item_id: 'honey', quantity: 30, minted_from: 'kofi.near' }),
];

/** The signed-in account in the mock (wallet sign-in comes later). */
export const ME = 'tilly.near';
