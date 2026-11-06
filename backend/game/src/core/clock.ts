/**
 * The server's time in epoch milliseconds — the unit of every game timestamp.
 * Read once per transaction attempt and passed down; nothing below reads the
 * wall clock itself, so tests pin time exactly.
 */
export interface Clock {
  nowMs(): number;
}

export const systemClock: Clock = { nowMs: () => Date.now() };

/** A clock tests set and advance by hand. */
export class FixedClock implements Clock {
  constructor(private current = 1_800_000_000_000) {}

  nowMs(): number {
    return this.current;
  }

  set(ms: number): void {
    this.current = ms;
  }

  advance(ms: number): void {
    if (ms < 0) throw new Error('time does not go backwards');
    this.current += ms;
  }
}
