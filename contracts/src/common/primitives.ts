import { z } from 'zod';

/**
 * Scalar building blocks. These are not registered as named contracts: they are
 * reused inside named objects and become plain C# scalars (`long`, `int`,
 * `string`) with the bounds documented here.
 */

/** The largest integer both C# (`long`) and TypeScript (`number`) represent exactly. */
export const MAX_SAFE = Number.MAX_SAFE_INTEGER;

/** A count of goods held. Generated as `int`. */
export const Stock = z.number().int().min(0).max(1_000_000_000);

/** A game timestamp: milliseconds since the Unix epoch. Generated as `long`. */
export const EpochMs = z.number().int().min(0).max(MAX_SAFE);

/** A semantic version, e.g. "1.4.0". */
export const SemVer = z.string().regex(/^\d{1,3}\.\d{1,3}\.\d{1,4}$/);

/** A player-facing display name. Further rules (profanity, uniqueness) are server-side. */
export const DisplayName = z
  .string()
  .min(3)
  .max(16)
  .regex(/^[\p{L}\p{N}_ ]+$/u);
