#!/usr/bin/env node
// Genesis Farmhands: generates the 500 workers (seeded, so the set is the same every run), then mints
// the first N with the studio account and lists them on the market at the price of their rarity.
// Decisions: Docs/architecture/15-game-market-economy-plan.md §7.2 and §9 D6.
//
//   node chain/scripts/farmhands.mjs --master farmverse.testnet            # generate, mint + list the first 100
//   node chain/scripts/farmhands.mjs --master farmverse.testnet --count 20 # first 20 only
//   node chain/scripts/farmhands.mjs --dry-run                              # only write chain/deploy/farmhands-genesis.json
//
// Needs (except --dry-run): near CLI with the studio account's key in the file keychain, items on v2
// (upgrade-testnet.sh). Safe to re-run: minted tokens and live listings are skipped.
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const args = Object.fromEntries(
  process.argv.slice(2).reduce((acc, a, i, all) => {
    if (a.startsWith('--')) acc.push([a.slice(2), all[i + 1] && !all[i + 1].startsWith('--') ? all[i + 1] : true]);
    return acc;
  }, []),
);
const TOTAL = 500;
const COUNT = Number(args.count ?? 100);
const DRY = Boolean(args['dry-run']);
const NETWORK = args.network ?? 'testnet';
const RPC = args.rpc ?? (NETWORK === 'mainnet' ? 'https://rpc.mainnet.near.org' : 'https://rpc.testnet.near.org');
const SIGN_WITH = process.env.SIGN_WITH ?? 'sign-with-legacy-keychain';
const E18 = 10n ** 18n;

// ---------------------------------------------------------------- the set (deterministic)

/** mulberry32: small, seeded, the same on every machine. */
function rng(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
const rand = rng(20261106);
const pick = (list) => list[Math.floor(rand() * list.length)];
const between = (lo, hi) => lo + Math.floor(rand() * (hi - lo + 1));

/** Outfit parts both the marketplace (character.parts.json) and the game (CharacterCatalog) know. */
function outfitParts() {
  const web = JSON.parse(readFileSync(join(ROOT, 'web/shared/public/models/character.parts.json'), 'utf8')).slots;
  const catalogPath = join(ROOT, 'Assets/Farmverse/Content/Resources/CharacterCatalog.asset');
  const game = existsSync(catalogPath) ? readFileSync(catalogPath, 'utf8') : null;
  const out = {};
  for (const [slot, parts] of Object.entries(web)) {
    if (slot === 'costume') continue; // a costume hides everything else: not for Farmhands
    const known = game ? parts.filter((p) => game.includes(p)) : parts;
    if (known.length) out[slot] = known;
  }
  return out;
}

const RARITY = [
  { id: 'common', share: 300, price: 40n, traits: [1, 3] },
  { id: 'rare', share: 140, price: 120n, traits: [2, 4] },
  { id: 'epic', share: 50, price: 350n, traits: [3, 5] },
  { id: 'legendary', share: 10, price: 1000n, traits: [4, 5] },
];
const SPECIALTIES = ['fields', 'animals', 'machines', 'freight'];
const NAMES = [
  'Mia', 'Leo', 'Ada', 'Kofi', 'Yara', 'Tomas', 'Ivy', 'Bayo', 'Nina', 'Omar', 'Lena', 'Ravi', 'Zoe', 'Felix', 'Amara',
  'Hugo', 'Sana', 'Jonah', 'Ines', 'Kai', 'Maya', 'Eli', 'Nora', 'Theo', 'Lila', 'Sami', 'Tara', 'Owen', 'Esme', 'Ade',
  'Rosa', 'Finn', 'Uma', 'Dara', 'Milo', 'Asha', 'Ben', 'Cleo', 'Dev', 'Elsa', 'Gus', 'Hana', 'Ike', 'Juno', 'Kemi',
  'Luca', 'Mei', 'Nico', 'Olu', 'Pia', 'Quin', 'Remy', 'Sol', 'Tobi', 'Vera', 'Wren', 'Xan', 'Yemi', 'Zara', 'Ola',
];
const OPTIONAL = new Set(['hat', 'glasses', 'accessory', 'glove', 'socks']);

function generate() {
  const parts = outfitParts();
  // rarity of each number: a seeded shuffle of the shares
  const tiers = RARITY.flatMap((r) => Array(r.share).fill(r));
  for (let i = tiers.length - 1; i > 0; i--) {
    const j = Math.floor(rand() * (i + 1));
    [tiers[i], tiers[j]] = [tiers[j], tiers[i]];
  }
  const seen = new Set();
  const set = [];
  for (let n = 1; n <= TOTAL; n++) {
    const r = tiers[n - 1];
    let outfit;
    do {
      outfit = {};
      for (const [slot, list] of Object.entries(parts)) {
        if (OPTIONAL.has(slot) && rand() < 0.5) continue;
        outfit[slot] = pick(list);
      }
      if (outfit.hat) delete outfit.hair; // hair doesn't show under a hat
    } while (seen.has(JSON.stringify(outfit)));
    seen.add(JSON.stringify(outfit));
    const name = pick(NAMES);
    const props = {
      name,
      outfit,
      specialty: pick(SPECIALTIES),
      rarity: r.id,
      traits: { speed: between(...r.traits), carry: between(...r.traits), stamina: between(...r.traits) },
      cohort: 'genesis',
      edition: n,
    };
    set.push({ token_id: `s-fh-${n}`, title: `${name} #${n}`, price: (r.price * E18).toString(), props });
  }
  return set;
}

// ---------------------------------------------------------------- chain

async function view(account, method, a) {
  const res = await fetch(RPC, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      jsonrpc: '2.0', id: 1, method: 'query',
      params: { request_type: 'call_function', finality: 'final', account_id: account, method_name: method, args_base64: Buffer.from(JSON.stringify(a)).toString('base64') },
    }),
  });
  const json = await res.json();
  if (!json.result?.result) throw new Error(`${account}.${method}: ${JSON.stringify(json.error ?? json).slice(0, 300)}`);
  return JSON.parse(Buffer.from(json.result.result).toString());
}

function call(contract, method, a, deposit, signer, gas = '100.0 Tgas') {
  execFileSync('near', [
    'contract', 'call-function', 'as-transaction', contract, method, 'json-args', JSON.stringify(a),
    'prepaid-gas', gas, 'attached-deposit', deposit, 'sign-as', signer, 'network-config', NETWORK, SIGN_WITH, 'send',
  ], { stdio: ['ignore', 'ignore', 'inherit'] });
}

async function main() {
  const set = generate();
  const outDir = join(ROOT, 'chain/deploy');
  mkdirSync(outDir, { recursive: true });
  writeFileSync(join(outDir, 'farmhands-genesis.json'), JSON.stringify(set, null, 1) + '\n');
  const tally = Object.fromEntries(RARITY.map((r) => [r.id, set.filter((f) => f.props.rarity === r.id).length]));
  console.log(`• ${set.length} Genesis Farmhands written to chain/deploy/farmhands-genesis.json`, tally);
  if (DRY) return;

  const master = args.master;
  if (!master || master === true) throw new Error('--master <account> is required (or --dry-run)');
  const items = `items.${master}`;
  const market = `market.${master}`;
  const studio = `studio.${master}`;

  const batch = set.slice(0, COUNT);
  // the market reserves 0.01 NEAR of the seller's deposit per listing
  const have = BigInt(await view(market, 'storage_balance_of', { account_id: studio }));
  const need = 10n ** 22n * BigInt(batch.length);
  if (have < need) {
    const more = need - have;
    console.log(`• studio storage deposit on the market: +${Number(more) / 1e24} NEAR`);
    call(market, 'storage_deposit', {}, `${Number(more) / 1e24} NEAR`, studio);
  }

  for (const f of batch) {
    const token = await view(items, 'nft_token', { token_id: f.token_id });
    if (!token) {
      call(items, 'studio_mint', {
        token_id: f.token_id, receiver_id: studio, kind: 'worker', item_id: 'farmhand', quantity: 1,
        props: JSON.stringify(f.props), title: f.title, media: 'items/farmhand.png',
      }, '0.03 NEAR', studio);
      console.log(`• minted ${f.token_id} ${f.title} (${f.props.rarity})`);
    } else if (token.owner_id !== studio) {
      console.log(`• ${f.token_id} already sold to ${token.owner_id}`);
      continue;
    }
    const listing = await view(market, 'get_listing', { nft_contract_id: items, token_id: f.token_id });
    if (!listing) {
      call(items, 'nft_approve', { token_id: f.token_id, account_id: market, msg: JSON.stringify({ price: f.price }) }, '0.01 NEAR', studio, '150.0 Tgas');
      console.log(`  listed for ${BigInt(f.price) / E18} FARM`);
    }
  }
  console.log(`Done: ${batch.length} Farmhands minted and listed.`);
}

main().catch((e) => {
  console.error(e.message ?? e);
  process.exit(1);
});
