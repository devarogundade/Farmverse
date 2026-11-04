/**
 * Grant or revoke the `admin` custom claim that the admin-* callables require.
 * Run by an operator with project credentials; there is no client path to it.
 *
 *   npx tsx tools/setAdmin.ts --project farmverse-8deb6 --uid <uid> [--revoke]
 *
 * The user must sign in again (or refresh their ID token) for the claim to apply.
 */
import { initializeApp } from 'firebase-admin/app';
import { getAuth } from 'firebase-admin/auth';

async function main(): Promise<void> {
  const arg = (name: string) => {
    const i = process.argv.indexOf(name);
    return i > 0 ? process.argv[i + 1] : undefined;
  };
  const projectId = arg('--project');
  const uid = arg('--uid');
  const revoke = process.argv.includes('--revoke');
  if (!projectId || !uid) throw new Error('usage: setAdmin --project <projectId> --uid <uid> [--revoke]');
  initializeApp({ projectId });
  const auth = getAuth();
  const user = await auth.getUser(uid);
  const claims = { ...(user.customClaims ?? {}) };
  if (revoke) delete claims['admin'];
  else claims['admin'] = true;
  await auth.setCustomUserClaims(uid, claims);
  console.log(`${revoke ? 'revoked' : 'granted'} admin for ${uid} (${user.email ?? 'no email'}) in ${projectId}`);
}

main().catch((error: unknown) => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
