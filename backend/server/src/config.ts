import { z } from 'zod';

const csv = z
  .string()
  .default('')
  .transform((s) => s.split(',').map((v) => v.trim()).filter((v) => v.length > 0));

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
