/**
 * The Privy wallet's sign page (plan 15 §13.2), opened in a popup by near-connect: log in with an emailed code,
 * make sure the player has a NEAR wallet (our server creates it through Privy), then sign what the game or the
 * market asked for.
 */
import '@radix-ui/themes/styles.css';
import '@peerfolio/privy-near-connect/sign-page-plugin/theme.css';
import Privy, { LocalStorage } from '@privy-io/js-sdk-core';
import { SignPagePlugin } from '@peerfolio/privy-near-connect/sign-page-plugin';
import { StrictMode, useEffect, useMemo, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { config } from '../config';

const env = import.meta.env as Record<string, string | undefined>;
const APP_ID = env.VITE_PRIVY_APP_ID ?? '';
const CLIENT_ID = env.VITE_PRIVY_CLIENT_ID ?? '';
/** Who may ask this page to sign: the market and the game. */
const ORIGINS = [location.origin, new URL(config.playUrl).origin];

type Step = 'loading' | 'email' | 'code' | 'wallet' | 'sign' | 'error';

const STYLE = `body{margin:0;font-family:system-ui,sans-serif;background:#0b2a55;color:#fff}
.sign-card{max-width:360px;margin:40px auto;padding:24px;background:#103a72;border-radius:16px}
.sign-card label{display:grid;gap:6px;margin:12px 0}
.sign-card input{padding:10px;border-radius:8px;border:0;font-size:16px}
.sign-card button{width:100%;padding:12px;border:0;border-radius:10px;background:#ffd23a;font-weight:700;font-size:16px;cursor:pointer}
.sign-note{opacity:.85}`;

function SignPage() {
  const privy = useMemo(() => new Privy({ appId: APP_ID, clientId: CLIENT_ID, storage: new LocalStorage() }), []);
  const [step, setStep] = useState<Step>('loading');
  const [email, setEmail] = useState('');
  const [code, setCode] = useState('');
  const [msg, setMsg] = useState('');

  const withWallet = async () => {
    setStep('wallet');
    const token = await privy.getAccessToken();
    const res = await fetch(`${config.gameApiUrl}/v1/auth/privy/near-wallet`, { method: 'POST', headers: { authorization: `Bearer ${token}` } });
    if (!res.ok) throw new Error('Could not set up your wallet.');
    setStep('sign');
  };

  useEffect(() => {
    privy
      .initialize()
      .then(async () => ((await privy.getAccessToken()) ? withWallet() : setStep('email')))
      .catch(() => {
        setMsg('Sign-in is not available right now.');
        setStep('error');
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [privy]);

  if (!APP_ID) return <p className="sign-note">Email sign-in isn't set up here.</p>;
  if (step === 'sign') return <SignPagePlugin privy={privy} options={{ allowedOrigins: ORIGINS, debug: false }} />;
  return (
    <main className="sign-card">
      <h1>Farmverse</h1>
      {step === 'loading' || step === 'wallet' ? <p>{step === 'wallet' ? 'Setting up your wallet…' : 'One moment…'}</p> : null}
      {step === 'email' ? (
        <form
          onSubmit={async (e) => {
            e.preventDefault();
            setMsg('');
            try {
              await privy.auth.email.sendCode(email.trim());
              setStep('code');
            } catch {
              setMsg("That email didn't work. Check it and try again.");
            }
          }}
        >
          <label>
            Your email
            <input type="email" required autoFocus value={email} onChange={(e) => setEmail(e.target.value)} />
          </label>
          <button type="submit">Send me a code</button>
        </form>
      ) : null}
      {step === 'code' ? (
        <form
          onSubmit={async (e) => {
            e.preventDefault();
            setMsg('');
            try {
              await privy.auth.email.loginWithCode(email.trim(), code.trim());
              await withWallet();
            } catch {
              setMsg("That code didn't work. Try again.");
            }
          }}
        >
          <label>
            The code we emailed to {email}
            <input inputMode="numeric" required autoFocus value={code} onChange={(e) => setCode(e.target.value)} />
          </label>
          <button type="submit">Sign in</button>
        </form>
      ) : null}
      {msg ? <p className="sign-note">{msg}</p> : null}
    </main>
  );
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <style>{STYLE}</style>
    <SignPage />
  </StrictMode>,
);
