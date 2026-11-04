import { CALLABLES } from '@farmverse/contracts';
import { contentFor } from '../game/content';
import { env } from './env';
import { defineCallable } from './runtime';

/** `content-manifest`: the art and audio bundles for the caller's platform. */
export const manifest = defineCallable(
  {
    spec: CALLABLES.contentManifest,
    auth: 'required',
    handler: async ({ data, log }) => contentFor(await env(log), data.meta.platform),
  },
  { memory: '128MiB' },
);
