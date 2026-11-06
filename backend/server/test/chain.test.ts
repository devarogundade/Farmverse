import { describe, expect, it } from 'vitest';
import { fallbackTitle, farmNumber, formatFarm, nftTypeOf, parseProps } from '../src/chain/items';
import { neardataSource, pacer } from '../src/chain/neardata';
import { eventsIn, succeeded } from '../src/chain/parse';
import { dailySeries, metadataOf, parseCursor } from '../src/market/read';
import { block, eventLog, farm, ITEMS, list, MARKET, mint, receipt, T0, WATCHED } from './chainFixtures';

describe('eventsIn', () => {
  it('reads our events in order, with the mint title and icon from the call args', () => {
    const b = block(100, [mint('g-1', 'ann.testnet', { kind: 'good', item_id: 'wheat', quantity: 250 }, 'Wheat ×250'), list('g-1', 'ann.testnet', farm(12.5))]);
    const events = eventsIn(b, WATCHED);
    expect(events.map((e) => `${e.standard}/${e.event}`)).toEqual(['nep171/nft_mint', 'farmverse_items/item_mint', 'farmverse_market/market_list']);
    const mintEvent = events[1]!;
    expect(mintEvent.data).toMatchObject({ token_id: 'g-1', title: 'Wheat ×250', media: 'items/wheat.png', source: 'voucher' });
    expect(mintEvent.id).toBe(`${mintEvent.receiptId}:1`);
    expect(mintEvent.blockHeight).toBe(100);
    expect(mintEvent.blockTime.getTime()).toBe(T0 + 100_000);
  });

  it('takes a studio mint title from the top-level args', () => {
    const b = block(1, [mint('s-7', 'bob.testnet', { kind: 'worker', item_id: 'worker', quantity: 1 }, 'Tilly Furrow', 'studio')]);
    expect(eventsIn(b, WATCHED)[1]!.data['title']).toBe('Tilly Furrow');
  });

  it('ignores failed receipts, other contracts, foreign standards and plain logs', () => {
    const forged = eventLog('farmverse_market', '1.0.0', 'market_sale', { token_id: 'g-1' });
    const b = block(1, [
      receipt({ executor: MARKET, logs: [eventLog('farmverse_market', '1.0.0', 'market_delist', { nft_contract_id: ITEMS, token_id: 'g-1' })], status: { Failure: {} } }),
      receipt({ executor: 'evil.testnet', logs: [forged] }),
      receipt({ executor: ITEMS, logs: [forged] }), // items may not speak for the market
      receipt({ executor: MARKET, logs: ['hello', 'EVENT_JSON:{not json', 'EVENT_JSON:{"standard":"farmverse_market"}'] }),
    ]);
    expect(eventsIn(b, WATCHED)).toEqual([]);
  });

  it('splits an event with several data entries into one row each', () => {
    const line = `EVENT_JSON:${JSON.stringify({ standard: 'nep171', version: '1.2.0', event: 'nft_burn', data: [{ owner_id: 'a', token_ids: ['x'] }, { owner_id: 'a', token_ids: ['y'] }] })}`;
    const events = eventsIn(block(1, [receipt({ executor: ITEMS, logs: [line] })]), WATCHED);
    expect(events.map((e) => e.id)).toEqual([`${events[0]!.receiptId}:0:0`, `${events[0]!.receiptId}:0:1`]);
  });

  it('knows success from failure', () => {
    expect(succeeded({ SuccessValue: '' })).toBe(true);
    expect(succeeded({ SuccessReceiptId: 'x' })).toBe(true);
    expect(succeeded({ Failure: {} })).toBe(false);
    expect(succeeded('Unknown')).toBe(false);
  });
});

describe('item helpers', () => {
  it('maps contract kinds to marketplace types', () => {
    expect(nftTypeOf('tool', 'tractor_red')).toBe('tractor');
    expect(nftTypeOf('good', 'cow')).toBe('animal');
    expect(nftTypeOf('good', 'wheat')).toBe('good');
    expect(nftTypeOf('worker', 'worker')).toBe('worker');
    expect(nftTypeOf('pack', 'gems_100')).toBeNull();
  });

  it('formats FARM', () => {
    expect(formatFarm(farm(12.5))).toBe('12.5');
    expect(formatFarm(farm(3))).toBe('3');
    expect(formatFarm(farm(0.07))).toBe('0.07');
    expect(farmNumber(farm(12.34))).toBe(12.34);
    expect(fallbackTitle('chicken_coop', 1)).toBe('Chicken coop');
    expect(fallbackTitle('wheat', 250)).toBe('Wheat ×250');
    expect(parseProps('{"level":3}')).toEqual({ level: 3 });
    expect(parseProps('[1]')).toEqual({});
  });

  it('builds marketplace metadata', () => {
    const base = { minted_from: 'ann.testnet', minted_at: String(T0) };
    expect(metadataOf({ ...base, nft_type: 'good', item_id: 'wheat', quantity: '250', props: { quality: 'gold', nested: { a: 1 } } })).toEqual({
      type: 'good',
      item_id: 'wheat',
      quantity: 250,
      props: { quality: 'gold' },
      minted_from: 'ann.testnet',
      minted_at: T0,
    });
    expect(metadataOf({ ...base, minted_from: null, nft_type: 'worker', item_id: 'worker', quantity: '1', props: { outfit: { hair: 'Hairstyle_Male_005' }, speed: 3 } })).toEqual({
      type: 'worker',
      outfit: { hair: 'Hairstyle_Male_005' },
      props: { speed: 3 },
      minted_at: T0,
    });
  });

  it('carries prices forward across days without sales', () => {
    expect(dailySeries([{ day: 3, avg: farm(10) }, { day: 1, avg: farm(14) }], 5)).toEqual([10, 10, 14, 14]);
    expect(dailySeries([], 5)).toEqual([]);
    expect(parseCursor('24')).toBe(24);
    expect(parseCursor('-1')).toBe(0);
    expect(parseCursor(undefined)).toBe(0);
  });
});

describe('neardata source', () => {
  it('paces requests and waits out a rate limit instead of failing', async () => {
    let clock = 0;
    const waits: number[] = [];
    const sleep = async (ms: number) => {
      waits.push(ms);
      clock += ms;
    };
    const calls: string[] = [];
    let limited = 1;
    const fetchStub = (async (url: string) => {
      calls.push(url);
      if (limited-- > 0) return new Response('slow down', { status: 429, headers: { 'retry-after': '2' } });
      return Response.json(url.includes('last_block') ? { block: { header: { height: 42 } } } : null);
    }) as unknown as typeof fetch;
    const src = neardataSource('https://example.test/', { fetch: fetchStub, sleep, now: () => clock, requestsPerMinute: 60, apiKey: 'k3y-k3y-k3y' });

    expect(await src.finalHeight()).toBe(42);
    expect(await src.block(7)).toBeNull();
    expect(calls).toEqual([
      'https://example.test/v0/last_block/final?apiKey=k3y-k3y-k3y',
      'https://example.test/v0/last_block/final?apiKey=k3y-k3y-k3y',
      'https://example.test/v0/block/7?apiKey=k3y-k3y-k3y',
    ]);
    expect(waits[0]).toBe(2_000); // Retry-After honoured
  });

  it('spaces parallel callers evenly', async () => {
    let clock = 0;
    const starts: number[] = [];
    const turn = pacer(120, () => clock, async (ms) => void (clock += ms));
    for (let i = 0; i < 3; i++) {
      await turn();
      starts.push(clock);
    }
    expect(starts).toEqual([0, 500, 1000]);
  });
});
