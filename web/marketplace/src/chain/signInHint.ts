/**
 * The way a player chose to sign in on our own sign-in screen ("Continue with Google", or their email), handed to the
 * social sign page (sign.html) so it goes straight there instead of asking again. The sign page opens in a popup
 * from the game or the market and asks its opener; the game page (farmverse-page.js) answers the same message.
 */
export type SignInHint = { method: 'google' } | { method: 'email'; email: string };

export const HINT_SOURCE = 'farmverse-signin';

let hint: SignInHint | null = null;
let listening = false;

/** Answer the sign page (at `signOrigin`) with `next` until it's cleared with null. */
export function offerSignInHint(next: SignInHint | null, signOrigin: string): void {
  hint = next;
  if (listening || !next) return;
  listening = true;
  window.addEventListener('message', (e) => {
    if (e.origin !== signOrigin || e.data?.source !== HINT_SOURCE || e.data.type !== 'hint?' || !e.source) return;
    (e.source as Window).postMessage({ source: HINT_SOURCE, type: 'hint', hint }, e.origin);
  });
}

/** Read a hint the opener sent; anything malformed is no hint. */
export function parseHint(v: unknown): SignInHint | null {
  const h = v as Partial<{ method: string; email: string }> | null;
  if (h?.method === 'google') return { method: 'google' };
  if (h?.method === 'email' && typeof h.email === 'string' && h.email.includes('@')) return { method: 'email', email: h.email };
  return null;
}
