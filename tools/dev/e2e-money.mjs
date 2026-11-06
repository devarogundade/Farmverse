// End-to-end check of every server module against the running local backend (start-backend.cmd):
//   node tools/dev/e2e-money.mjs [projectId]
// Signs up fresh anonymous players each run (never touches your own farm). Needs the island engine and
// the catalogue in the emulator (start-backend.cmd does both).
const P = process.argv[2] ?? 'farmverse-8deb6';
process.env.FUNCTIONS_DIR ??= new URL('../../backend/functions', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1');
const meta = { clientVersion: '1.0.0', platform: 'editor' };
let fails = 0;
const ok = (cond, label, extra) => { console.log((cond ? 'PASS ' : 'FAIL ') + label + (extra !== undefined ? '  ' + JSON.stringify(extra) : '')); if (!cond) fails++; };
const key = () => 'k' + Math.random().toString(36).slice(2, 14).padEnd(12, '0');

const signUpOne = () => fetch('http://127.0.0.1:9099/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ returnSecureToken: true }) }).then(r => r.json());
const signUp = await signUpOne();
let token = signUp.idToken;
const uid = signUp.localId;
ok(!!token, 'anonymous sign-in', uid);

async function call(name, data) {
  const r = await fetch(`http://127.0.0.1:5001/${P}/europe-west1/${name}`, { method: 'POST', headers: { 'content-type': 'application/json', authorization: 'Bearer ' + token }, body: JSON.stringify({ data: { meta, ...data } }) });
  const j = await r.json().catch(() => ({}));
  return j.result ?? { httpStatus: r.status, ...j };
}
async function doc(path) {
  const r = await fetch(`http://127.0.0.1:8080/v1/projects/${P}/databases/(default)/documents/${path}`, { headers: { authorization: 'Bearer owner' } });
  return r.ok ? r.json() : null;
}
const int = (d, f) => Number(d?.fields?.[f]?.integerValue ?? NaN);

let r = await call('player-suggestNames', { count: 3 });
ok(r.ok && r.data.names.length > 0, 'player-suggestNames', r.data ?? r);
r = await call('player-create', { displayName: r.data.names[0], avatarId: 'avatar_default' });
ok(r.ok, 'player-create', r.error);
ok(int(await doc(`players/${uid}/state/wallet`), 'gems') === 50, 'wallet opened with 50 gems');
const shop = await doc(`players/${uid}/state/shop`);
ok(int(shop, 'starterEndsAt') > Date.now() + 47 * 3600e3, 'starter pack window ~48h');

const cat = await doc('config/catalogue');
ok(int(cat, 'version') >= 9, 'catalogue published', int(cat, 'version'));

r = await call('wallet-spend', { offerId: 'coins_1000', key: key() });
ok(r.ok && r.data.gems === 40 && r.data.coins === 1000, 'buy coins_1000 for 10 gems', r.data ?? r.error);
const k = key();
r = await call('wallet-spend', { offerId: 'deal_daily', key: k });
ok(r.ok && r.data.gems === 11 && r.data.boosts.fert === 3, 'buy daily deal (29 gems, 3 fert)', r.data ?? r.error);
r = await call('wallet-spend', { offerId: 'deal_daily', key: k });
ok(r.ok && r.data.fresh === false && r.data.gems === 11, 'same key repeats the answer, no charge');
r = await call('wallet-spend', { offerId: 'deal_daily', key: key() });
ok(!r.ok && r.error.code === 'OFFER_SOLD_OUT', 'daily deal sold out', r.error);
r = await call('wallet-spend', { offerId: 'boost_basket', key: key() });
ok(!r.ok && r.error.code === 'NOT_ENOUGH_GEMS', 'not enough gems', r.error);
r = await call('wallet-spend', { offerId: 'nope_x', key: key() });
ok(!r.ok && r.error.code === 'OFFER_UNKNOWN', 'unknown offer', r.error);

let inv = await doc(`players/${uid}/state/inventory`);
ok(Number(inv?.fields?.boosts?.mapValue?.fields?.fert?.integerValue) === 3, 'inventory holds 3 fert');
r = await call('boost-use', { boostId: 'fert', key: key() });
ok(r.ok && r.data.left === 2 && r.data.activeUntil > Date.now() + 29 * 60e3, 'use fert: 2 left, runs 30 min', r.data ?? r.error);
r = await call('boost-use', { boostId: 'shield', key: key() });
ok(!r.ok && r.error.code === 'BOOST_NONE', 'no shield to use', r.error);

r = await call('shop-claimGift', { key: key() });
ok(r.ok && r.data.coins === 50, 'free gift 50 coins', r.data ?? r.error);
r = await call('shop-claimGift', { key: key() });
ok(!r.ok && r.error.code === 'GIFT_NOT_READY', 'gift not ready again', r.error);

const island = (ordersToday, dailiesDone, stats = {}) => {
  const now = Date.now() / 1000;
  return JSON.stringify({ rulesVersion: 1, simAt: now, buildings: [], economy: { now, coins: 100, day: Math.floor(now / 86400),
    stats: Object.entries({ ...stats, orders: 10 + ordersToday }).map(([item, n]) => ({ item, n })), dayBase: [{ item: 'orders', n: 10 }],
    dailies: [0, 1, 2], dailyClaimed: dailiesDone ? [0, 1, 2] : [1] } });
};
r = await call('island-sync', { adopt: island(2, false, { sold: 150 }), mirror: true });
ok(r.ok, 'island mirrored through the engine', r.error);
r = await call('shop-claimChest', { chest: 'order', key: key() });
ok(!r.ok && r.error.code === 'CHEST_LOCKED' && r.error.params.have === '2', 'order chest locked at 2/6 orders', r.error);
r = await call('shop-claimChest', { chest: 'task', key: key() });
ok(!r.ok && r.error.code === 'CHEST_LOCKED', 'task chest locked (1/3 dailies)', r.error);
r = await call('goals-claimAchievement', { id: 'farmer1', key: key() });
ok(!r.ok && r.error.code === 'ACHIEVEMENT_LOCKED', 'farmer1 locked (0 harvested)', r.error);
r = await call('goals-claimAchievement', { id: 'trader1', key: key() });
ok(r.ok && r.data.granted === 10 && r.data.gems === 21, 'trader1 pays 10 gems (sold 150 >= 100)', r.data ?? r.error);
r = await call('goals-claimAchievement', { id: 'trader1', key: key() });
ok(!r.ok && r.error.code === 'ACHIEVEMENT_CLAIMED', 'trader1 only once', r.error);
r = await call('island-sync', { adopt: island(6, true, { sold: 150 }), mirror: true });
ok(r.ok, 'island mirrored again (6 orders, dailies done)', r.error);
r = await call('shop-claimChest', { chest: 'order', key: key() });
ok(r.ok && r.data.coins === 500 && r.data.gems === 5, 'order chest', r.data ?? r.error);
r = await call('shop-claimChest', { chest: 'order', key: key() });
ok(!r.ok && r.error.code === 'CHEST_CLAIMED', 'order chest once a day', r.error);
r = await call('shop-claimChest', { chest: 'task', key: key() });
ok(r.ok && r.data.gems === 3, 'task chest', r.data ?? r.error);
ok(int(await doc(`players/${uid}/state/wallet`), 'gems') === 29, 'wallet 11 + 10 + 5 + 3 = 29');

r = await call('wallet-earn', { source: 'inbox', amount: 120, key: key() });
ok(r.ok && r.data.granted === 100 && r.data.gems === 129, 'earn capped at 100/day (inbox)', r.data ?? r.error);

r = await call('wallet-spend', { offerId: 'coins_1000', key: 'bad key!' });
ok(!r.ok && r.error.code === 'INVALID_ARGUMENT', 'malformed key refused', r.error?.code);


// ---- module 4: daily login + wheel ----------------------------------------------------------
r = await call('daily-claimLogin', { key: key() });
ok(r.ok && r.data.day === 1 && r.data.coins === 500, 'daily login day 1 (500 coins)', r.data ?? r.error);
r = await call('daily-claimLogin', { key: key() });
ok(!r.ok && r.error.code === 'DAILY_CLAIMED', 'login once a day', r.error);
// five spins a day; a key spins once (retries are safe)
let left = 5, spun = true, firstKey = key(), first;
for (let i = 0; i < 5; i++) {
  r = await call('wheel-spin', { key: i === 0 ? firstKey : key() });
  if (i === 0) first = r.data;
  spun = spun && r.ok && r.data.spinsLeft === --left && r.data.segment >= 0 && r.data.segment <= 7;
}
ok(spun, 'five spins, server-rolled');
r = await call('wheel-spin', { key: firstKey });
ok(r.ok && !r.data.fresh && r.data.segment === first.segment, 'the same key never spins twice', r.data ?? r.error);
r = await call('wheel-spin', { key: key() });
ok(!r.ok && r.error.code === 'WHEEL_NO_SPINS', 'no sixth spin', r.error);

const w = await doc(`players/${uid}/state/wallet`);
ok(Number(w?.fields?.bought?.mapValue?.fields?.deal_daily?.integerValue) === 1, 'daily deal: bought today is on the wallet doc (the shop shows "Bought")', w?.fields);

// ---- module 5: farm pass ---------------------------------------------------------------------
r = await call('island-sync', { adopt: JSON.stringify({ ...JSON.parse(island(6, true, { sold: 150 })), economy: { ...JSON.parse(island(6, true, { sold: 150 })).economy, xp: 1000 } }), mirror: true });
ok(r.ok, 'island mirrored with xp 1000', r.error);
r = await call('pass-join', {});
ok(r.ok && r.data.baseXp === 1000, 'joined the season at xp 1000', r.data ?? r.error);
r = await call('pass-claim', { tier: 1, premium: false, key: key() });
ok(r.ok && r.data.coins === 300, 'pass tier 1 (free)', r.data ?? r.error);
r = await call('pass-claim', { tier: 2, premium: false, key: key() });
ok(!r.ok && r.error.code === 'PASS_LOCKED', 'tier 2 locked', r.error);
r = await call('pass-claim', { tier: 1, premium: true, key: key() });
ok(!r.ok && r.error.code === 'PASS_PREMIUM_REQUIRED', 'premium needs the pass', r.error);

// ---- module 6: coupons -------------------------------------------------------------------------
r = await call('coupons-claim', { taskId: 'g1' });
ok(!r.ok && r.error.code === 'COUPON_LOCKED', 'coupon task not done (0/3 goals)', r.error);
r = await call('coupons-claim', { taskId: 'd2' });   // fill 3 orders today: 6 done on the mirror
ok(r.ok && r.data.typeId === 'b3tr' && /^B3TR-[0-9A-Z]{4}-[0-9A-Z]{4}$/.test(r.data.code) && r.data.amount === 10 && r.data.unit === 'B3TR', 'coupon code made by the server, worth 10 B3TR', r.data ?? r.error);
const firstCode = r.data?.code;
r = await call('coupons-claim', { taskId: 'd2' });
ok(r.ok && r.data.code === firstCode && r.data.fresh === false, 'same task today: same code');

// ---- module 7: leaderboard (written from the engine summary) ---------------------------------
const week = 'w' + Math.floor((Date.now() - Date.UTC(2026, 0, 5)) / (7 * 86400e3));
const row = await doc(`leaderboards/${week}/players/${uid}`);
ok(row && int(row, 'level') >= 1 && row.fields.name, 'board row written by island-sync', row?.fields);

// ---- module 8: visits --------------------------------------------------------------------------
const other = await signUpOne();
const myToken = token;
token = other.idToken;
r = await call('player-suggestNames', { count: 1 });
r = await call('player-create', { displayName: r.data.names[0], avatarId: 'avatar_default' });
ok(r.ok, 'second player');
r = await call('visits-get', { uid });
ok(r.ok && r.data.crops >= 0 && Array.isArray(r.data.buildings) && r.data.likedToday === false, 'visit the first island', r.ok ? { name: r.data.name, level: r.data.level } : r.error);
r = await call('visits-like', { uid });
ok(r.ok && r.data.likes === 1 && r.data.fresh, 'like it');
r = await call('visits-like', { uid });
ok(r.ok && r.data.fresh === false, 'once a day');
r = await call('visits-get', { uid: other.localId });
ok(!r.ok && r.error.code === 'VISITS_CLOSED', 'no island yet: closed', r.error);
token = myToken;

// ---- module 9: inbox -----------------------------------------------------------------------------
const { execSync } = await import('node:child_process');
execSync(`npx --offline tsx tools/sendMail.ts --project ${P} --to ${uid} --title "Thanks" --body "A gift" --coins 200 --gems 4`, { cwd: process.env.FUNCTIONS_DIR, env: { ...process.env, FIRESTORE_EMULATOR_HOST: '127.0.0.1:8080' }, stdio: 'ignore' });
const list = await fetch(`http://127.0.0.1:8080/v1/projects/${P}/databases/(default)/documents/players/${uid}/notifications`, { headers: { authorization: 'Bearer owner' } }).then(x => x.json());
const mail = (list.documents ?? []).find(d => d.fields?.kind?.stringValue === 'mail');
const rewardId = mail?.fields?.routeArgs?.mapValue?.fields?.rewardId?.stringValue;
const mailId = mail?.name?.split('/').pop();
ok(!!rewardId, 'mail arrived with a reward');
r = await call('inbox-delete', { ids: [mailId] });
ok(r.ok && r.data.deleted === 0, 'kept while its reward waits', r.data ?? r.error);
r = await call('rewards-claim', { id: rewardId });
ok(r.ok && r.data.coins === 200 && r.data.gems === 4 && r.data.fresh, 'collect the mail reward', r.data ?? r.error);
r = await call('inbox-delete', { ids: [mailId] });
ok(r.ok && r.data.deleted === 1, 'then deleted', r.data ?? r.error);

console.log(fails === 0 ? 'ALL PASSED' : fails + ' FAILED');
process.exit(fails ? 1 : 0);
