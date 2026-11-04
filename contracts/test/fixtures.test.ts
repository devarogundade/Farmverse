import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { allContracts } from '../src';

interface Sample {
  readonly contract: string;
  readonly value: unknown;
}

const { samples } = JSON.parse(
  readFileSync(resolve(__dirname, '../fixtures/wire-samples.json'), 'utf8'),
) as { samples: Sample[] };

const byId = new Map(allContracts().map(({ id, schema }) => [id, schema]));

describe('wire samples', () => {
  it.each(samples.map((s, i) => [`${i}: ${s.contract}`, s] as const))('%s is valid', (_, sample) => {
    const schema = byId.get(sample.contract);
    expect(schema, `unknown contract ${sample.contract}`).toBeDefined();
    const parsed = schema!.safeParse(sample.value);
    expect(parsed.success, JSON.stringify(parsed.error?.issues)).toBe(true);
  });
});
