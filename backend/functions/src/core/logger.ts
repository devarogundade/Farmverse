/**
 * Structured logging. Fields are machine-readable; never log personal data
 * (uid is fine, display names and emails are not — 03 §7).
 */
export type LogFields = Readonly<Record<string, unknown>>;

export interface Logger {
  debug(message: string, fields?: LogFields): void;
  info(message: string, fields?: LogFields): void;
  warn(message: string, fields?: LogFields): void;
  error(message: string, fields?: LogFields): void;
  /** A logger whose entries all carry these fields. */
  with(fields: LogFields): Logger;
}

type Sink = (severity: 'DEBUG' | 'INFO' | 'WARNING' | 'ERROR', message: string, fields: LogFields) => void;

export function createLogger(sink: Sink, base: LogFields = {}): Logger {
  const write = (severity: Parameters<Sink>[0]) => (message: string, fields: LogFields = {}) =>
    sink(severity, message, { ...base, ...fields });
  return {
    debug: write('DEBUG'),
    info: write('INFO'),
    warn: write('WARNING'),
    error: write('ERROR'),
    with: (fields) => createLogger(sink, { ...base, ...fields }),
  };
}

/** Writes Cloud Logging structured JSON lines (severity + message + fields). */
export const cloudLogger: Logger = createLogger((severity, message, fields) => {
  const line = JSON.stringify({ severity, message, ...serializable(fields) });
  if (severity === 'ERROR') console.error(line);
  else if (severity === 'WARNING') console.warn(line);
  else console.log(line);
});

/** A logger that keeps entries in memory, for tests. */
export function memoryLogger(): Logger & { entries: { severity: string; message: string; fields: LogFields }[] } {
  const entries: { severity: string; message: string; fields: LogFields }[] = [];
  const logger = createLogger((severity, message, fields) => entries.push({ severity, message, fields }));
  return Object.assign(logger, { entries });
}

function serializable(fields: LogFields): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(fields)) {
    out[key] = value instanceof Error ? { name: value.name, message: value.message, stack: value.stack } : value;
  }
  return out;
}
