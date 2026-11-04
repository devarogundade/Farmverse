import { z } from 'zod';

/**
 * Fields every aggregate document carries (04 conventions): `schemaVersion` for
 * lazy migrations, `rev` incremented by the server on every write so the client
 * can retire optimistic overlays.
 */
export const DOCUMENT_SCHEMA_VERSION = 1;

export const SchemaVersion = z.number().int().min(1).max(1000);
export const Rev = z.number().int().min(0).max(Number.MAX_SAFE_INTEGER);
