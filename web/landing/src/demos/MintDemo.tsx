import { FarmIcon } from '@farmverse/shared/brand';
import { sprite } from '@farmverse/shared/gameTheme';
import { Bar, Count, Fly, Spinner, Stage, States, Window, type Act, type Rect } from '@farmverse/shared/stageshow';
import type { ReactNode } from 'react';
import './mintDemo.css';

// "Everything on your island can travel", told as one action. Storage (the
// game's inventory) sits over the market's left rail. The cursor picks the
// wheat and presses Mint: the wheat leaves Storage and lands as a new card at
// the front of the market, the cards behind it making room. A beat later the
// card is sold and the FARM balance rolls up. It plays once when it comes into
// view and holds the sold card, which is also the poster.
//
// Copied from the Stageshow gold `subscriptions` (a thing flies from one
// surface into a list on another, then a total rolls), rebuilt in the game's
// own look: its sprites, frames, buttons and fonts.

type Mint = 'idle' | 'busy' | 'done';
type S = {
  picked: boolean; // the wheat slot is selected
  mint: Mint;
  gone: boolean; // the wheat has left Storage
  open: boolean; // the new card's room is open in the market
  landed: boolean;
  sold: boolean;
  paid: boolean;
  fly: null | { from: Rect; to: Rect; go: boolean };
};

const START: S = { picked: false, mint: 'idle', gone: false, open: false, landed: false, sold: false, paid: false, fly: null };
const POSTER: S = { ...START, mint: 'done', gone: true, open: true, landed: true, sold: true, paid: true };

const BALANCE = 1240;
const PRICE = 120;
const money = (n: number) => n.toLocaleString('en-US');

// Storage: the wheat is the only slot with words that matter; the rest are the
// game's own icons with their counts, as the game draws them.
const STORAGE = [
  { icon: 'Paint_crop_wheat', n: '250', key: 'wheat' },
  { icon: 'Isl_g_milk', n: '40' },
  { icon: 'Isl_g_egg', n: '120' },
  { icon: 'Isl_g_flour', n: '60' },
  { icon: 'Paint_good_bread', n: '85' },
  { icon: 'Paint_animal_cow', n: '1' },
];

// Listings already on the market: their art is the market's signature, their
// names and prices are filler.
const LISTED = [
  { icon: 'Isl_b_barn', frame: 'brown', name: 76, price: 44 },
  { icon: 'Isl_b_windmill', frame: 'brown', name: 68, price: 40 },
  { icon: 'Paint_animal_cow', frame: 'green', name: 72, price: 46 },
];
const LOWER = [
  { icon: 'Isl_g_milk', frame: 'blue' },
  { icon: 'Isl_b_greenhouse', frame: 'brown' },
  { icon: 'Isl_g_egg', frame: 'green' },
  { icon: 'Isl_b_barn', frame: 'brown' },
];

async function script(a: Act<S>) {
  await a.wait(800);
  a.mark('pick');
  // Aim at the slot's empty top-left, clear of its count
  await a.click({ at: 'slot-wheat', x: 0.3, y: 0.3 });
  await a.set({ picked: true });
  await a.wait(500);

  a.mark('mint');
  await a.click({ at: 'mint', x: 0.78, y: 0.62 });
  await a.set({ mint: 'busy' });
  await a.wait(650);

  // Take-off: the wheat lifts out of its slot in the same frame the slot lets
  // it go, and the market opens room at the front while it travels. The new
  // card's icon sits where it will land even while its room is shut.
  const from = a.rect('wheat-icon', { layout: true });
  const to = a.rect('new-icon', { layout: true });
  await a.set({ fly: { from, to, go: false } });
  await a.wait(60);
  await a.set((s) => ({ mint: 'done', picked: false, gone: true, open: true, fly: { ...s.fly!, go: true } }));
  // The cursor clears off the changed label while the wheat travels
  void a.move(a.layout === 'compact' ? { x: 150, y: 254 } : { x: 186, y: 338 });
  await a.wait(950);
  a.mark('landed');
  await a.set({ landed: true, fly: null });

  // Someone buys it: the ribbon drops on the card, then the balance follows
  await a.wait(1300);
  a.mark('sold');
  await a.set({ sold: true });
  await a.wait(350);
  await a.set({ paid: true });
  await a.wait(2400);
}

function Card({ icon, frame, children, iconTarget }: { icon: string; frame: string; children: ReactNode; iconTarget?: string }) {
  return (
    <div className={`md-card fv-frame ${frame}`}>
      <img className="md-card__icon" src={sprite(icon)} alt="" data-target={iconTarget} />
      <div className="md-card__foot">{children}</div>
    </div>
  );
}

function Storage({ s }: { s: S }) {
  return (
    <section className="md-storage fv-panel">
      <div className="md-storage__head">
        <span className="fv-title md-storage__title">Storage</span>
        <span className="md-mint fv-btn green" data-target="mint">
          <States
            value={s.mint}
            states={{
              idle: 'Mint',
              busy: <Spinner size={14} />,
              done: 'Minted',
            }}
          />
        </span>
      </div>
      <div className="md-slots">
        {STORAGE.map((it) => {
          const wheat = it.key === 'wheat';
          return (
            <div
              key={it.icon}
              className="md-slot"
              data-target={wheat ? 'slot-wheat' : undefined}
              data-picked={(wheat && s.picked) || undefined}
              data-gone={(wheat && s.gone) || undefined}
            >
              <img className="md-slot__icon" src={sprite(it.icon)} alt="" data-target={wheat ? 'wheat-icon' : undefined} />
              <b className="fv-title md-slot__n">{it.n}</b>
            </div>
          );
        })}
      </div>
    </section>
  );
}

function Market({ s }: { s: S }) {
  return (
    <Window url="marketplace.farmverse.world" className="md-window">
      <div className="md-body">
        <aside className="md-rail">
            <span className="md-rail__btn" data-on>
              <img src={sprite('Icon_Shop')} alt="" />
            </span>
            <span className="md-rail__btn">
              <img src={sprite('Icon_MenuIcon02_Inventory')} alt="" />
            </span>
            <span className="md-rail__btn">
              <img src={sprite('Icon_PictoIcon_Cloth')} alt="" />
            </span>
        </aside>
        <main className="md-main">
          <div className="md-head">
            <span className="fv-title md-head__title">Newest</span>
            <span className="md-wallet">
              <FarmIcon size={18} />
              <Count className="md-wallet__n" value={s.paid ? BALANCE + PRICE : BALANCE} duration={900} format={money} />
            </span>
          </div>
          <div className="md-row">
            {/* The new listing: its room opens and pushes the others right */}
            <div className="md-room" data-open={s.open || undefined}>
              <div className="md-new" data-landed={s.landed || undefined}>
                <Card icon="Paint_crop_wheat" frame="purple" iconTarget="new-icon">
                  <span className="fv-title md-card__name">Wheat</span>
                  <span className="md-price">
                    <FarmIcon size={12} />
                    {PRICE}
                  </span>
                </Card>
                <span className="md-qty fv-title">×250</span>
                <span className="md-sold fv-title" data-on={s.sold || undefined}>
                  Sold
                </span>
              </div>
            </div>
            {LISTED.map((l) => (
              <Card key={l.icon + l.name} icon={l.icon} frame={l.frame}>
                <Bar w={l.name} h={6} tone={4} />
                <Bar w={l.price} h={6} tone={3} />
              </Card>
            ))}
          </div>
          <div className="md-row md-row--lower">
            {LOWER.map((l, i) => (
              <Card key={i} icon={l.icon} frame={l.frame}>
                <Bar w={74} h={6} tone={4} />
              </Card>
            ))}
          </div>
        </main>
      </div>
    </Window>
  );
}

export function MintDemo() {
  return (
    <Stage<S>
      width={528}
      height={384}
      compact={{ below: 500, width: 358, height: 270 }}
      initial={START}
      poster={POSTER}
      script={script}
      play="once"
      theme="dark"
      cursor={{ at: { wide: { x: 186, y: 338 }, compact: { x: 150, y: 254 } }, style: 'arrow', ripple: true }}
      className="md"
    >
      {(s) => (
        <>
          <Market s={s} />
          <Storage s={s} />
          {s.fly && (
            <Fly from={s.fly.from} to={s.fly.to} go={s.fly.go} duration={900}>
              <img className="md-flyer" src={sprite('Paint_crop_wheat')} alt="" />
            </Fly>
          )}
        </>
      )}
    </Stage>
  );
}
