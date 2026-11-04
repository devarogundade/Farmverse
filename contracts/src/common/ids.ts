import { z } from 'zod';

/**
 * Identifier formats. Every id is validated by shape before any read, so a
 * malformed id can never reach a Firestore path.
 *
 * Server-generated ids carry a prefix that says what they are (`n_`
 * notification) followed by 8–24 URL-safe characters.
 */

const serverId = (prefix: string) =>
  z.string().regex(new RegExp(`^${prefix}_[A-Za-z0-9]{8,24}$`));

/** A Firebase Auth uid. */
export const Uid = z.string().regex(/^[A-Za-z0-9]{20,128}$/);

/** A catalogue id: lowercase snake case with a type prefix, e.g. `seed_wheat`, `machine_mill`. */
export const CatalogueId = z.string().regex(/^[a-z][a-z0-9]*(?:_[a-z0-9]+)+$/).max(48);

/** `n_…`: an inbox row. */
export const NotificationId = serverId('n');

/** `r_…`: a reward to collect. */
export const RewardId = serverId('r');

/** A Firebase installation id (push token owner). */
export const InstallationId = z.string().regex(/^[A-Za-z0-9_-]{8,64}$/);
