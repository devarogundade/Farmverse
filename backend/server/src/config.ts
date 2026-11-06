import { z } from 'zod';

const csv = z
  .string()
  .default('')
  .transform((s) => s.split(',').map((v) => v.trim()).filter((v) => v.length > 0));

/** An empty value (`KEY=` in .env) counts as unset. */
const optional = <T extends z.ZodType>(t: T) => z.preprocess((v) => (v === '' ? undefined : v), t.optional());

/** Every setting the server reads, validated once at start (a bad value stops the boot). */
const Schema = z.object({
  DATABASE_URL: z.string().url(),
  REDIS_URL: z.string().url(),
  /** Base64 of the 32-byte Ed25519 seed that signs session JWTs. */
  JWT_SEED: z.string().refine((s) => Buffer.from(s, 'base64').length === 32, 'JWT_SEED must be base64 of 32 bytes'),
  ISLAND_ENGINE_URL: z.string().url().default('http://localhost:8080'),
  FARMVERSE_ENV: z.enum(['dev', 'staging', 'prod']).default('dev'),
  MIN_CLIENT_VERSION: z.string().default('0.0.0'),
  PORT: z.coerce.number().int().min(1).max(65535).default(8787),
  ALLOWED_ORIGINS: csv,
  NEAR_NETWORK: z.enum(['testnet', 'mainnet']).default('testnet'),
  NEAR_RPC_URL: z.string().url().default('https://rpc.testnet.near.org'),
  AUTH_RECIPIENT: z.string().min(1).default('play.farmverse.world'),
  ADMIN_ACCOUNTS: csv,
  /** The contracts the indexer follows and the marketplace API serves. */
  NEAR_ITEMS_CONTRACT: z.string().min(2).default('items.farmverse.testnet'),
  NEAR_MARKET_CONTRACT: z.string().min(2).default('market.farmverse.testnet'),
  /** Block feed for the indexer; defaults to neardata.xyz for NEAR_NETWORK. */
  NEARDATA_URL: optional(z.string().url()),
  /** FastNEAR subscription key for neardata (the free tier allows 180 requests a minute). */
  NEARDATA_API_KEY: optional(z.string().min(8)),
  /** Requests a minute the indexer makes to neardata; raise it with an API key. */
  NEARDATA_RPM: optional(z.coerce.number().int().min(10).max(100_000)),
  /** First block the indexer reads on its very first run: the contracts' deploy block. Default: now. */
  INDEXER_START_BLOCK: optional(z.coerce.number().int().positive()),
  /**
   * Base64 of the 32-byte Ed25519 seed that signs mint vouchers; its public key must be registered
   * on the items contract (`add_signer`). Unset: chain-mint is refused. Production: keep it in a KMS.
   */
  MINT_SIGNER_SEED: optional(z.string().refine((s) => Buffer.from(s, 'base64').length === 32, 'MINT_SIGNER_SEED must be base64 of 32 bytes')),
  /** Market value (coins) one player may mint per UTC day. */
  MINT_DAILY_VALUE_CAP: z.coerce.number().int().min(0).default(5_000),
  /** Hours since a player started before they may mint. */
  MINT_MIN_ACCOUNT_HOURS: z.coerce.number().min(0).default(24),
  NEAR_FARM_CONTRACT: z.string().min(2).default('farm.farmverse.testnet'),
  /** Testnet faucet (chain/scripts/upgrade-testnet.sh writes these). Unset, or on mainnet: no faucet. */
  FAUCET_ACCOUNT: optional(z.string().min(2)),
  FAUCET_PRIVATE_KEY: optional(z.string().regex(/^ed25519:[1-9A-HJ-NP-Za-km-z]+$/, 'FAUCET_PRIVATE_KEY must be ed25519:<base58>')),
  /** Whole test FARM per daily claim, test NEAR once per account, claims per UTC day in total. */
  FAUCET_FARM: z.coerce.number().int().min(0).default(100),
  FAUCET_NEAR: z.coerce.number().min(0).max(5).default(1),
  FAUCET_DAILY_CAP: z.coerce.number().int().min(0).default(2_000),
  /** Pays gas for sponsored calls and welcome top-ups (plan 15 §14). Unset: the faucet account does it. */
  RELAYER_ACCOUNT: optional(z.string().min(2)),
  RELAYER_PRIVATE_KEY: optional(z.string().regex(/^ed25519:[1-9A-HJ-NP-Za-km-z]+$/, 'RELAYER_PRIVATE_KEY must be ed25519:<base58>')),
  /** NEAR a new account gets once at sign-in when it holds less than WELCOME_BELOW_NEAR (0 = off). */
  WELCOME_NEAR: z.coerce.number().min(0).max(1).default(0.05),
  WELCOME_BELOW_NEAR: z.coerce.number().min(0).max(1).default(0.02),
  /** Social login (plan 15 §13.2): the Privy app; unset = no Privy wallet. */
  PRIVY_APP_ID: optional(z.string().min(5)),
  PRIVY_APP_SECRET: optional(z.string().min(10)),
  /** The app's verification key (SPKI PEM; 
 for newlines in .env). */
  PRIVY_VERIFICATION_KEY: optional(z.string().min(40)),
  /** Market Town merchants (plan 15 §13.1): JSON [{"name":"bea","account":"bea.x.testnet","key":"ed25519:…"}] (chain/scripts/merchants.sh). */
  MERCHANTS: optional(
    z.string().transform((s, ctx) => {
      try {
        return z.array(z.object({ name: z.string(), account: z.string(), key: z.string().startsWith('ed25519:') })).parse(JSON.parse(s));
      } catch {
        ctx.addIssue({ code: 'custom', message: 'MERCHANTS must be a JSON array of {name, account, key}' });
        return z.NEVER;
      }
    }),
  ),
});

export type Config = z.infer<typeof Schema>;

export function loadConfig(source: NodeJS.ProcessEnv = process.env): Config {
  const parsed = Schema.safeParse(source);
  if (!parsed.success) {
    const problems = parsed.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`).join('; ');
    throw new Error(`Bad server configuration: ${problems}`);
  }
  return parsed.data;
}
