import { sprite } from '@farmverse/shared/gameTheme';
import type { NftMetadata } from '@farmverse/shared/nft/metadata';
import { Loader2 } from 'lucide-react';
import { clamp, lerp, pop, ramp, rise } from '../anim';
import { useT } from '../clock';
import { Model3D } from '../three/Model3D';
import { Chip, Cursor, Ground, Title, pressed } from '../ui';
import { Tick } from './Intro';

const WHEAT: NftMetadata = { type: 'good', item_id: 'wheat', quantity: 250 };
const BARN: NftMetadata = { type: 'building', item_id: 'barn' };
const TRACTOR: NftMetadata = { type: 'tractor', item_id: 'tractor_red' };

const TRIO = [
  { meta: WHEAT, name: 'Wheat ×250', frame: 'purple', at: 1.1 },
  { meta: BARN, name: 'Barn', frame: 'brown', at: 2.3 },
  { meta: TRACTOR, name: 'Red Tractor', frame: 'blue', at: 3.4 },
];

const SLOTS = [
  { icon: 'Paint_crop_wheat', n: 250, key: 'wheat' },
  { icon: 'Isl_g_milk', n: 40 },
  { icon: 'Isl_g_egg', n: 120 },
  { icon: 'Isl_g_flour', n: 60 },
  { icon: 'Paint_good_bread', n: 85 },
  { icon: 'Isl_g_tomato', n: 36 },
  { icon: 'Paint_seed_wheat', n: 18 },
  { icon: 'Paint_animal_cow', n: 2 },
];

const STEPS = ['Taking 250 wheat from Storage', 'Minting it on NEAR', 'Yours to keep or trade'];

/** 0:48 — anything you make can leave the game; then the mint itself. */
export function Mint() {
  const t = useT();
  const partB = ramp(t, 6.4, 0.6, 'inOut');
  const pickAt = 7.9;
  const mintAt = 9.0;
  const stepAt = [9.5, 10.5, 11.5];
  const step = (i: number) => (t < stepAt[i] ? 'wait' : t < stepAt[i] + 0.8 ? 'busy' : 'done');
  const picked = t > pickAt;
  const taken = t > stepAt[0] + 0.6;
  const wheatN = Math.round(250 * (1 - clamp((t - stepAt[0] - 0.2) / 0.5)));
  const sheet = ramp(t, mintAt + 0.15, 0.35);
  const card = ramp(t, 12.2, 0.7);
  return (
    <div className="sc sc-mint">
      <Ground image="Home_Bg" dim={0.9} />

      {/* A: three things that can leave the game */}
      <div className="sc-mint__a" style={{ opacity: 1 - partB, transform: `scale(${1 - partB * 0.04})` }}>
        <Title at={0.3} size={80} className="sc-head" gold={['leave']}>
          Anything you make can leave the game
        </Title>
        <div className="sc-mint__trio">
          {TRIO.map((it) => (
            <div key={it.name} className={`trio-card fv-frame lg ${it.frame}`} style={pop(t, it.at, 0.55, 0.7)}>
              <Model3D meta={it.meta} className="trio-card__stage" angle={-0.7} spin={0.35} />
              <b className="fv-title trio-card__name">{it.name}</b>
            </div>
          ))}
        </div>
      </div>

      {/* B: the mint, in the game's Storage */}
      <div className="sc-mint__b" style={{ opacity: partB }}>
        <Title at={6.8} size={72} className="sc-head">
          Tap Mint. It's yours, on NEAR.
        </Title>
        <section className="store fv-panel" style={{ ...rise(t, 6.5, 0.6, 40), left: lerp(580, 120, ramp(t, 11.7, 0.8, 'inOut')) }}>
          <header className="store__head">
            <b className="fv-title">Storage</b>
            <span className="store__tabs">
              <span className="is-on">Goods</span>
              <span>NFTs</span>
            </span>
          </header>
          <div className="store__grid">
            {SLOTS.map((s) => {
              const wheat = s.key === 'wheat';
              return (
                <div key={s.icon} className={`slot ${wheat && picked ? 'is-picked' : ''}`} style={wheat ? { opacity: taken ? 0.45 : 1 } : undefined}>
                  <img src={sprite(s.icon)} alt="" />
                  <b className="fv-title">{wheat ? wheatN : s.n}</b>
                </div>
              );
            })}
          </div>
          <footer className="store__foot">
            <span className="store__sel">{picked ? 'Wheat ×250' : 'Pick something to mint'}</span>
            <span className="fv-btn green store__mint" style={{ transform: `scale(${pressed(t, mintAt)})` }}>
              Mint as NFT
            </span>
          </footer>

          <div className="sheet fv-panel" style={{ opacity: sheet, transform: `translateY(${(1 - sheet) * 40}px)` }}>
            <b className="fv-title sheet__title">Minting Wheat ×250</b>
            {STEPS.map((s, i) => (
              <div key={s} className={`sheet__step is-${step(i)}`}>
                {step(i) === 'busy' ? <Loader2 className="spin" size={26} style={{ transform: `rotate(${t * 400}deg)` }} /> : <Tick on={step(i) === 'done'} />}
                <span>{s}</span>
              </div>
            ))}
          </div>
          <Cursor
            appear={7.0}
            path={[
              [7.0, 640, 620],
              [7.8, 110, 190],
              [8.2, 110, 190],
              [8.9, 600, 578],
              [9.6, 600, 578],
              [10.6, 700, 700],
            ]}
            clicks={[pickAt, mintAt]}
            hide={10.4}
          />
        </section>

        <div className="nftcard" style={{ opacity: card, transform: `translateX(${(1 - card) * 120}px) rotate(${(1 - card) * 4}deg)` }}>
          <div className="nftcard__frame fv-frame lg purple">
            <Model3D meta={WHEAT} className="nftcard__stage" angle={-0.4} spin={0.4} />
            <span className="nftcard__tag fv-title">NFT</span>
          </div>
          <b className="fv-title nftcard__name">Wheat ×250</b>
          <span className="nftcard__owner">Owned by you · on NEAR</span>
        </div>
        <div className="sc-mint__chips">
          <Chip at={13.2}>No popup</Chip>
          <Chip at={13.7}>No gas</Chip>
          <Chip at={14.2} icon="Icon_Clover">
            Fee paid by Farmverse
          </Chip>
        </div>
      </div>
    </div>
  );
}

export { WHEAT };
