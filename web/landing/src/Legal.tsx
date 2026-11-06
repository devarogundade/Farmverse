import type { ReactNode } from 'react';

/**
 * Plain-language summaries. These are drafts: the plan (Docs/architecture/13 §10) requires a legal
 * review of the Terms, Privacy Policy and FARM disclosure before the mainnet launch.
 */
const PAGES: Record<string, { title: string; body: ReactNode }> = {
  terms: {
    title: 'Terms of Service',
    body: (
      <>
        <p>By playing Farmverse or using the market you agree to play fair and to these terms.</p>
        <h3 className="fv-title">Your account</h3>
        <p>You sign in with your own NEAR wallet. You are responsible for keeping it safe; we can never recover a wallet for you.</p>
        <h3 className="fv-title">Items you own</h3>
        <p>
          Items you mint or buy are held in your wallet. You can keep, give away, sell or redeem them. A redeemed item is used up.
          Items keep working in the game as long as Farmverse runs.
        </p>
        <h3 className="fv-title">The market</h3>
        <p>
          The market charges a small fee on each sale, shown before you confirm. Sales happen directly between players and can’t
          be reversed.
        </p>
        <h3 className="fv-title">Fair play</h3>
        <p>No cheating, bots, exploiting bugs or harassing other players. We may limit accounts that do.</p>
      </>
    ),
  },
  privacy: {
    title: 'Privacy Policy',
    body: (
      <>
        <p>We collect as little as we can to run the game.</p>
        <h3 className="fv-title">What we keep</h3>
        <p>Your wallet address, your island’s progress, and basic logs needed to keep the game running and fair.</p>
        <h3 className="fv-title">What we don’t</h3>
        <p>No real names, emails, phone numbers or payment cards. No advertising trackers.</p>
        <h3 className="fv-title">Public by nature</h3>
        <p>Trades, mints and redemptions happen on the NEAR blockchain, which anyone can read. That record can’t be deleted.</p>
      </>
    ),
  },
  farm: {
    title: 'FARM disclosure',
    body: (
      <>
        <p>FARM is the coin used inside Farmverse to trade items between players.</p>
        <h3 className="fv-title">Fixed supply</h3>
        <p>All FARM was created once. No more can ever be made.</p>
        <h3 className="fv-title">For playing, not investing</h3>
        <p>
          FARM is for using in the game and on the market. It is not an investment, carries no promise of value, and its price can
          go down as well as up. Only use what you are comfortable losing.
        </p>
        <h3 className="fv-title">Check your local rules</h3>
        <p>Digital coins are regulated differently around the world. It’s up to you to make sure you may use FARM where you live.</p>
      </>
    ),
  },
};

export function Legal({ page }: { page: string }) {
  const p = PAGES[page];
  if (!p) return null;
  return (
    <article className="legal fv-panel">
      <h1 className="fv-title">{p.title}</h1>
      <p className="draft">Draft — this page will be reviewed before launch.</p>
      {p.body}
      <p>
        <a className="more-link" href="#/">
          ← Back home
        </a>
      </p>
    </article>
  );
}

export const LEGAL_PAGES = Object.keys(PAGES);
