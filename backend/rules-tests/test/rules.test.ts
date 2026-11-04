import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterAll, beforeAll, beforeEach, describe, it } from 'vitest';
import {
  assertFails,
  assertSucceeds,
  initializeTestEnvironment,
  type RulesTestEnvironment,
} from '@firebase/rules-unit-testing';
import { collection, doc, getDoc, getDocs, limit, query, setDoc, updateDoc, where } from 'firebase/firestore';

/**
 * Security rules (08 §2): clients read their own documents with capped queries,
 * write only settings and notification `readAt`, and touch nothing else. Every
 * case here is one line of the rules.
 */
const ME = 'meAAAAAAAAAAAAAAAAAAAAAA';
const YOU = 'youAAAAAAAAAAAAAAAAAAAAA';
let env: RulesTestEnvironment;

beforeAll(async () => {
  const [host, port] = (process.env['FIRESTORE_EMULATOR_HOST'] ?? '127.0.0.1:8080').split(':');
  env = await initializeTestEnvironment({
    projectId: 'demo-farmverse',
    firestore: { rules: readFileSync(resolve(__dirname, '../../firestore.rules'), 'utf8'), host: host!, port: Number(port) },
  });
});

afterAll(async () => env?.cleanup());

beforeEach(async () => {
  await env.clearFirestore();
  await env.withSecurityRulesDisabled(async (ctx) => {
    const db = ctx.firestore();
    for (const uid of [ME, YOU]) {
      await setDoc(doc(db, `players/${uid}`), { displayName: 'farm', shortId: '10000' });
      await setDoc(doc(db, `players/${uid}/state/island`), { rev: 1, state: '{}' });
      await setDoc(doc(db, `players/${uid}/private/settings`), {
        schemaVersion: 1, allowVisitors: true, pushEnabled: true, quietStart: 22, quietEnd: 8,
        locale: 'en', timezone: 'UTC', reduceMotion: false, textScale: 100,
      });
      await setDoc(doc(db, `players/${uid}/notifications/n_aaaaaaaaaa`), { kind: 'system', readAt: 0, createdAt: 1 });
    }
    await setDoc(doc(db, 'usernames/me'), { uid: ME });
    await setDoc(doc(db, 'counters/playerNumbers'), { next: 10002 });
    await setDoc(doc(db, `players/${ME}/pushTokens/install_one`), { token: 'secret' });
    await setDoc(doc(db, `players/${ME}/friends/${YOU}`), { displayName: 'you' });
    await setDoc(doc(db, `players/${YOU}/friends/${ME}`), { displayName: 'me' });
    await setDoc(doc(db, 'invites/ABCDEFGH'), { inviterUid: YOU });
    await setDoc(doc(db, 'content/current'), { minAppVersion: '1.0.0' });
    await setDoc(doc(db, `players/${ME}/rewards/r_aaaaaaaaaa`), { coins: 600, gems: 5, claimedAt: 0 });
    await setDoc(doc(db, `referrals/${ME}`), { inviterUid: YOU, status: 'pending' });
    await setDoc(doc(db, 'news/n_aaaaaaaaaa'), { title: 'Hello', body: 'World', publishedAt: 1 });
    for (const uid of [ME, YOU]) {
      await setDoc(doc(db, `players/${uid}/state/wallet`), { gems: 50, updatedAt: 1 });
      await setDoc(doc(db, `players/${uid}/state/goals`), { achievements: {}, updatedAt: 1 });
      await setDoc(doc(db, `players/${uid}/state/daily`), { loginDay: 1, loginOn: '', wheelOn: '', wheelUsed: 0, updatedAt: 1 });
      await setDoc(doc(db, `players/${uid}/state/inventory`), { boosts: { fert: 1 }, activeUntil: {}, updatedAt: 1 });
      await setDoc(doc(db, `players/${uid}/state/shop`), { nextGiftAt: 0, starterEndsAt: 1, chestDay: {}, updatedAt: 1 });
      await setDoc(doc(db, `players/${uid}/state/walletDay`), { day: '2026-11-01', earned: {}, bought: {} });
      await setDoc(doc(db, `players/${uid}/ledger/s_key00001`), { kind: 'spend', delta: -10 });
    }
    await setDoc(doc(db, 'config/catalogue'), { version: 1 });
    await setDoc(doc(db, `leaderboards/w43/players/${YOU}`), { name: 'you', worth: 10 });
    await setDoc(doc(db, 'leaderboards/w43'), { paidAt: 1 });
    await setDoc(doc(db, `players/${ME}/coupons/d1_2026-11-01`), { code: 'B3TR-AAAA-0001', typeId: 'b3tr' });
    await setDoc(doc(db, 'couponCodes/B3TR-AAAA-0002'), { issued: false });
  });
});

const me = () => env.authenticatedContext(ME).firestore();
const anon = () => env.unauthenticatedContext().firestore();

describe('own world', () => {
  it('reads own player and island; never another player’s', async () => {
    await assertSucceeds(getDoc(doc(me(), `players/${ME}`)));
    await assertSucceeds(getDoc(doc(me(), `players/${ME}/state/island`)));
    await assertFails(getDoc(doc(me(), `players/${YOU}`)));
    await assertFails(getDoc(doc(me(), `players/${YOU}/state/island`)));
    await assertFails(getDoc(doc(anon(), `players/${ME}`)));
  });

  it('never reads server-only documents', async () => {
    await assertFails(getDoc(doc(me(), 'usernames/me')));
    await assertFails(getDoc(doc(me(), 'counters/playerNumbers')));
    await assertFails(getDoc(doc(me(), `players/${ME}/pushTokens/install_one`)));
    await assertFails(getDoc(doc(me(), 'invites/ABCDEFGH')));
    await assertFails(getDoc(doc(me(), 'content/current')));
  });

  it('writes no game state, not even its own island', async () => {
    await assertFails(updateDoc(doc(me(), `players/${ME}`), { displayName: 'someone' }));
    await assertFails(setDoc(doc(me(), `players/${ME}/state/island`), { rev: 2, state: '{"coins":1000000}' }));
    await assertFails(setDoc(doc(me(), 'usernames/taken'), { uid: ME }));
  });

  it('lists own friends in pages, never another player’s, and never writes them', async () => {
    await assertSucceeds(getDocs(query(collection(me(), `players/${ME}/friends`), limit(50))));
    await assertFails(getDocs(query(collection(me(), `players/${ME}/friends`), limit(51))));
    await assertFails(getDocs(query(collection(me(), `players/${YOU}/friends`), limit(50))));
    await assertFails(setDoc(doc(me(), `players/${ME}/friends/${YOU}`), { displayName: 'x' }));
  });
});

describe('rewards, referrals and news', () => {
  it('reads own rewards but never writes them, and never sees referrals', async () => {
    await assertSucceeds(getDoc(doc(me(), `players/${ME}/rewards/r_aaaaaaaaaa`)));
    await assertSucceeds(getDocs(query(collection(me(), `players/${ME}/rewards`), where('claimedAt', '==', 0), limit(50))));
    await assertFails(updateDoc(doc(me(), `players/${ME}/rewards/r_aaaaaaaaaa`), { claimedAt: 1 }));
    await assertFails(setDoc(doc(me(), `players/${ME}/rewards/r_bbbbbbbbbb`), { coins: 1e6, gems: 1e4, claimedAt: 0 }));
    await assertFails(getDoc(doc(me(), `players/${YOU}/rewards/r_aaaaaaaaaa`)));
    await assertFails(getDoc(doc(me(), `referrals/${ME}`)));
  });

  it('reads the news in pages of at most 20, signed in only, read only', async () => {
    await assertSucceeds(getDocs(query(collection(me(), 'news'), limit(20))));
    await assertFails(getDocs(query(collection(me(), 'news'), limit(21))));
    await assertFails(getDoc(doc(anon(), 'news/n_aaaaaaaaaa')));
    await assertFails(setDoc(doc(me(), 'news/n_bbbbbbbbbb'), { title: 'Free gems', body: 'x', publishedAt: 2 }));
  });
});

describe('the two direct writes', () => {
  it('updates own settings within types and ranges only', async () => {
    const settings = doc(me(), `players/${ME}/private/settings`);
    await assertSucceeds(updateDoc(settings, { allowVisitors: false, quietStart: 23, textScale: 120 }));
    await assertFails(updateDoc(settings, { quietStart: 24 }));
    await assertFails(updateDoc(settings, { allowVisitors: 'no' }));
    await assertSucceeds(updateDoc(settings, { music: false, analyticsConsent: true, termsAcceptedAt: Date.now() }));
    await assertFails(updateDoc(settings, { music: 'off' }));
    await assertFails(updateDoc(settings, { termsAcceptedAt: Date.now() + 3_600_000 }));
    await assertFails(updateDoc(settings, { coins: 5 }));
    await assertFails(updateDoc(doc(me(), `players/${YOU}/private/settings`), { allowVisitors: false }));
  });

  it('marks a notification read once, with a current timestamp, and changes nothing else', async () => {
    const note = doc(me(), `players/${ME}/notifications/n_aaaaaaaaaa`);
    await assertFails(updateDoc(note, { readAt: 1 }));
    await assertFails(updateDoc(note, { readAt: Date.now(), kind: 'friend_joined' }));
    await assertSucceeds(updateDoc(note, { readAt: Date.now() }));
    await assertFails(updateDoc(note, { readAt: Date.now() }));
    await assertFails(updateDoc(doc(me(), `players/${YOU}/notifications/n_aaaaaaaaaa`), { readAt: Date.now() }));
  });

  it('lists notifications in pages of at most 50', async () => {
    await assertSucceeds(getDocs(query(collection(me(), `players/${ME}/notifications`), where('readAt', '==', 0), limit(50))));
    await assertFails(getDocs(query(collection(me(), `players/${ME}/notifications`), where('readAt', '==', 0))));
  });
});

describe('wallet and catalogue', () => {
  it('reads own gems, never writes them, never sees the daily counters or the ledger', async () => {
    await assertSucceeds(getDoc(doc(me(), `players/${ME}/state/wallet`)));
    await assertFails(getDoc(doc(me(), `players/${YOU}/state/wallet`)));
    await assertFails(setDoc(doc(me(), `players/${ME}/state/wallet`), { gems: 999999, updatedAt: 2 }));
    await assertFails(getDoc(doc(me(), `players/${ME}/state/walletDay`)));
    await assertFails(getDoc(doc(me(), `players/${ME}/ledger/s_key00001`)));
    await assertFails(getDocs(query(collection(me(), `players/${ME}/ledger`), limit(10))));
  });

  it('reads own boosts and shop timers, never writes them', async () => {
    for (const d of ['inventory', 'shop', 'goals', 'daily', 'pass']) {
      await assertSucceeds(getDoc(doc(me(), `players/${ME}/state/${d}`)));
      await assertFails(getDoc(doc(me(), `players/${YOU}/state/${d}`)));
    }
    await assertFails(setDoc(doc(me(), `players/${ME}/state/inventory`), { boosts: { fert: 99 }, activeUntil: {}, updatedAt: 2 }));
    await assertFails(setDoc(doc(me(), `players/${ME}/state/shop`), { nextGiftAt: 0, starterEndsAt: 9e12, chestDay: {}, updatedAt: 2 }));
  });

  it('lists own coupons, never writes them, never sees the pools', async () => {
    await assertSucceeds(getDocs(query(collection(me(), `players/${ME}/coupons`), limit(50))));
    await assertFails(getDocs(query(collection(me(), `players/${YOU}/coupons`), limit(50))));
    await assertFails(setDoc(doc(me(), `players/${ME}/coupons/g1_once`), { code: 'FREE', typeId: 'b3tr' }));
    await assertFails(getDocs(query(collection(me(), 'couponCodes'), limit(5))));
    await assertFails(getDoc(doc(me(), 'couponCodes/B3TR-AAAA-0002')));
  });

  it('reads the leaderboard in pages when signed in, never writes it', async () => {
    await assertSucceeds(getDocs(query(collection(me(), 'leaderboards/w43/players'), limit(50))));
    await assertFails(getDocs(query(collection(me(), 'leaderboards/w43/players'), limit(51))));
    await assertSucceeds(getDoc(doc(me(), `leaderboards/w43/players/${YOU}`)));
    await assertFails(getDoc(doc(anon(), `leaderboards/w43/players/${YOU}`)));
    await assertFails(setDoc(doc(me(), `leaderboards/w43/players/${ME}`), { name: 'me', worth: 9e9 }));
    await assertFails(getDoc(doc(me(), 'leaderboards/w43')));
  });

  it('reads the catalogue signed in, never writes it', async () => {
    await assertSucceeds(getDoc(doc(me(), 'config/catalogue')));
    await assertFails(getDoc(doc(anon(), 'config/catalogue')));
    await assertFails(setDoc(doc(me(), 'config/catalogue'), { version: 99 }));
    await assertFails(getDoc(doc(me(), 'config/other')));
  });
});
