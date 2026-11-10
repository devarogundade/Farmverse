import { sprite } from '@farmverse/shared/gameTheme';
import { Check } from 'lucide-react';
import { keys, pop, ramp, rise } from '../anim';
import { useT } from '../clock';
import { nearLogo } from '../media';
import { Chip, Ground, Title } from '../ui';

const GOALS = [
  { text: 'Merchants with wallets and NEAR AI voices', at: 0.6 },
  { text: 'Markets owned by players', at: 2.0 },
  { text: 'An economy that evolves on its own', at: 3.5 },
  { text: 'Players who never notice the chain', at: 5.6 },
];

const STACK = [
  { icon: 'Icon_Megaphone', text: 'Merchant voices by NEAR AI, verified in a TEE' },
  { icon: 'Icon_Clover', text: 'FARM token on NEAR' },
  { icon: 'check', text: 'Gasless mints and redeems' },
  { icon: 'Icon_PictoIcon_User', text: 'Email and Google sign-in' },
  { icon: 'Icon_Compass', text: 'Pay with any token via NEAR Intents' },
];

/** 2:12 — what the Games track asks for, ticked off; built on NEAR. */
export function Near() {
  const t = useT();
  const up = ramp(t, 8.2, 0.8, 'inOut');
  return (
    <div className="sc sc-near">
      <Ground image="Home_LoadingBg" dim={0.93} />
      <div className="near" style={{ transform: `translateY(${-up * 120}px) scale(${1 - up * 0.14})` }}>
        <span className="near__kicker" style={rise(t, 0.1, 0.5, 12)}>
          NEAR HACKATHON · Games With Real Economies
        </span>
        {GOALS.map((g) => (
          <div key={g.text} className="near__goal" style={rise(t, g.at, 0.55, 30)}>
            <span className="near__tick" style={pop(t, g.at + 0.25, 0.45, 0.4)}>
              <Check size={40} strokeWidth={4.5} />
            </span>
            <b className="fv-title">{g.text}</b>
          </div>
        ))}
      </div>
      <div className="near__stack">
        {STACK.map((s, i) => (
          <Chip key={s.text} at={8.8 + i * 0.25} icon={s.icon}>
            {s.text}
          </Chip>
        ))}
      </div>
      <div className="near__built" style={pop(t, 10.2, 0.6, 0.7)}>
        <span>Built on</span>
        {nearLogo ? <img src={nearLogo} alt="NEAR" /> : <b className="near__word">NEAR</b>}
      </div>
    </div>
  );
}

/** 2:26 — the Cleanmate DAO partnership. */
export function Community() {
  const t = useT();
  const n = Math.round(keys(t, [[1.4, 0], [3.4, 2700]], 'out'));
  return (
    <div className="sc sc-comm">
      <Ground image="Home_Bg" dim={0.9} />
      <div className="comm">
        <div className="comm__logos" style={pop(t, 0.2, 0.6, 0.7)}>
          <img src={sprite('Home_Logo')} alt="Farmverse" className="comm__fv" />
          <span className="comm__x">×</span>
          <span className="comm__cm">
            <img src="/media/brand/cleanmate.svg" alt="" />
            <b>Cleanmate DAO</b>
          </span>
        </div>
        <span className="comm__kicker" style={rise(t, 0.9, 0.5, 14)}>
          Partnership
        </span>
        <div className="comm__count" style={rise(t, 1.2, 0.5, 20)}>
          <b className="fv-title fv-gold">{n.toLocaleString('en-US')}+</b>
          <span className="fv-title">players in their community</span>
        </div>
      </div>
    </div>
  );
}

/** 2:33 — the end card. */
export function End() {
  const t = useT();
  const zoom = keys(t, [[0, 1.12], [12, 1.0]], 'out');
  return (
    <div className="sc sc-end">
      <img className="sc-open__bg" src={sprite('Home_LoadingBg')} alt="" style={{ transform: `scale(${zoom})`, filter: 'blur(6px)' }} />
      <div className="sc-end__shade" />
      <div className="end">
        <img className="end__logo" src={sprite('Home_Logo')} alt="Farmverse" style={pop(t, 0.4, 0.7, 0.55)} />
        <Title at={1.5} size={88} className="end__tag" gold={['Trade']}>
          Grow it. Own it. Trade it.
        </Title>
        <span className="fv-btn green lg end__cta" style={pop(t, 2.8, 0.5, 0.7)}>
          Play free at play.farmverse.world
        </span>
        <span className="end__foot" style={rise(t, 3.4, 0.6, 12)}>
          Built on NEAR · marketplace.farmverse.world
        </span>
      </div>
      <div className="tr-black" style={{ opacity: ramp(t, 10.6, 1.4, 'inOut') }} />
    </div>
  );
}
