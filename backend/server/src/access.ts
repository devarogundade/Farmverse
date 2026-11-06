/**
 * What a signed-in player may read, list and change directly — the port of
 * backend/firestore.rules. Everything not allowed here is server-only. All game writes go through
 * calls; the only direct writes are the player's own settings and marking a notification read.
 */

const PLAYER_STATE = new Set(['island', 'wallet', 'inventory', 'shop', 'goals', 'daily', 'pass', 'purchases']);
const OWN_LISTS = new Set(['friends', 'rewards', 'coupons', 'notifications']);

/** May `uid` read the document at `path`? */
export function canRead(uid: string, path: string): boolean {
  const s = path.split('/');
  if (s[0] === 'players' && s[1] === uid) {
    if (s.length === 2) return true;
    if (s.length === 4 && s[2] === 'state') return PLAYER_STATE.has(s[3]!);
    if (s.length === 4 && OWN_LISTS.has(s[2]!)) return true;
    if (s.length === 4 && s[2] === 'private' && s[3] === 'settings') return true;
    return false;
  }
  if (s.length === 2 && s[0] === 'config' && s[1] === 'catalogue') return true;
  if (s.length === 2 && s[0] === 'news') return true;
  if (s.length === 4 && s[0] === 'leaderboards' && s[2] === 'players') return true;
  return false;
}

/** The largest page `uid` may list from `collection`, or 0 when listing is not allowed. */
export function listLimit(uid: string, collection: string): number {
  const s = collection.split('/');
  if (s.length === 3 && s[0] === 'players' && s[1] === uid && OWN_LISTS.has(s[2]!)) return 50;
  if (s.length === 3 && s[0] === 'leaderboards' && s[2] === 'players') return 50;
  if (s.length === 1 && s[0] === 'news') return 20;
  return 0;
}

const SETTINGS_KEYS = new Set([
  'allowVisitors', 'pushEnabled', 'quietStart', 'quietEnd', 'locale', 'timezone', 'reduceMotion', 'textScale',
  'music', 'sound', 'vibration', 'hints', 'analyticsConsent', 'termsAcceptedAt', 'pushTopics',
]);
const PUSH_TOPICS = new Set(['island', 'raids', 'rewards', 'offers', 'news']);
const FIVE_MINUTES = 300_000;

const isInt = (v: unknown, min: number, max: number) => Number.isInteger(v) && (v as number) >= min && (v as number) <= max;
const isBool = (v: unknown) => typeof v === 'boolean';

/**
 * Checks a direct field update. Returns null when allowed, else the reason. `current` is the
 * document as stored (updates apply to existing documents only).
 */
export function checkUpdate(
  uid: string,
  path: string,
  fields: Record<string, unknown>,
  current: Record<string, unknown> | undefined,
  nowMs: number,
): string | null {
  const s = path.split('/');
  if (s[0] !== 'players' || s[1] !== uid || s.length !== 4) return 'not writable';
  if (!current) return 'document does not exist';
  const keys = Object.keys(fields);
  if (keys.length === 0) return 'no fields';

  if (s[2] === 'notifications') {
    if (keys.length !== 1 || keys[0] !== 'readAt') return 'only readAt may change';
    if (current['readAt'] !== 0) return 'already read';
    const at = fields['readAt'];
    if (!Number.isInteger(at) || Math.abs((at as number) - nowMs) > FIVE_MINUTES) return 'readAt must be now';
    return null;
  }

  if (s[2] === 'private' && s[3] === 'settings') {
    for (const k of keys) if (!SETTINGS_KEYS.has(k)) return `${k} may not change`;
    const next = { ...current, ...fields };
    const problems: [boolean, string][] = [
      [isBool(next['allowVisitors']), 'allowVisitors'],
      [isBool(next['pushEnabled']), 'pushEnabled'],
      [isInt(next['quietStart'], 0, 23), 'quietStart'],
      [isInt(next['quietEnd'], 0, 23), 'quietEnd'],
      [typeof next['locale'] === 'string' && (next['locale'] as string).length <= 16, 'locale'],
      [typeof next['timezone'] === 'string' && (next['timezone'] as string).length <= 64, 'timezone'],
      [isBool(next['reduceMotion']), 'reduceMotion'],
      [isInt(next['textScale'], 80, 150), 'textScale'],
    ];
    for (const k of ['music', 'sound', 'vibration', 'hints', 'analyticsConsent']) {
      problems.push([next[k] === undefined || isBool(next[k]), k]);
    }
    const topics = next['pushTopics'];
    problems.push([
      topics === undefined ||
        (typeof topics === 'object' && topics !== null && !Array.isArray(topics) &&
          Object.entries(topics).every(([t, on]) => PUSH_TOPICS.has(t) && isBool(on))),
      'pushTopics',
    ]);
    const terms = next['termsAcceptedAt'];
    problems.push([terms === undefined || isInt(terms, 0, nowMs + FIVE_MINUTES), 'termsAcceptedAt']);
    const bad = problems.find(([ok]) => !ok);
    return bad ? `invalid ${bad[1]}` : null;
  }
  return 'not writable';
}
