/**
 * The Privy wallet's sign page (plan 15 §13.2), opened in a popup by near-connect: sign in with Google or an emailed
 * code, make sure the player has a NEAR wallet (our server creates it through Privy), then sign what the game or the
 * market asked for. When the player already chose Google or typed their email on the game's or market's sign-in
 * screen, the opener says so (signInHint.ts) and this page goes straight there.
 */
import '@radix-ui/themes/styles.css';
import '@peerfolio/privy-near-connect/sign-page-plugin/theme.css';
import '@farmverse/shared/theme.css';
import './sign.css';
import Privy, { LocalStorage } from '@privy-io/js-sdk-core';
import { SignPagePlugin } from '@peerfolio/privy-near-connect/sign-page-plugin';
import { StrictMode, useEffect, useMemo, useState, type ReactNode } from 'react';
import { createRoot } from 'react-dom/client';
import { HINT_SOURCE, parseHint, type SignInHint } from '../chain/signInHint';
import { SignInChoices } from '../components/SignIn';
import { config } from '../config';

const env = import.meta.env as Record<string, string | undefined>;
const APP_ID = env.VITE_PRIVY_APP_ID ?? '';
const CLIENT_ID = env.VITE_PRIVY_CLIENT_ID ?? '';
/** Who may ask this page to sign: the market and the game. */
const ORIGINS = [location.origin, new URL(config.playUrl).origin];

// The wallet's executor (in the game or market) closes this window unless it hears READY within 5 s, but signing in
// takes longer. Say READY now: the executor answers every READY with its request, so the sign step below, which says
// READY again once it's mounted, still gets it. (Also runs again when Google sends the player back here.)
if (window.opener) window.opener.postMessage({ source: 'privy-near-connect', type: 'READY' }, '*');

/** Ask the opener how the player chose to sign in; no answer in a moment is no choice. */
function askOpener(): Promise<SignInHint | null> {
  const opener = window.opener as Window | null;
  if (!opener) return Promise.resolve(null);
  return new Promise((resolve) => {
    const done = (h: SignInHint | null) => {
      window.removeEventListener('message', onMessage);
      clearTimeout(timer);
      resolve(h);
    };
    const onMessage = (e: MessageEvent) => {
      if (ORIGINS.includes(e.origin) && e.data?.source === HINT_SOURCE && e.data.type === 'hint') done(parseHint(e.data.hint));
    };
    window.addEventListener('message', onMessage);
    const timer = setTimeout(() => done(null), 800);
    // only our own pages hear the question (a wrong origin drops the message)
    for (const origin of ORIGINS) opener.postMessage({ source: HINT_SOURCE, type: 'hint?' }, origin);
  });
}

type Step = 'loading' | 'choose' | 'code' | 'busy' | 'sign' | 'error';

function SignPage() {
  const privy = useMemo(() => new Privy({ appId: APP_ID, clientId: CLIENT_ID, storage: new LocalStorage() }), []);
  const [step, setStep] = useState<Step>('loading');
  const [busyText, setBusyText] = useState('One moment…');
  const [email, setEmail] = useState('');
  const [code, setCode] = useState('');
  const [msg, setMsg] = useState('');

  const busy = (text: string) => {
    setBusyText(text);
    setStep('busy');
  };

  const withWallet = async () => {
    busy('Getting your account ready…');
    const token = await privy.getAccessToken();
    const res = await fetch(`${config.gameApiUrl}/v1/auth/privy/near-wallet`, { method: 'POST', headers: { authorization: `Bearer ${token}` } });
    if (!res.ok) throw new Error('Could not set up your account.');
    setStep('sign');
  };

  const google = async () => {
    setMsg('');
    busy('Opening Google…');
    try {
      const { url } = await privy.auth.oauth.generateURL('google', `${location.origin}${location.pathname}`);
      location.href = url;
    } catch {
      setMsg("Google sign-in isn't available right now. Use your email instead.");
      setStep('choose');
    }
  };

  const sendCode = async (to: string) => {
    setMsg('');
    setEmail(to);
    busy('Sending your code…');
    try {
      await privy.auth.email.sendCode(to);
      setCode('');
      setStep('code');
    } catch {
      setMsg("That email didn't work. Check it and try again.");
      setStep('choose');
    }
  };

  useEffect(() => {
    privy
      .initialize()
      .then(async () => {
        // back from Google: finish the login, then tidy the address
        const q = new URLSearchParams(location.search);
        const oauthCode = q.get('privy_oauth_code');
        const oauthState = q.get('privy_oauth_state');
        if (oauthCode && oauthState) {
          await privy.auth.oauth.loginWithCode(oauthCode, oauthState, 'google');
          history.replaceState(null, '', location.pathname);
        }
        if (await privy.getAccessToken()) return withWallet();
        // they already chose on the game's or market's sign-in screen: go straight there
        const hint = await askOpener();
        if (hint?.method === 'google') return google();
        if (hint?.method === 'email') return sendCode(hint.email);
        setStep('choose');
      })
      .catch(() => {
        setMsg('Sign-in is not available right now. Close this window and try again.');
        setStep('error');
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [privy]);

  if (!APP_ID) {
    return (
      <Frame title="Sign in">
        <p className="sp-note">Email and Google sign-in aren't set up here.</p>
      </Frame>
    );
  }
  if (step === 'sign') return <SignPagePlugin privy={privy} options={{ allowedOrigins: ORIGINS, debug: false }} />;
  if (step === 'loading' || step === 'busy') {
    return (
      <Frame>
        <div className="sp-wait" role="status" aria-live="polite">
          <img className="sp-spin" src="/ui/Loading_Rotate.webp" alt="" />
          <p>{step === 'busy' ? busyText : 'One moment…'}</p>
        </div>
      </Frame>
    );
  }
  if (step === 'error') {
    return (
      <Frame title="Sign in">
        <p className="sp-err" role="alert">
          {msg}
        </p>
      </Frame>
    );
  }
  if (step === 'code') {
    return (
      <Frame title="Check your email">
        <p className="sp-note">
          We sent a code to <b className="sp-email">{email}</b>.
        </p>
        <form
          className="sp-form"
          onSubmit={async (e) => {
            e.preventDefault();
            setMsg('');
            try {
              await privy.auth.email.loginWithCode(email, code.trim());
              await withWallet();
            } catch {
              setStep('code');
              setMsg("That code didn't work. Try again.");
            }
          }}
        >
          <input
            className="si-input sp-code"
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={6}
            required
            autoFocus
            placeholder="000000"
            aria-label="The code from the email"
            value={code}
            onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
          />
          <button type="submit" className="fv-btn green si-wide">
            Sign in
          </button>
        </form>
        {msg && (
          <p className="sp-err" role="alert">
            {msg}
          </p>
        )}
        <div className="sp-links">
          <button type="button" onClick={() => sendCode(email)}>
            Send a new code
          </button>
          <button
            type="button"
            onClick={() => {
              setMsg('');
              setStep('choose');
            }}
          >
            Use another email
          </button>
        </div>
      </Frame>
    );
  }
  return (
    <Frame title="Sign in">
      <p className="sp-note">No wallet app needed. Your account works in the game and the market.</p>
      <SignInChoices key={email} email={email} onGoogle={google} onEmail={sendCode} />
      {msg && (
        <p className="sp-err" role="alert">
          {msg}
        </p>
      )}
    </Frame>
  );
}

/** The page: the game's logo over one panel. */
function Frame({ title, children }: { title?: string; children: ReactNode }) {
  return (
    <main className="sp">
      <img className="sp-logo" src="/ui/Home_Logo.webp" alt="Farmverse" />
      <section className="sp-card fv-panel">
        {title && <h1 className="fv-title sp-title">{title}</h1>}
        {children}
      </section>
    </main>
  );
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <SignPage />
  </StrictMode>,
);
