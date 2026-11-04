import { initializeApp, getApp, getApps, type App } from 'firebase-admin/app';
import { getFirestore, type Firestore } from 'firebase-admin/firestore';
import { onCall, type CallableOptions } from 'firebase-functions/v2/https';
import { defineString } from 'firebase-functions/params';
import type { CallableSpec } from '@farmverse/contracts';
import { runCallable, type CallableDefinition, type CallableDeps } from '../core/callable';
import { systemClock } from '../core/clock';
import { cloudLogger } from '../core/logger';

/** The one region every function runs in (decision D1, 10 §5). */
export const REGION = 'europe-west1';

const MIN_CLIENT_VERSION = defineString('MIN_CLIENT_VERSION', { default: '0.0.0' });
const ENVIRONMENT = defineString('FARMVERSE_ENV', { default: 'dev' });

/**
 * True only inside the local Functions emulator (it sets FUNCTIONS_EMULATOR).
 * The emulator cannot verify App Check tokens against Google, and the editor's
 * desktop Firebase has no attestation provider, so App Check is enforced
 * everywhere except there. Deployed functions never see this variable.
 */
export const IN_EMULATOR = process.env['FUNCTIONS_EMULATOR'] === 'true';

let firestore: Firestore | undefined;

/**
 * The default Admin SDK app. firebase-functions creates its own named app before a
 * trigger runs, so "some app exists" is not "the default app exists": look for it by name.
 */
export function adminApp(): App {
  return getApps().some((a) => a.name === '[DEFAULT]') ? getApp() : initializeApp();
}

/** The Admin SDK's Firestore, initialised once per instance. */
export function db(): Firestore {
  if (!firestore) {
    firestore = getFirestore(adminApp());
    firestore.settings({ ignoreUndefinedProperties: true });
  }
  return firestore;
}

export function callableDeps(): CallableDeps {
  return {
    clock: systemClock,
    log: cloudLogger,
    minClientVersion: () => MIN_CLIENT_VERSION.value(),
    exposeIssues: ENVIRONMENT.value() !== 'prod',
  };
}

/** Per-function runtime options, in one table (03 §8). */
export interface RuntimeOptions {
  readonly minInstances?: number;
  readonly memory?: CallableOptions['memory'];
  readonly timeoutSeconds?: number;
  /** Consume the App Check token (replay protection) — for spends and trades. */
  readonly replayProtected?: boolean;
}

/** Turn a callable definition into a deployed function. */
export function defineCallable<S extends CallableSpec>(definition: CallableDefinition<S>, options: RuntimeOptions = {}) {
  return onCall(
    {
      region: REGION,
      enforceAppCheck: !IN_EMULATOR,
      consumeAppCheckToken: !IN_EMULATOR && options.replayProtected === true,
      memory: options.memory ?? '256MiB',
      timeoutSeconds: options.timeoutSeconds ?? 30,
      concurrency: 40,
      ...(options.minInstances === undefined ? {} : { minInstances: options.minInstances }),
    },
    (request) =>
      runCallable(
        definition,
        request.data,
        {
          uid: request.auth?.uid,
          isAdmin: request.auth?.token?.['admin'] === true,
          signInProvider: request.auth?.token?.firebase?.sign_in_provider,
          requestId: request.rawRequest.header('x-cloud-trace-context')?.split('/')[0],
        },
        callableDeps(),
      ),
  );
}
