import { createHash } from 'node:crypto';
import { secp256k1 } from '@noble/curves/secp256k1.js';
import { keccak_256 } from '@noble/hashes/sha3.js';
import { describe, expect, it } from 'vitest';
import { memoryLogger } from '../../game/src/core/logger';
import { MemoryStore } from '../../game/src/infra/memoryStore';
import type { PrismaClient } from '../src/generated/prisma/client';
import { buildMessages, checkSignedText, cleanNote, farmText, nearAiVoice, needsNote, PERSONAS, recoverSigner, type MerchantVoice, type PriceMove, type Voice } from '../src/chain/merchantVoice';
import { MERCHANTS, merchantTick, type MerchantWallet } from '../src/chain/merchants';

const E18 = 10n ** 18n;
const move: PriceMove = {
  merchant: 'bea', display: 'Bea', role: 'Baker', item: 'wheat', stock: 120, target: 300, sales24h: 4,
  oldBuy: 50n * 10n ** 15n, newBuy: 71n * 10n ** 15n, sell: 88_750n * 10n ** 12n, why: 'Low on stock and it sells: paying more', wantsMore: true,
};

// a well-known test key and its Ethereum address
const KEY = Buffer.from('4c0883a69102937d6231471b5dbb6204fe5129617082792ae468d01a3f362318', 'hex');
const ADDRESS = '0x2c7536e3605d9c16a7a3d7b1898e529396a65c23';
/** personal_sign, as NEAR AI's ECDSA chat signatures are made: r || s || v. */
function personalSign(text: string): string {
  const msg = Buffer.from(text, 'utf8');
  const hash = keccak_256(Buffer.concat([Buffer.from(`\x19Ethereum Signed Message:\n${msg.length}`), msg]));
  const rec = secp256k1.sign(hash, KEY, { prehash: false, format: 'recovered' });
  return `0x${Buffer.concat([Buffer.from(rec.subarray(1)), Buffer.from([rec[0]! + 27])]).toString('hex')}`;
}
const sha = (s: string) => createHash('sha256').update(s).digest('hex');

describe('merchant voice prompt', () => {
  it('gives the persona and only the real numbers', () => {
    const [system, user] = buildMessages(move);
    expect(system!.content).toContain('You are Bea, the baker');
    expect(system!.content).toContain(PERSONAS['bea']);
    expect(system!.content).toContain('Use only the facts given');
    expect(user!.content).toBe(
      [
        'Goods: wheat',
        'In stock: 120 (I like to hold about 300)',
        'Sold on the market in the last 24 hours: 4',
        'I paid before: 0.05 FARM each',
        'I pay now: 0.071 FARM each',
        'I sell at: 0.0888 FARM each',
        'Reason: Low on stock and it sells: paying more',
      ].join('\n'),
    );
  });

  it('says when it is the first price and when the shelves are full', () => {
    const { oldBuy: _, ...first } = move;
    const user = buildMessages({ ...first, item: 'golden_tomato', wantsMore: false })[1]!.content;
    expect(user).toContain('Goods: golden tomato');
    expect(user).toContain('This is my first price for it.');
    expect(user).toContain('I am not buying more right now');
    expect(user).not.toContain('I pay now');
  });

  it('every merchant has a persona', () => {
    for (const m of MERCHANTS) expect(PERSONAS[m.name]).toBeTruthy();
  });

  it('formats FARM briefly', () => {
    expect(farmText(5n * 10n ** 16n)).toBe('0.05');
    expect(farmText(E18)).toBe('1');
    expect(farmText(123_456_789n * 10n ** 10n)).toBe('1.2346');
  });
});

describe('merchant voice fallback rules', () => {
  const note: Voice = { text: 'x', buy: '5', why: 'Steady', model: 'm', at: 1 };
  it('asks again only when the price or reason changed', () => {
    expect(needsNote(undefined, 5n, 'Steady')).toBe(true);
    expect(needsNote(note, 5n, 'Steady')).toBe(false);
    expect(needsNote(note, 6n, 'Steady')).toBe(true);
    expect(needsNote(note, 5n, 'Plenty in stock: paying less')).toBe(true);
  });

  it('cleans the answer to a short note', () => {
    expect(cleanNote('<think>hmm</think>  "Flour is short, so I pay more."  ')).toBe('Flour is short, so I pay more.');
    expect(cleanNote('One. Two! Three? Four.')).toBe('One. Two!');
    expect(cleanNote('   ')).toBeNull();
    expect(cleanNote(null)).toBeNull();
    expect(cleanNote('word '.repeat(100))!.length).toBeLessThanOrEqual(280);
  });
});

describe('NEAR AI signature checks', () => {
  it('recovers the personal_sign signer', () => {
    expect(recoverSigner('model:a:b', personalSign('model:a:b'))).toBe(ADDRESS);
    expect(recoverSigner('model:a:c', personalSign('model:a:b'))).not.toBe(ADDRESS);
  });

  it('matches the signed text to our hashes and model', () => {
    expect(checkSignedText('m:rq:rs', 'm', 'rq', 'rs')).toBe('provider_tee');
    expect(checkSignedText('rq:rs', 'm', 'rq', 'rs')).toBe('gateway');
    expect(checkSignedText('other:rq:rs', 'm', 'rq', 'rs')).toBeNull();
    expect(checkSignedText('m:rq:xx', 'm', 'rq', 'rs')).toBeNull();
  });
});

/** A fake NEAR AI Cloud: answers chat, signature and attestation calls. */
function fakeCloud(opts: { chat?: (body: string) => Response | Promise<Response>; signer?: string; attested?: string[] } = {}) {
  const calls: string[] = [];
  let lastRequest = '';
  let lastResponse = '';
  const fetchFn = (async (input: string | URL, init?: RequestInit) => {
    const url = String(input);
    calls.push(url.replace(/\?.*/, ''));
    if (url.endsWith('/v1/chat/completions')) {
      lastRequest = String(init?.body);
      if (opts.chat) return opts.chat(lastRequest);
      lastResponse = JSON.stringify({ id: 'chat-1', choices: [{ message: { content: 'Wheat is short and selling, so I pay a bit more.' } }] });
      return new Response(lastResponse);
    }
    if (url.includes('/v1/signature/chat-1')) {
      const text = `M:${sha(lastRequest)}:${sha(lastResponse)}`;
      return new Response(JSON.stringify({ text, signature: personalSign(text), signing_address: opts.signer ?? ADDRESS, signing_algo: 'ecdsa', signature_kind: 'provider_tee' }));
    }
    if (url.includes('/v1/attestation/report')) {
      const nonce = new URL(url).searchParams.get('nonce');
      return new Response(JSON.stringify({ model_attestations: (opts.attested ?? [ADDRESS]).map((a) => ({ signing_address: a, request_nonce: nonce, nvidia_payload: '{}' })) }));
    }
    return new Response('not found', { status: 404 });
  }) as typeof fetch;
  return { fetchFn, calls };
}
const cfg = { apiKey: 'test-key-123', model: 'M', baseUrl: 'https://cloud.test', timeoutMs: 1000 };

describe('nearAiVoice', () => {
  it('writes a note and verifies its signature against the attested key', async () => {
    const cloud = fakeCloud();
    const v = nearAiVoice(cfg, memoryLogger(), { fetch: cloud.fetchFn, now: () => 7, gpuCheck: async () => true, retryMs: 0 });
    const note = await v.note(move);
    expect(note).toMatchObject({ text: 'Wheat is short and selling, so I pay a bit more.', buy: move.newBuy.toString(), why: move.why, model: 'M', at: 7 });
    expect(note!.proof).toMatchObject({ chatId: 'chat-1', kind: 'provider_tee', checks: { hashes: true, signer: true, attested: true, gpu: true }, verified: true });
    // the attestation is reused for the next note
    await v.note(move);
    expect(cloud.calls.filter((c) => c.endsWith('/attestation/report'))).toHaveLength(1);
  });

  it('does not claim verification when the key is not attested or the GPU check fails', async () => {
    const other = nearAiVoice(cfg, memoryLogger(), { fetch: fakeCloud({ attested: ['0x0000000000000000000000000000000000000001'] }).fetchFn, gpuCheck: async () => true, retryMs: 0 });
    expect((await other.note(move))!.proof).toMatchObject({ checks: { attested: false }, verified: false });
    const noGpu = nearAiVoice(cfg, memoryLogger(), { fetch: fakeCloud().fetchFn, gpuCheck: async () => false, retryMs: 0 });
    expect((await noGpu.note(move))!.proof).toMatchObject({ checks: { attested: true, gpu: false }, verified: false });
    const wrongSigner = nearAiVoice(cfg, memoryLogger(), { fetch: fakeCloud({ signer: '0x0000000000000000000000000000000000000002', attested: ['0x0000000000000000000000000000000000000002'] }).fetchFn, gpuCheck: async () => true, retryMs: 0 });
    expect((await wrongSigner.note(move))!.proof).toMatchObject({ checks: { signer: false }, verified: false });
  });

  it('falls back (null) on an error, a timeout or an empty answer', async () => {
    const fail = (chat: () => Response | Promise<Response>) => nearAiVoice(cfg, memoryLogger(), { fetch: fakeCloud({ chat }).fetchFn, retryMs: 0 });
    expect(await fail(() => new Response('busy', { status: 503 })).note(move)).toBeNull();
    expect(await fail(() => Promise.reject(new DOMException('The operation timed out.', 'TimeoutError'))).note(move)).toBeNull();
    expect(await fail(() => new Response(JSON.stringify({ id: 'x', choices: [{ message: { content: '  ' } }] }))).note(move)).toBeNull();
    expect(await fail(() => new Response('not json')).note(move)).toBeNull();
  });
});

/** merchant-tick against a fake database and wallet: only Bea trades. */
function tickDeps(state: { sales: Record<string, number> }, voice?: MerchantVoice) {
  const prisma = {
    $queryRaw: async (strings: TemplateStringsArray) => {
      const sql = strings.join('?');
      if (sql.includes('chain.sales')) return Object.entries(state.sales).map(([item_id, n]) => ({ item_id, n: BigInt(n) }));
      return [];
    },
  } as unknown as PrismaClient;
  const wallet: MerchantWallet = {
    accountId: 'bea.test',
    farmBalance: async () => 1000n * E18,
    makeItemOffer: async () => {},
    list: async () => {},
    ensureMarketStorage: async () => {},
  };
  const store = new MemoryStore();
  return { store, deps: { prisma, store, itemsContract: 'items.test', wallets: new Map([['bea', wallet]]), nowMs: () => 1_000, ...(voice ? { voice } : {}) } };
}
const goods = async (store: MemoryStore) => ((await store.read('merchants/bea')).data as { goods: { item: string; why: string; note?: Voice }[] }).goods;

describe('merchant-tick with voices', () => {
  it('asks only for moves whose price or reason changed, and keeps the label as fallback', async () => {
    const asked: string[] = [];
    const voice: MerchantVoice = {
      note: async (m) => {
        asked.push(m.item);
        return { text: `${m.display} on ${m.item}`, buy: m.newBuy.toString(), why: m.why, model: 'M', at: 1 };
      },
    };
    const state = { sales: {} as Record<string, number> };
    const { store, deps } = tickDeps(state, voice);
    await merchantTick(deps, memoryLogger());
    expect(asked).toEqual(['wheat', 'maize', 'flour', 'croissant']);
    expect((await goods(store)).every((g) => g.note?.text.startsWith('Bea on ') && g.why)).toBe(true);

    asked.length = 0;
    await merchantTick(deps, memoryLogger());
    expect(asked).toEqual([]);

    state.sales['maize'] = 3; // its price moves: only maize is asked again
    await merchantTick(deps, memoryLogger());
    expect(asked).toEqual(['maize']);
  });

  it('shows the fixed label when there is no voice or the call fails, and stops asking for that tick', async () => {
    const { store, deps } = tickDeps({ sales: {} });
    await merchantTick(deps, memoryLogger());
    expect((await goods(store)).map((g) => [g.why, g.note])).toEqual([
      ['Low on stock: paying more', undefined],
      ['Low on stock: paying more', undefined],
      ['Low on stock: paying more', undefined],
      ['Low on stock: paying more', undefined],
    ]);

    let calls = 0;
    const failing = tickDeps({ sales: {} }, { note: async () => (calls++, null) });
    await merchantTick(failing.deps, memoryLogger());
    expect(calls).toBe(1);
    expect((await goods(failing.store)).every((g) => g.note === undefined && g.why)).toBe(true);
  });

  it('drops a note whose price moved when no new one comes', async () => {
    const state = { sales: {} as Record<string, number> };
    let ok = true;
    const { store, deps } = tickDeps(state, { note: async (m) => (ok ? { text: 'hi', buy: m.newBuy.toString(), why: m.why, model: 'M', at: 1 } : null) });
    await merchantTick(deps, memoryLogger());
    ok = false;
    state.sales['wheat'] = 5;
    await merchantTick(deps, memoryLogger());
    const g = await goods(store);
    expect(g.find((x) => x.item === 'wheat')!.note).toBeUndefined();
    expect(g.find((x) => x.item === 'maize')!.note?.text).toBe('hi');
  });
});
