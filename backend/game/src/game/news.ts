import { NewsDoc, type SettingsDoc } from '@farmverse/contracts';
import { paths } from '../infra/paths';
import { DAY_END_HOUR, DAY_MS, DAY_START_HOUR, localHourAt, MINUTE_MS, minuteOf, nextGoodTime, readPresence, sendDirect } from './notify';
import { inQuietHours } from './push';
import type { NudgeOutcome } from './engagement';
import type { RaidDeps } from './raids';

/**
 * News as a push (module: engagement). The team publishes a news item with `push: true`
 * (tools/publishNews.ts --push); every player seen in the last 30 days who left News on gets one push,
 * in their language, at a good moment for them: soon if it's daytime where they are, else at their
 * usual hour. Each push still goes through the gate (not while playing, quiet hours, the daily cap).
 */

/** Only players who played this recently hear about news. */
export const NEWS_AUDIENCE_MS = 30 * DAY_MS;
/** A news push that couldn't go within this long after publishing is dropped (it's old news). */
export const NEWS_FRESH_MS = 3 * DAY_MS;
const PAGE = 300;
const MAX_PAGES = 100;
/** Pushes spread over this long when it's daytime for the player, so they don't all land at once. */
const SPREAD_MS = 30 * MINUTE_MS;

/** A published item's words in the player's language (English otherwise). */
export function newsText(item: NewsDoc, locale: string): { title: string; body: string } {
  const language = locale.split(/[-_]/)[0] ?? 'en';
  const t = item.i18n?.[locale] ?? item.i18n?.[language];
  return t ?? { title: item.title, body: item.body };
}

/** Lock screens show a line or two: cut long bodies at a word. */
export function shortBody(body: string, max = 140): string {
  if (body.length <= max) return body;
  const cut = body.slice(0, max - 1);
  const space = cut.lastIndexOf(' ');
  return (space > max * 0.6 ? cut.slice(0, space) : cut).trimEnd() + '…';
}

/** A news item was published: book one push for each recent player who wants news. Returns how many. */
export async function fanOutNews(deps: RaidDeps, newsId: string, raw: unknown): Promise<number> {
  const parsed = NewsDoc.safeParse(raw);
  if (!parsed.success || parsed.data.push !== true) return 0;
  const { env, tasks } = deps;
  const nowMs = env.clock.nowMs();
  let booked = 0;
  let before = Number.MAX_SAFE_INTEGER;
  for (let page = 0; page < MAX_PAGES; page++) {
    const rows = await env.store.query({
      collection: 'players',
      where: [
        ['lastSeenAt', '>', nowMs - NEWS_AUDIENCE_MS],
        ['lastSeenAt', '<', before],
      ],
      orderBy: ['lastSeenAt', 'desc'],
      limit: PAGE,
    });
    await Promise.all(
      rows.map(async (row) => {
        const uid = row.path.split('/')[1]!;
        const [settingsSnap, presenceSnap] = await Promise.all([env.store.read(paths.settings(uid)), env.store.read(paths.presence(uid))]);
        const settings = settingsSnap.data as Partial<SettingsDoc> | undefined;
        if (settings?.pushEnabled !== true || settings.pushTopics?.['news'] === false) return;
        const presence = readPresence(presenceSnap.data);
        const minute = minuteOf(uid);
        const hour = localHourAt(nowMs, presence?.offsetMin ?? 0);
        const daytime = hour >= DAY_START_HOUR && hour < DAY_END_HOUR && !inQuietHours(hour, settings.quietStart ?? 22, settings.quietEnd ?? 8);
        const at = daytime ? nowMs + Math.round((minute / 50) * SPREAD_MS) : nextGoodTime(nowMs, presence, settings, minute);
        try {
          await tasks.schedule(uid, { kind: 'news', stage: newsId }, at);
          booked++;
        } catch (e) {
          env.log.warn('news push not booked', { uid, error: String(e) });
        }
      }),
    );
    if (rows.length < PAGE) break;
    before = Number(rows[rows.length - 1]!.data['lastSeenAt'] ?? 0);
  }
  env.log.info('news push booked', { newsId, players: booked });
  return booked;
}

/** A booked news push, checked again: the item still there, still fresh, the player still wants it. */
export async function newsTask(deps: RaidDeps, uid: string, newsId: string): Promise<NudgeOutcome> {
  const { env, messenger } = deps;
  if (!newsId) return 'not_needed';
  const parsed = NewsDoc.safeParse((await env.store.read(paths.newsItem(newsId))).data);
  if (!parsed.success || parsed.data.push !== true) return 'not_needed';
  if (env.clock.nowMs() - parsed.data.publishedAt > NEWS_FRESH_MS) return 'not_needed';
  const settings = (await env.store.read(paths.settings(uid))).data as Partial<SettingsDoc> | undefined;
  const { title, body } = newsText(parsed.data, settings?.locale ?? 'en');
  return sendDirect(env, messenger, uid, 'news', { title, body: shortBody(body) });
}
