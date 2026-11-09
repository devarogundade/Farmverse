/**
 * The sign-in choices, social first: "Continue with Google", then an email, then whatever the screen offers instead
 * (a NEAR wallet in the market). Shared by the market's sign-in dialog and the social sign page (sign.html), so
 * it imports nothing heavier than React.
 */
import { useState, type ReactNode } from 'react';
import './signin.css';

/** Google's "G", in its own colours (Google's sign-in button guidelines). */
export function GoogleMark({ size = 22 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 48 48" aria-hidden="true">
      <path fill="#EA4335" d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z" />
      <path fill="#4285F4" d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z" />
      <path fill="#FBBC05" d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z" />
      <path fill="#34A853" d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z" />
    </svg>
  );
}

export function SignInChoices({
  onGoogle,
  onEmail,
  busy = false,
  email: initialEmail = '',
  children,
}: {
  onGoogle: () => void;
  onEmail: (email: string) => void;
  busy?: boolean;
  email?: string;
  /** The other ways, under an "or". */
  children?: ReactNode;
}) {
  const [email, setEmail] = useState(initialEmail);
  return (
    <div className="si">
      <button type="button" className="si-google" disabled={busy} onClick={onGoogle}>
        <GoogleMark />
        Continue with Google
      </button>
      <form
        className="si-email"
        onSubmit={(e) => {
          e.preventDefault();
          if (email.trim()) onEmail(email.trim());
        }}
      >
        <input
          className="si-input"
          type="email"
          required
          autoComplete="email"
          placeholder="Your email"
          aria-label="Your email"
          value={email}
          disabled={busy}
          onChange={(e) => setEmail(e.target.value)}
        />
        <button type="submit" className="fv-btn green si-wide" disabled={busy}>
          Continue with email
        </button>
      </form>
      {children && (
        <>
          <div className="si-or" role="separator">
            <span>or</span>
          </div>
          {children}
        </>
      )}
    </div>
  );
}

/** Sign-in errors in plain words (wallets and popups throw technical ones). */
export function signInError(e: unknown): string {
  const m = String((e as Error)?.message ?? e);
  if (/blocked/i.test(m)) return 'Your browser blocked the sign-in window. Allow pop-ups for this site and try again.';
  if (/closed|reject|cancel/i.test(m)) return 'Sign-in was closed before it finished. Try again.';
  return "Sign-in didn't finish. Try again.";
}
