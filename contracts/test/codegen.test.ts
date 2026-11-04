import { readFileSync, readdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { generate } from '../codegen/generate';
import { allContracts } from '../src';

const OUTPUT = resolve(__dirname, '../../Assets/Farmverse/Runtime/Contracts/Generated');

describe('C# code generation', () => {
  it('is deterministic', () => {
    expect([...generate()]).toEqual([...generate()]);
  });

  it('emits one file per contract plus the manifest and the catalogue defaults', () => {
    const files = generate();
    expect(files.size).toBe(allContracts().length + 2);
    expect(files.has('CatalogueDefaults.g.cs')).toBe(true);
    expect(files.has('Manifest.g.cs')).toBe(true);
  });

  it('matches the committed output (run `npm run contracts:gen` if this fails)', () => {
    const files = generate();
    const committed = readdirSync(OUTPUT).filter((name) => name.endsWith('.g.cs'));
    expect(committed.sort()).toEqual([...files.keys()].sort());
    for (const [name, content] of files) {
      expect(readFileSync(join(OUTPUT, name), 'utf8'), name).toBe(content);
    }
  });

  it('never emits reflection or third-party serializer attributes', () => {
    for (const [, content] of generate()) {
      expect(content).not.toMatch(/System\.Reflection|JsonProperty|Newtonsoft|\[Serializable\]/);
    }
  });
});
