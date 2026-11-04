import { CALLABLES, CONTRACT_VERSION, ERROR_CATALOGUE, contracts } from '../src';
import { HEADER } from './csharp';
import { literal, pascal } from './naming';

/**
 * The manifest: facts about the contract that are not types — the contract
 * version, the callable table and the parameters each error code carries.
 */
export function emitManifest(namespace: string): string {
  const nameOf = (schema: unknown): string => {
    const meta = contracts.get(schema as never);
    if (!meta) throw new Error('A manifest schema is not a registered contract.');
    return meta.id;
  };

  const lines: string[] = [];
  lines.push(HEADER);
  lines.push('using System;');
  lines.push('using System.Collections.Generic;');
  lines.push('');
  lines.push(`namespace ${namespace}`);
  lines.push('{');

  // ---- version --------------------------------------------------------------
  lines.push('    /// <summary>The contract version this client was generated from.</summary>');
  lines.push('    public static class ContractInfo');
  lines.push('    {');
  lines.push(`        public const int Version = ${CONTRACT_VERSION};`);
  lines.push('    }');
  lines.push('');

  // ---- callables ------------------------------------------------------------
  lines.push('    /// <summary>Every callable function, with its request and response types.</summary>');
  lines.push('    public static class Callables');
  lines.push('    {');
  for (const [key, spec] of Object.entries(CALLABLES)) {
    const request = nameOf(spec.request);
    const response = nameOf(spec.response);
    lines.push(
      `        public static readonly CallableSpec<${request}, ${response}> ${pascal(key)} = new CallableSpec<${request}, ${response}>(${literal(spec.name)}, ${response}.FromWire);`,
    );
  }
  lines.push('    }');
  lines.push('');

  // ---- errors ---------------------------------------------------------------
  lines.push('    /// <summary>The localization parameters each error code carries.</summary>');
  lines.push('    public static class ErrorCatalogue');
  lines.push('    {');
  lines.push('        private static readonly IReadOnlyDictionary<ErrorCode, string[]> Params =');
  lines.push('            new Dictionary<ErrorCode, string[]>');
  lines.push('            {');
  for (const [code, params] of Object.entries(ERROR_CATALOGUE)) {
    const list = params.length === 0 ? 'Array.Empty<string>()' : `new[] { ${params.map(literal).join(', ')} }`;
    lines.push(`                [ErrorCode.${pascal(code)}] = ${list},`);
  }
  lines.push('            };');
  lines.push('');
  lines.push('        public static IReadOnlyList<string> ParamsOf(ErrorCode code) =>');
  lines.push('            Params.TryGetValue(code, out var list) ? list : Array.Empty<string>();');
  lines.push('');
  lines.push('        /// <summary>The localization key for an error message.</summary>');
  lines.push('        public static string MessageKey(ErrorCode code) =>');
  lines.push('            code == ErrorCode.Unknown ? "error.UNKNOWN" : "error." + ErrorCodeWire.ToWire(code);');
  lines.push('    }');
  lines.push('}');
  return lines.join('\n') + '\n';
}
