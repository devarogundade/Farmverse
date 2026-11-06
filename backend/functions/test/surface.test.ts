import { describe, expect, it } from 'vitest';
import { CALLABLES } from '@farmverse/contracts';
import * as deployed from '../src/index';

/** `group.verb` exports deploy as `group-verb` (firebase-functions naming). */
function deployedNames(): string[] {
  return Object.entries(deployed).flatMap(([group, fns]) => Object.keys(fns as object).map((verb) => `${group}-${verb}`)).sort();
}

/** Chain calls (mint, redeem) are served only by backend/server; this Firebase backend is being retired (docs 13 §4). */
const firebaseCallables = () => Object.values(CALLABLES).filter((s) => !s.name.startsWith('chain-'));

describe('the deployed surface', () => {
  it('exports every callable in the contract, under its contract name', () => {
    const names = deployedNames();
    for (const spec of firebaseCallables()) expect(names).toContain(spec.name);
  });

  it('exports nothing outside the documented inventory (03 §1)', () => {
    const callables = firebaseCallables().map((s) => s.name);
    const jobsAndTriggers = ['push-onNotificationCreated', 'push-task', 'push-onNewsCreated', 'island-wake', 'leaderboard-payWeek'];
    expect(deployedNames()).toEqual([...callables, ...jobsAndTriggers].sort());
  });
});
