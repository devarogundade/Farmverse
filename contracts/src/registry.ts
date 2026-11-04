import { z } from 'zod';

/**
 * Metadata attached to every schema that crosses the client/server boundary.
 *
 * `id` becomes the C# type name, so it must be PascalCase and unique.
 */
export interface ContractMeta {
  readonly id: string;
  readonly description?: string;
}

/**
 * The registry of every named contract.
 *
 * Code generation walks this registry, so a schema that is not registered here
 * does not exist on the C# side. Every object and enum that appears in a
 * request, a response or a document must be registered; the generator refuses
 * anonymous nested objects so that every C# type has a deliberate name.
 */
export const contracts = z.registry<ContractMeta>();

const PASCAL_CASE = /^[A-Z][A-Za-z0-9]*$/;

/** Register a schema under a contract name and return it unchanged. */
export function named<T extends z.ZodType>(id: string, schema: T, description?: string): T {
  if (!PASCAL_CASE.test(id)) {
    throw new Error(`Contract id '${id}' must be PascalCase.`);
  }
  if (contracts.has(schema)) {
    throw new Error(`Schema is already registered as '${contracts.get(schema)?.id}'.`);
  }
  for (const [, meta] of registeredEntries()) {
    if (meta.id === id) {
      throw new Error(`Contract id '${id}' is registered twice.`);
    }
  }
  const meta: ContractMeta = description === undefined ? { id } : { id, description };
  contracts.add(schema, meta);
  ensureTracked(schema, id);
  return schema;
}

// z.registry does not expose iteration, so the registry keeps its own index.
const tracked = new Map<string, z.ZodType>();

function ensureTracked(schema: z.ZodType, id: string): void {
  tracked.set(id, schema);
}

function* registeredEntries(): Iterable<[z.ZodType, ContractMeta]> {
  for (const [id, schema] of tracked) {
    yield [schema, contracts.get(schema) ?? { id }];
  }
}

/** Every registered contract, in registration order. */
export function allContracts(): ReadonlyArray<{ readonly id: string; readonly schema: z.ZodType }> {
  return Array.from(tracked, ([id, schema]) => ({ id, schema }));
}
