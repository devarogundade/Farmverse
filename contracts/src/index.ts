/**
 * @farmverse/contracts — the single source of truth for the client/server boundary.
 *
 * The backend imports these schemas to validate every request and response.
 * `npm run gen` emits the matching C# into the Unity project; CI runs
 * `npm run check` and fails when the generated code is stale.
 */
export * from './registry';
export * from './common/primitives';
export * from './common/ids';
export * from './common/errors';
export * from './common/envelope';
export * from './callables';
export * from './documents';
export * from './push';
export * from './catalogue';

/** Bumped on every breaking contract change (see 05 §6). */
export const CONTRACT_VERSION = 1;
