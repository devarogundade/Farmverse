/**
 * The "Email or Google" wallet for the game page, built on its own as privy-near.js (vite.game.config.ts) and loaded
 * by farmverse-page.js. The Privy app comes from this build's env (deployment/vercel.sh sets it), so the game's
 * index.html needs no new settings.
 */
import { createPrivyNear, PRIVY_NEAR_ID } from './chain/privyNear';

export { PRIVY_NEAR_ID };

export function setup(o: { gameApiUrl: string; network: 'mainnet' | 'testnet'; rpcUrl?: string; icon: string }) {
  const appId: string | undefined = import.meta.env.VITE_PRIVY_APP_ID;
  if (!appId) return null;
  return createPrivyNear({
    appId,
    clientId: import.meta.env.VITE_PRIVY_CLIENT_ID ?? '',
    gameApiUrl: o.gameApiUrl,
    rpcUrls: o.rpcUrl ? { [o.network]: o.rpcUrl } : undefined,
    icon: o.icon,
  });
}
