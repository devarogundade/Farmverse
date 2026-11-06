import { sprite } from '@farmverse/shared/gameTheme';
import { useState } from 'react';
import { Input } from '../components/ui';
import { useSession } from '../session';

export function SignIn() {
  const { signInWithWallet, signInDev, devServer } = useSession();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [devAccount, setDevAccount] = useState('');

  const run = async (fn: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try {
      await fn();
    } catch (e) {
      setError((e as Error).message || 'Sign-in didn’t finish.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="signin">
      <div className="signin-card fv-panel">
        <img className="signin-logo" src={sprite('Home_Logo')} alt="Farmverse" />
        <h1 className="fv-title">Admin</h1>
        <p className="muted">Sign in with a team wallet. Only wallets on the admin list can get in.</p>
        <button className="fv-btn yellow lg" disabled={busy} onClick={() => run(signInWithWallet)}>
          <img src={sprite('Icon_Crown')} alt="" />
          {busy ? 'Waiting for your wallet…' : 'Sign in with wallet'}
        </button>
        {error && (
          <p className="form-err" role="alert">
            {error}
          </p>
        )}
        {devServer && (
          <form
            className="signin-dev"
            onSubmit={(e) => {
              e.preventDefault();
              void run(() => signInDev(devAccount));
            }}
          >
            <p className="muted">Local server: sign in without a wallet.</p>
            <div className="row">
              <Input value={devAccount} onChange={(e) => setDevAccount(e.target.value)} placeholder="name.testnet" aria-label="Account" />
              <button className="fv-btn gray sm" disabled={busy || devAccount.trim().length < 2}>
                Sign in
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}
