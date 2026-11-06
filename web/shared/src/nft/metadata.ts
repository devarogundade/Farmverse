/**
 * Farmverse NFT metadata — what the marketplace needs to render a token in 3D.
 *
 * On chain it lives in the items contract state and is mirrored into NEP-177 `extra` (a JSON
 * string), so `parseMetadata` accepts either the object or that string.
 *
 *   { type: "worker",  outfit: { hair: "Hairstyle_Male_005", top: "Outwear_043", glove: "Gloves_006" } }
 *   { type: "tractor", item_id: "tractor_red" }
 *   { type: "good",    item_id: "wheat", quantity: 250 }
 *
 * `type` picks the renderer, `item_id` picks the model for non-workers, `outfit` dresses a worker
 * (each slot names one part from public/models/character.parts.json), `quantity` sizes a stack.
 */
import parts from '../../public/models/character.parts.json' with { type: 'json' };

export const NFT_TYPES = ['worker', 'tractor', 'building', 'land', 'good', 'animal'] as const;
export type NftType = (typeof NFT_TYPES)[number];

export const OUTFIT_SLOTS = [
  'hair', 'hat', 'face', 'glasses', 'accessory', 'top', 'bottom', 'glove', 'socks', 'shoes', 'costume',
] as const;
export type OutfitSlot = (typeof OUTFIT_SLOTS)[number];
export type Outfit = Partial<Record<OutfitSlot, string>>;

export interface NftMetadata {
  type: NftType;
  /** Model id for every type except `worker` (see ITEM_MODELS). */
  item_id?: string;
  /** Units held by the token; goods only. Fixed at mint. */
  quantity?: number;
  /** Worker look: one part id per slot. Missing slots are left empty. */
  outfit?: Outfit;
  /** Small item-specific traits (quality, speed, …). Shown, not rendered. */
  props?: Record<string, string | number | boolean>;
  minted_from?: string;
  minted_at?: number;
}

/** Every part id the character model ships, by slot. */
export const OUTFIT_PARTS = parts.slots as Record<OutfitSlot, string[]>;
export const CHARACTER_CLIPS = parts.clips;

/** Item NFT models (public/models/items/<id>.glb) and the type each belongs to. */
export const ITEM_MODELS: Record<string, { type: Exclude<NftType, 'worker'>; label: string }> = {
  tractor_red: { type: 'tractor', label: 'Red Tractor' },
  tractor_blue: { type: 'tractor', label: 'Blue Tractor' },
  tractor_green: { type: 'tractor', label: 'Green Tractor' },
  tractor_gold: { type: 'tractor', label: 'Blue & Gold Tractor' },
  harvester: { type: 'tractor', label: 'Combine Harvester' },
  farmhouse: { type: 'building', label: 'Farmhouse' },
  cottage: { type: 'building', label: 'Cottage' },
  greenhouse: { type: 'building', label: 'Greenhouse' },
  coop: { type: 'building', label: 'Chicken Coop' },
  barn: { type: 'building', label: 'Barn' },
  silo: { type: 'building', label: 'Silo' },
  apiary: { type: 'building', label: 'Apiary' },
  field: { type: 'land', label: 'Field Plot' },
  garden_bed: { type: 'land', label: 'Garden Bed' },
  wheat: { type: 'good', label: 'Wheat' },
  flour: { type: 'good', label: 'Flour' },
  milk: { type: 'good', label: 'Milk' },
  egg: { type: 'good', label: 'Eggs' },
  honey: { type: 'good', label: 'Honey' },
  tomato: { type: 'good', label: 'Tomato' },
  cow: { type: 'animal', label: 'Cow' },
  chicken: { type: 'animal', label: 'Chicken' },
  sheep: { type: 'animal', label: 'Sheep' },
  pig: { type: 'animal', label: 'Pig' },
};

export const itemModelUrl = (itemId: string) => `/models/items/${itemId}.glb`;
export const CHARACTER_URL = '/models/character.glb';

export type ParseResult = { ok: true; value: NftMetadata; warnings: string[] } | { ok: false; error: string };

/**
 * Validate metadata from the chain, the indexer or a user. Unknown outfit parts and slots are
 * dropped with a warning (so an old client still renders a newer token); a bad `type` or a missing
 * model is an error.
 */
export function parseMetadata(raw: unknown): ParseResult {
  let data = raw;
  if (typeof data === 'string') {
    try {
      data = JSON.parse(data);
    } catch (e) {
      return { ok: false, error: `Not valid JSON: ${(e as Error).message}` };
    }
  }
  if (!data || typeof data !== 'object' || Array.isArray(data)) return { ok: false, error: 'Metadata must be an object.' };
  const m = data as Record<string, unknown>;
  const warnings: string[] = [];

  if (!NFT_TYPES.includes(m.type as NftType)) {
    return { ok: false, error: `"type" must be one of ${NFT_TYPES.join(', ')}.` };
  }
  const type = m.type as NftType;
  const value: NftMetadata = { type };

  if (type === 'worker') {
    const outfit: Outfit = {};
    if (m.outfit != null) {
      if (typeof m.outfit !== 'object' || Array.isArray(m.outfit)) return { ok: false, error: '"outfit" must be an object.' };
      for (const [slot, part] of Object.entries(m.outfit as Record<string, unknown>)) {
        if (!OUTFIT_SLOTS.includes(slot as OutfitSlot)) {
          warnings.push(`Unknown outfit slot "${slot}" ignored.`);
        } else if (part === '' || part == null) {
          // empty slot
        } else if (typeof part !== 'string' || !OUTFIT_PARTS[slot as OutfitSlot]?.includes(part)) {
          warnings.push(`"${String(part)}" is not a ${slot} part; slot left empty.`);
        } else {
          outfit[slot as OutfitSlot] = part;
        }
      }
    }
    value.outfit = outfit;
  } else {
    const id = m.item_id;
    if (typeof id !== 'string' || !ITEM_MODELS[id]) {
      return { ok: false, error: `"item_id" must name a model: ${Object.keys(ITEM_MODELS).filter((k) => ITEM_MODELS[k].type === type).join(', ')}.` };
    }
    if (ITEM_MODELS[id].type !== type) warnings.push(`"${id}" is a ${ITEM_MODELS[id].type}, not a ${type}.`);
    value.item_id = id;
  }

  if (m.quantity != null) {
    const q = Number(m.quantity);
    if (!Number.isInteger(q) || q < 1) return { ok: false, error: '"quantity" must be a whole number ≥ 1.' };
    value.quantity = q;
  }
  if (m.props && typeof m.props === 'object' && !Array.isArray(m.props)) value.props = m.props as NftMetadata['props'];
  if (typeof m.minted_from === 'string') value.minted_from = m.minted_from;
  if (typeof m.minted_at === 'number') value.minted_at = m.minted_at;

  return { ok: true, value, warnings };
}

/** Display name of a token's model ("Red Tractor", "Worker"). */
export function modelLabel(meta: NftMetadata): string {
  if (meta.type === 'worker') return 'Worker';
  return (meta.item_id && ITEM_MODELS[meta.item_id]?.label) || meta.type;
}

/** What people see instead of part ids. */
const PART_NAMES: Record<string, string> = {
  Hairstyle_Male_001: 'Short crop', Hairstyle_Male_005: 'Messy mop', Hairstyle_Male_Single_006: 'Swoop',
  Hat_010: 'Sun hat', Hat_Single_008: 'Beanie', Hat_Single_013: 'Visor hat', Hat_Single_016: 'Cap',
  Male_emotion_usual_001: 'Calm', Male_emotion_happy_002: 'Happy', Male_emotion_angry_003: 'Grumpy',
  Glasses_004: 'Round glasses', Glasses_006: 'Shades',
  Clown_nose_001: 'Clown nose', Headphones_002: 'Headphones', Mustache_003: 'Mustache', Mustache_011: 'Big mustache', Pacifier_001: 'Pacifier',
  Outwear_004: 'T-shirt', Outwear_043: 'Puffer jacket', Outwear_050: 'Sweater', Outfit_010: 'Jumpsuit',
  Pants_009: 'Trousers', Pants_010: 'Cargo pants', Shorts_003: 'Shorts',
  Gloves_006: 'Work gloves', Gloves_014: 'Gloves', Socks_008: 'Socks',
  Shoe_Slippers_002: 'Slippers', Shoe_Slippers_005: 'Slides', Shoe_Sneakers_009: 'Sneakers',
  Costume_13_001: 'Clown suit', Costume_13_002: 'Jester suit', Mascot_002: 'Mouse mascot',
};
export const partLabel = (id: string) => PART_NAMES[id] ?? id.replace(/_/g, ' ');

export const SLOT_LABEL: Record<OutfitSlot, string> = {
  hair: 'Hair', hat: 'Hat', face: 'Face', glasses: 'Glasses', accessory: 'Extra', top: 'Top', bottom: 'Bottom',
  glove: 'Gloves', socks: 'Socks', shoes: 'Shoes', costume: 'Costume',
};

export const TYPE_LABEL: Record<NftType, string> = {
  worker: 'Workers', tractor: 'Machines', building: 'Buildings', land: 'Land', good: 'Goods', animal: 'Animals',
};
export const TYPE_SINGULAR: Record<NftType, string> = {
  worker: 'Worker', tractor: 'Machine', building: 'Building', land: 'Land', good: 'Goods', animal: 'Animal',
};

/** Short number people see: "w-1245" -> "1245", "g-01JD7Q3K8M" -> "Q3K8M". */
export const displayNo = (tokenId: string) => {
  const rest = tokenId.replace(/^[a-z]-/, '');
  return /^\d+$/.test(rest) ? rest : rest.slice(-5);
};

/** "speed" -> "Speed", "minted_from" -> "Minted from". */
export const traitLabel = (key: string) => key.charAt(0).toUpperCase() + key.slice(1).replace(/_/g, ' ');
