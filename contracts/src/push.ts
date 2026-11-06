import { z } from 'zod';
import { named } from './registry';
import type { NotificationKind } from './documents/history';

type Kind = z.infer<typeof NotificationKind>;

/**
 * Push copy (03 §6). Inbox rows are localized on the device from their keys;
 * a push is rendered by the OS before the game runs, so the server fills the
 * text from this table in the player's locale (English fallback). Placeholders
 * are the notification's params. Keep each line short: lock screens truncate.
 */
export const PUSH_COPY: Readonly<Record<string, Readonly<Record<Kind, { readonly title: string; readonly body: string }>>>> = {
  en: {
    system: { title: 'Farmverse', body: '{message}' },
    friend_joined: { title: 'A friend joined', body: '{name} joined your farm circle.' },
    referral_reward: { title: 'Referral reward!', body: '{name} reached level {level}: collect {coins} coins and {gems} gems.' },
    leaderboard_prize: { title: 'Weekly ranking', body: 'You finished #{rank} this week: collect {coins} coins and {gems} gems.' },
    mail: { title: '{title}', body: '{body}' },
  },
  fr: {
    system: { title: 'Farmverse', body: '{message}' },
    friend_joined: { title: 'Un ami a rejoint', body: '{name} a rejoint ton cercle de fermiers.' },
    referral_reward: { title: 'Récompense de parrainage !', body: '{name} a atteint le niveau {level} : récupère {coins} pièces et {gems} gemmes.' },
    leaderboard_prize: { title: 'Classement hebdo', body: 'Tu as fini #{rank} cette semaine : récupère {coins} pièces et {gems} gemmes.' },
    mail: { title: '{title}', body: '{body}' },
  },
  ja: {
    system: { title: 'Farmverse', body: '{message}' },
    friend_joined: { title: 'フレンドが参加', body: '{name}さんが農場仲間になりました。' },
    referral_reward: { title: '招待報酬！', body: '{name}さんがレベル{level}に到達！{coins}コインと{gems}ジェムを受け取ろう。' },
    leaderboard_prize: { title: '週間ランキング', body: '今週は{rank}位でした！{coins}コインと{gems}ジェムを受け取ろう。' },
    mail: { title: '{title}', body: '{body}' },
  },
};

/** Fill `{name}` placeholders; unknown ones stay visible so a bad template is noticed, not hidden. */
export function renderPush(locale: string, kind: Kind, params: Readonly<Record<string, string>>): { title: string; body: string } {
  const language = locale.split(/[-_]/)[0] ?? 'en';
  const copy = PUSH_COPY[locale]?.[kind] ?? PUSH_COPY[language]?.[kind] ?? PUSH_COPY['en']![kind]!;
  const fill = (text: string) => text.replace(/\{([a-zA-Z]+)\}/g, (match, name: string) => params[name] ?? match);
  return { title: fill(copy.title), body: fill(copy.body) };
}

/**
 * Pushes the server sends straight to the phone, with no inbox row: timely nudges about the island
 * (raids, storage), rewards waiting, a player who drifted away, an offer about to end. Every one of
 * them goes through the same gate on the server (opt-in, topic switch, quiet hours, not while
 * playing, a daily cap); see backend/game/src/game/notify.ts.
 */
export const PushKind = named(
  'PushKind',
  z.enum(['raid_soon', 'raid_won', 'raid_lost', 'storage_full', 'workers_hungry', 'daily_ready', 'comeback_3', 'comeback_7', 'comeback_14', 'starter_ending', 'likes', 'news']),
  'A push the server sends without an inbox row.',
);
export type PushKind = z.infer<typeof PushKind>;

/** The switches in Settings → Notifications; each push kind belongs to one. */
export const PushTopic = named('PushTopic', z.enum(['island', 'raids', 'rewards', 'offers', 'news']), 'A group of pushes the player can turn off.');
export type PushTopic = z.infer<typeof PushTopic>;

export const PUSH_TOPIC: Readonly<Record<PushKind, PushTopic>> = {
  raid_soon: 'raids',
  raid_won: 'raids',
  raid_lost: 'raids',
  storage_full: 'island',
  workers_hungry: 'island',
  daily_ready: 'rewards',
  comeback_3: 'island',
  comeback_7: 'rewards',
  comeback_14: 'rewards',
  starter_ending: 'offers',
  likes: 'island',
  news: 'news',
};

/** Where a tap on each push lands (GameUi routes). */
export const PUSH_ROUTE: Readonly<Record<PushKind, string>> = {
  raid_soon: 'play.raid',
  raid_won: 'raidresult',
  raid_lost: 'raidresult',
  storage_full: 'storage',
  workers_hungry: 'workers',
  daily_ready: 'daily',
  comeback_3: 'orders',
  comeback_7: 'inbox',
  comeback_14: 'inbox',
  starter_ending: 'shop.offers',
  likes: 'profile',
  news: 'news',
};

type Copy = { readonly title: string; readonly body: string };

export const DIRECT_COPY: Readonly<Record<string, Readonly<Record<PushKind, Copy>>>> = {
  en: {
    raid_soon: { title: 'Raiders spotted!', body: 'They land in {minutes} min. Open your farm to fight them off.' },
    raid_won: { title: 'Raid driven off!', body: 'Your warriors beat {size} raiders. Nothing was lost.' },
    raid_lost: { title: 'Your farm was raided', body: '{size} raiders hit your farm while you were away. See what they took.' },
    storage_full: { title: 'Storage is full', body: 'Your workers have nowhere to put the harvest. Sell or build more storage.' },
    workers_hungry: { title: 'Your workers ran out of food', body: 'The farm has stopped. Stock the workers\' house and they\'ll get back to work.' },
    daily_ready: { title: 'Day {day} reward is ready', body: 'Your daily gift and today\'s wheel spins are waiting.' },
    comeback_3: { title: 'Your farm needs you', body: 'Orders are piling up and the barn is filling. Come and sell!' },
    comeback_7: { title: 'A welcome-back gift', body: 'Come back to your island: {gems} gems are waiting for you.' },
    comeback_14: { title: 'Your island misses you', body: 'Your gift of {gems} gems is still waiting. Your farmers kept everything safe.' },
    starter_ending: { title: 'Starter Pack ends soon', body: '5x value, only {hours} hours left. Don\'t miss it!' },
    likes: { title: 'Farmers love your island', body: '{count} visitors liked your farm today.' },
    news: { title: '{title}', body: '{body}' },
  },
  fr: {
    raid_soon: { title: 'Pillards en vue !', body: 'Ils débarquent dans {minutes} min. Ouvre ta ferme pour les repousser.' },
    raid_won: { title: 'Raid repoussé !', body: 'Tes guerriers ont battu {size} pillards. Rien n\'a été perdu.' },
    raid_lost: { title: 'Ta ferme a été pillée', body: '{size} pillards ont attaqué pendant ton absence. Vois ce qu\'ils ont pris.' },
    storage_full: { title: 'Entrepôt plein', body: 'Tes ouvriers n\'ont plus de place pour la récolte. Vends ou construis.' },
    workers_hungry: { title: 'Tes ouvriers n\'ont plus à manger', body: 'La ferme s\'est arrêtée. Remplis la maison des ouvriers pour qu\'ils reprennent.' },
    daily_ready: { title: 'Récompense du jour {day} prête', body: 'Ton cadeau du jour et tes tours de roue t\'attendent.' },
    comeback_3: { title: 'Ta ferme a besoin de toi', body: 'Les commandes s\'accumulent et la grange se remplit. Viens vendre !' },
    comeback_7: { title: 'Un cadeau de retour', body: 'Reviens sur ton île : {gems} gemmes t\'attendent.' },
    comeback_14: { title: 'Ton île te manque', body: 'Ton cadeau de {gems} gemmes t\'attend toujours. Tes fermiers ont tout gardé.' },
    starter_ending: { title: 'Le Pack de départ se termine', body: '5x plus de valeur, plus que {hours} h. Ne le rate pas !' },
    likes: { title: 'Ton île plaît !', body: '{count} visiteurs ont aimé ta ferme aujourd\'hui.' },
    news: { title: '{title}', body: '{body}' },
  },
  ja: {
    raid_soon: { title: '略奪者を発見！', body: 'あと{minutes}分で上陸します。農場を開いて撃退しよう。' },
    raid_won: { title: '略奪を撃退！', body: '戦士たちが{size}人の略奪者を倒しました。被害はありません。' },
    raid_lost: { title: '農場が襲われました', body: '留守中に{size}人の略奪者が来ました。奪われた物を確認しよう。' },
    storage_full: { title: '倉庫がいっぱい', body: '収穫物を置く場所がありません。売るか倉庫を建てよう。' },
    workers_hungry: { title: '作業員の食料がなくなりました', body: '農場が止まっています。作業員の家に食料を入れると再開します。' },
    daily_ready: { title: '{day}日目の報酬が届いています', body: '今日のギフトとルーレットが待っています。' },
    comeback_3: { title: '農場があなたを待っています', body: '注文がたまり、納屋もいっぱいに。売りに来よう！' },
    comeback_7: { title: 'おかえりギフト', body: '島に戻ってきて！{gems}ジェムが待っています。' },
    comeback_14: { title: '島がさみしがっています', body: '{gems}ジェムのギフトはまだ待っています。農夫たちが全部守っています。' },
    starter_ending: { title: 'スターターパックまもなく終了', body: '5倍のお得、残り{hours}時間。お見逃しなく！' },
    likes: { title: 'あなたの島が人気です', body: '今日{count}人の訪問者があなたの農場にいいねしました。' },
    news: { title: '{title}', body: '{body}' },
  },
};

/** The direct push in the player's language (English fallback), placeholders filled. */
export function renderDirect(locale: string, kind: PushKind, params: Readonly<Record<string, string>>): { title: string; body: string } {
  const language = locale.split(/[-_]/)[0] ?? 'en';
  const copy = DIRECT_COPY[locale]?.[kind] ?? DIRECT_COPY[language]?.[kind] ?? DIRECT_COPY['en']![kind]!;
  const fill = (text: string) => text.replace(/\{([a-zA-Z]+)\}/g, (match, name: string) => params[name] ?? match);
  return { title: fill(copy.title), body: fill(copy.body) };
}
