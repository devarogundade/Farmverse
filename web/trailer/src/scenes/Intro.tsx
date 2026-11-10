import { sprite } from '@farmverse/shared/gameTheme';
import { Check } from 'lucide-react';
import { keys, pop, ramp, rise } from '../anim';
import { useT } from '../clock';
import { Browser, Chip, Clip, Cursor, Farm, Line, Title, pressed } from '../ui';

/** 0:00 — the island from the title screen, a slow push in, the logo lands. */
export function Open() {
  const t = useT();
  // Beat 1: the island alone while the voice sets the scene. Beat 2: the picture softens and the
  // logo lands over it.
  const zoom = keys(t, [[0, 1.0], [9, 1.16]], 'linear');
  const soft = ramp(t, 3.4, 1.0, 'inOut');
  return (
    <div className="sc sc-open">
      <img className="sc-open__bg" src={sprite('Home_Bg')} alt="" style={{ transform: `scale(${zoom})`, filter: `blur(${soft * 7}px)` }} />
      <div className="sc-open__shade" style={{ opacity: soft }} />
      <div className="sc-open__brand">
        <img className="sc-open__logo" src={sprite('Home_Logo')} alt="Farmverse" style={pop(t, 3.7, 0.7, 0.55)} />
        <p className="sc-open__sub" style={rise(t, 4.6, 0.7, 20)}>
          A cosy island farming game, in your browser
        </p>
      </div>
      <div className="tr-black" style={{ opacity: 1 - ramp(t, 0, 1.2, 'inOut') }} />
    </div>
  );
}

/** 0:08 — crops, then animals and a factory. */
export function Grow() {
  return (
    <div className="sc">
      <Clip id="clip1" from={0} until={7} />
      <Clip id="clip2" from={7} until={14.5} />
      <div className="sc-cap">
        <Title at={0.6} size={92} gold={['Build.']}>
          Grow. Raise. Build.
        </Title>
        <Line at={7.4}>Something to harvest every 30 seconds</Line>
      </div>
    </div>
  );
}

/** 0:22 — zombie nights, then the island growing. */
export function Island() {
  const t = useT();
  return (
    <div className="sc">
      <Clip id="clip3" from={0} until={7} />
      <Clip id="clip4" from={7} until={14.5} />
      <div className="sc-cap">
        <Title at={0.5} size={84} out={6.6}>
          Hold the island at night
        </Title>
        {t > 6.8 && (
          <Title at={7.2} size={84} gold={['100']} style={{ position: 'absolute', bottom: 0 }}>
            100 levels. 3 chapters.
          </Title>
        )}
      </div>
    </div>
  );
}

/** 0:36 — the game in a browser tab: Play as guest, and you're on your island. */
export function Tab() {
  const t = useT();
  const playAt = 2.8;
  const island = ramp(t, 3.3, 0.6, 'inOut');
  return (
    <div className="sc sc-tab">
      <div className="tr-ground">
        <div className="tr-ground__glow" />
      </div>
      <div className="sc-tab__head">
        <Title at={0.3} size={76}>
          It plays in a browser tab
        </Title>
      </div>
      <div className="sc-tab__win" style={rise(t, 0, 0.8, 60)}>
        <Browser url="play.farmverse.world">
          <div className="gm">
            {/* the title screen */}
            <div className="gm-home" style={{ opacity: 1 - island }}>
              <img className="gm-home__bg" src={sprite('Home_LoadingBg')} alt="" />
              <img className="gm-home__logo" src={sprite('Home_Logo')} alt="" />
              <div className="gm-home__btns">
                <span className="fv-btn green lg gm-home__btn" style={{ transform: `scale(${pressed(t, playAt)})` }}>
                  Play as guest
                </span>
                <span className="fv-btn yellow lg gm-home__btn">Sign in</span>
              </div>
            </div>
            {/* the island */}
            <div className="gm-isl" style={{ opacity: island }}>
              <img className="gm-isl__bg" src={sprite('Home_Bg')} alt="" style={{ transform: `scale(${1.25 - island * 0.12 - Math.max(0, t - 4) * 0.008})` }} />
              <div className="gm-hud" style={rise(t, 3.9, 0.5, -16)}>
                <span className="gm-hud__lvl fv-title">
                  <img src={sprite('Icon_Star')} alt="" />1
                </span>
                <span className="gm-hud__right">
                  <span className="gm-pill">
                    <img src={sprite('Icon_Golds')} alt="" />
                    <b>120</b>
                  </span>
                  <span className="gm-pill">
                    <img src={sprite('Icon_Gems')} alt="" />
                    <b>10</b>
                  </span>
                  <span className="gm-pill gm-pill--farm">
                    <Farm value={100} size={22} />
                  </span>
                </span>
              </div>
              <div className="gm-toast fv-panel" style={pop(t, 4.4, 0.45, 0.8)}>
                Welcome to your island!
              </div>
            </div>
          </div>
          <Cursor
            appear={0.9}
            path={[
              [0.9, 1080, 640],
              [2.6, 590, 542],
              [3.6, 590, 542],
              [5.0, 760, 420],
            ]}
            clicks={[playAt]}
            hide={5.4}
          />
        </Browser>
      </div>
      <div className="sc-tab__chips">
        <Chip at={6.4}>No wallet app</Chip>
        <Chip at={7.0}>No sign-up</Chip>
        <Chip at={7.6}>No gas</Chip>
      </div>
    </div>
  );
}

export const Tick = ({ on }: { on: boolean }) => <span className={`tr-tick ${on ? 'is-on' : ''}`}>{on && <Check size={20} strokeWidth={4} />}</span>;
