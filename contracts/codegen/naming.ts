/** Name conversions between the wire (camelCase / snake / SCREAMING) and C# (PascalCase). */

const CSHARP_KEYWORDS = new Set([
  'abstract', 'as', 'base', 'bool', 'break', 'byte', 'case', 'catch', 'char', 'checked', 'class', 'const',
  'continue', 'decimal', 'default', 'delegate', 'do', 'double', 'else', 'enum', 'event', 'explicit', 'extern',
  'false', 'finally', 'fixed', 'float', 'for', 'foreach', 'goto', 'if', 'implicit', 'in', 'int', 'interface',
  'internal', 'is', 'lock', 'long', 'namespace', 'new', 'null', 'object', 'operator', 'out', 'override',
  'params', 'private', 'protected', 'public', 'readonly', 'ref', 'return', 'sbyte', 'sealed', 'short',
  'sizeof', 'stackalloc', 'static', 'string', 'struct', 'switch', 'this', 'throw', 'true', 'try', 'typeof',
  'uint', 'ulong', 'unchecked', 'unsafe', 'ushort', 'using', 'virtual', 'void', 'volatile', 'while',
]);

/** Split any identifier style into lowercase words: `clientVersion`, `PRICE_CHANGED`, `farming.plant`. */
export function words(identifier: string): string[] {
  return identifier
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .split(/[^A-Za-z0-9]+/)
    .filter((part) => part.length > 0)
    .map((part) => part.toLowerCase());
}

/** `clientVersion` → `ClientVersion`, `PRICE_CHANGED` → `PriceChanged`, `farming.plant` → `FarmingPlant`. */
export function pascal(identifier: string): string {
  const result = words(identifier)
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join('');
  if (result.length === 0) {
    throw new Error(`Cannot derive a C# name from '${identifier}'.`);
  }
  return /^[0-9]/.test(result) ? `_${result}` : result;
}

/** A C# local/parameter name, escaped when it is a keyword. */
export function camel(identifier: string): string {
  const p = pascal(identifier);
  const result = p.charAt(0).toLowerCase() + p.slice(1);
  return CSHARP_KEYWORDS.has(result) ? `@${result}` : result;
}

/** A C# string literal. */
export function literal(value: string): string {
  return `"${value.replace(/\\/g, '\\\\').replace(/"/g, '\\"')}"`;
}

/** XML-doc-safe text. */
export function xml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
