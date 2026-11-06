import { DOCUMENT_SCHEMA_VERSION, type PlayerDoc, type SettingsDoc } from '@farmverse/contracts';
import { fail } from '../core/errors';
import type { CatalogueDoc } from '@farmverse/contracts';
import { newShop } from './shop';
import { paths } from '../infra/paths';
import type { UnitOfWork } from '../infra/unitOfWork';

export interface NewPlayer {
  readonly displayName: string;
  readonly avatarId: string;
}

/** Short player numbers start here, so every one has five digits or more. */
export const FIRST_PLAYER_NUMBER = 10_000;

/** The lower-cased key of the `usernames/` uniqueness index. */
export const usernameKey = (displayName: string) => displayName.trim().toLowerCase().replace(/\s+/g, '_');

/** Everyone's starting preferences. */
export function defaultSettings(): SettingsDoc {
  return {
    schemaVersion: DOCUMENT_SCHEMA_VERSION,
    allowVisitors: true,
    pushEnabled: true,
    quietStart: 22,
    quietEnd: 8,
    locale: 'en',
    timezone: 'UTC',
    reduceMotion: false,
    textScale: 100,
    music: true,
    sound: true,
    vibration: true,
    hints: true,
    analyticsConsent: false,
    termsAcceptedAt: 0,
  };
}

/**
 * `player-create` inside one transaction: claim the name, take the next short
 * number, write the player and their settings. The island arrives with the
 * first `island-sync`.
 */
export async function createPlayer(uow: UnitOfWork, uid: string, nowMs: number, input: NewPlayer, cat?: CatalogueDoc): Promise<void> {
  const displayName = input.displayName.trim();
  const nameKey = usernameKey(displayName);
  const [existing, taken, numbers] = await uow.readAll([paths.player(uid), paths.username(nameKey), paths.playerNumbers()]);
  if (existing?.exists) fail('PLAYER_EXISTS');
  if (taken?.exists) fail('NAME_TAKEN');
  const shortNumber = Math.max(FIRST_PLAYER_NUMBER, Number(numbers?.data?.['next'] ?? FIRST_PLAYER_NUMBER));

  const player: PlayerDoc = {
    schemaVersion: DOCUMENT_SCHEMA_VERSION,
    rev: 1,
    displayName,
    avatarId: input.avatarId,
    shortId: String(shortNumber),
    lastSeenAt: nowMs,
    createdAt: nowMs,
  };
  uow.set(paths.player(uid), player);
  uow.set(paths.username(nameKey), { uid });
  uow.set(paths.playerNumbers(), { next: shortNumber + 1 });
  uow.set(paths.settings(uid), defaultSettings());
  uow.set(paths.wallet(uid), { gems: cat?.startingGems ?? 0, updatedAt: nowMs });
  if (cat) uow.set(paths.shop(uid), newShop(cat, nowMs, nowMs));
}
