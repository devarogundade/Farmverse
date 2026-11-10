import { sprite } from '@farmverse/shared/gameTheme';
import type { NftMetadata } from '@farmverse/shared/nft/metadata';
import { clamp, ease, lerp, pop, ramp } from '../anim';
import { useT } from '../clock';
import { Model3D } from '../three/Model3D';
import { Chip, Clip, Cursor, Farm, Ground, Line, Title, pressed, rollValue } from '../ui';
import { WHEAT } from './Mint';

const HANDS: { name: string; skill: string; level: number; meta: NftMetadata }[] = [
  {
    name: 'Tilly Furrow', skill: 'Feeds the animals', level: 4,
    meta: { type: 'worker', outfit: { hair: 'Hairstyle_Male_001', hat: 'Hat_Single_013', face: 'Male_emotion_usual_001', top: 'Outwear_004', bottom: 'Shorts_003', socks: 'Socks_008', shoes: 'Shoe_Slippers_005' } },
  },
  {
    name: 'Juniper Beat', skill: 'Works the fields', level: 7,
    meta: { type: 'worker', outfit: { hair: 'Hairstyle_Male_Single_006', face: 'Male_emotion_happy_002', accessory: 'Headphones_002', top: 'Outwear_050', bottom: 'Pants_010', shoes: 'Shoe_Slippers_002' } },
  },
  {
    name: 'Otis Haywire', skill: 'Runs the machines', level: 5,
    meta: { type: 'worker', outfit: { hat: 'Hat_010', face: 'Male_emotion_angry_003', glasses: 'Glasses_004', accessory: 'Mustache_003', top: 'Outfit_010', bottom: 'Pants_009', glove: 'Gloves_014', shoes: 'Shoe_Sneakers_009' } },
  },
];

/** 1:44 — the Farmhands: Genesis workers you hire by the day. */
export function Hands() {
  const t = useT();
  const focus = ramp(t, 6.0, 0.9, 'inOut');
  const hireAt = 8.2;
  const hired = t > hireAt + 0.15;
  const earn = ramp(t, 10.4, 0.5);
  return (
    <div className="sc sc-hands">
      <Ground image="Home_Bg" dim={0.88} />
      <div className="sc-head">
        <Title at={0.3} size={80} gold={['500']}>
          500 Genesis Farmhands
        </Title>
        <Line at={1.0} out={5.8}>
          Each with an outfit, a job and a level
        </Line>
      </div>
      <div className="hands">
        {HANDS.map((h, i) => {
          const mid = i === 1;
          const x = lerp(i * 480, mid ? 0 : i * 480 + (i === 0 ? -160 : 160), focus);
          return (
            <div
              key={h.name}
              className="hand fv-frame lg yellow"
              style={{
                left: x,
                opacity: pop(t, 0.8 + i * 0.3, 0.55, 0.8).opacity * (mid ? 1 : 1 - focus),
                transform: pop(t, 0.8 + i * 0.3, 0.55, 0.8).transform,
              }}
            >
              <Model3D meta={h.meta} className="hand__stage" angle={mid ? 0.15 : i === 0 ? 0.35 : -0.35} clip={mid && hired ? 'Walk_Forward' : i === 2 ? 'Idle_Look_Around' : 'Idle_Relaxed'} />
              <span className="hand__lvl fv-title">Lv {h.level}</span>
              <b className="fv-title hand__name">{h.name}</b>
              <span className="hand__skill">{h.skill}</span>
            </div>
          );
        })}
        <div className="hire fv-panel" style={{ opacity: focus, transform: `translateX(${(1 - focus) * 100}px)` }}>
          <b className="fv-title hire__title">Hire Juniper Beat</b>
          <span className="hire__sub">Works the fields · Level 7</span>
          <div className="hire__price">
            <Farm value={12} size={44} />
            <span>a day</span>
          </div>
          <span className={`fv-btn ${hired ? 'blue' : 'green'} lg hire__btn`} style={{ transform: `scale(${pressed(t, hireAt)})` }}>
            {hired ? 'Hired · working on your island' : 'Hire for a day'}
          </span>
          <div className="earn" style={{ opacity: earn, transform: `translateY(${(1 - earn) * 24}px)` }}>
            <span className="earn__who">
              <img src={sprite('Icon_Golds')} alt="" />
              Their owner collects
            </span>
            <span className="earn__amt">
              <Farm value={Math.round(rollValue(t, 10.8, 0, 12, 1.2))} size={40} />
            </span>
          </div>
          <Cursor
            appear={6.8}
            path={[
              [6.8, 720, 620],
              [8.0, 190, 278],
              [8.7, 190, 278],
              [9.8, 560, 520],
            ]}
            clicks={[hireAt]}
            hide={9.6}
          />
        </div>
      </div>
    </div>
  );
}

/** 2:00 — Redeem: the NFT is burned and the goods land in Storage; then the same in the game. */
export function Redeem() {
  const t = useT();
  const goAt = 1.0;
  const fly = ease.inOut(clamp((t - 1.25) / 1.1));
  const landed = t > 2.35;
  const n = Math.round(rollValue(t, 2.35, 0, 250, 0.7));
  return (
    <div className="sc sc-redeem">
      <Ground image="Home_Bg" dim={0.9} />
      <div className="sc-head">
        <Title at={0.2} size={80} gold={['Redeem']}>
          Redeem it into any island
        </Title>
      </div>
      <div className="rd">
        <div className="rd-card" style={{ opacity: 1 - ramp(t, 2.0, 0.35), transform: `translate(${fly * 910}px, ${fly * -30}px) scale(${1 - fly * 0.7}) rotate(${fly * 8}deg)` }}>
          <div className="nftcard__frame fv-frame lg purple" style={{ boxShadow: `0 0 ${fly * 90}px rgba(255, 210, 58, ${fly * 0.8})` }}>
            <Model3D meta={WHEAT} className="nftcard__stage" angle={-0.2} spin={0.5} />
            <span className="nftcard__tag fv-title">NFT</span>
          </div>
        </div>
        <span className="fv-btn green lg rd-btn" style={{ transform: `scale(${pressed(t, goAt)})`, opacity: 1 - ramp(t, 1.4, 0.3) }}>
          Redeem
        </span>
        <div className="rd-store fv-panel" style={{ left: lerp(1120, 680, ramp(t, 2.9, 0.9, 'inOut')) }}>
          <b className="fv-title">Storage</b>
          <div className="rd-slot" style={landed ? pop(t, 2.35, 0.45, 1.25) : { opacity: 0.55 }}>
            <img src={sprite('Paint_crop_wheat')} alt="" />
            <b className="fv-title">{n}</b>
          </div>
          <div className="rd-slots">
            {['Isl_g_milk', 'Isl_g_egg', 'Isl_g_flour', 'Paint_good_bread', 'Isl_g_tomato'].map((s) => (
              <span key={s} className="rd-mini">
                <img src={sprite(s)} alt="" />
              </span>
            ))}
          </div>
        </div>
        <Cursor
          path={[
            [0, 640, 860],
            [0.85, 490, 822],
            [1.4, 490, 822],
            [2.4, 700, 960],
          ]}
          clicks={[goAt]}
          hide={2.2}
        />
        <div className="rd-chip">
          <Chip at={2.8} icon="Icon_Hourglass">
            Burned on the way in. Used once.
          </Chip>
        </div>
      </div>
      <Clip id="clip5">
        <div className="sc-cap">
          <Title at={4.8} size={80}>
            It lands on your island, ready to use
          </Title>
        </div>
      </Clip>
    </div>
  );
}
