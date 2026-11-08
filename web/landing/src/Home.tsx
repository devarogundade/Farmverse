import { FarmIcon } from '@farmverse/shared/brand';
import { sprite } from '@farmverse/shared/gameTheme';
import type { NftMetadata } from '@farmverse/shared/nft/metadata';
import { NftStage } from '@farmverse/shared/three/NftStage';
import { useState } from 'react';
import { MintDemo } from './demos/MintDemo';
import { MARKET_URL, PLAY_URL, marketType } from './links';

const HERO_WORKER: NftMetadata = {
  type: 'worker',
  outfit: { hair: 'Hairstyle_Male_005', face: 'Male_emotion_happy_002', top: 'Outwear_043', bottom: 'Pants_010', glove: 'Gloves_006', shoes: 'Shoe_Sneakers_009' },
};
const HERO_TRACTOR: NftMetadata = { type: 'tractor', item_id: 'tractor_red' };
const HERO_WHEAT: NftMetadata = { type: 'good', item_id: 'wheat', quantity: 250 };

const WORKERS: { name: string; skill: string; meta: NftMetadata }[] = [
  {
    name: 'Tilly Furrow', skill: 'Feeds the animals',
    meta: { type: 'worker', outfit: { hair: 'Hairstyle_Male_001', hat: 'Hat_Single_013', face: 'Male_emotion_usual_001', top: 'Outwear_004', bottom: 'Shorts_003', socks: 'Socks_008', shoes: 'Shoe_Slippers_005' } },
  },
  {
    name: 'Otis Haywire', skill: 'Runs the machines',
    meta: { type: 'worker', outfit: { hat: 'Hat_010', face: 'Male_emotion_angry_003', glasses: 'Glasses_004', accessory: 'Mustache_003', top: 'Outfit_010', bottom: 'Pants_009', glove: 'Gloves_014', shoes: 'Shoe_Sneakers_009' } },
  },
  {
    name: 'Juniper Beat', skill: 'Works the fields',
    meta: { type: 'worker', outfit: { hair: 'Hairstyle_Male_Single_006', face: 'Male_emotion_happy_002', accessory: 'Headphones_002', top: 'Outwear_050', bottom: 'Pants_010', shoes: 'Shoe_Slippers_002' } },
  },
];

const STEPS = [
  { icon: 'Paint_tool_watering_can', title: 'Grow', text: 'Plant crops, raise animals and build up your island, one season at a time.' },
  { icon: 'Paint_good_flour', title: 'Mint', text: 'Turn a harvest, a building or even a tractor into an item that’s truly yours.' },
  { icon: 'Icon_Shop', title: 'Trade', text: 'Sell it on the market for FARM, or pick up what your island is missing.' },
  { icon: 'Paint_fac_farmhouse_l1', title: 'Redeem', text: 'Bring any item you hold back into the game. It lands on your island, ready to use.' },
];

const FAQ = [
  ['What is Farmverse?', 'A cosy island farming game you play in your browser. Grow crops, raise animals, build, hire workers — and trade what you make with other players.'],
  ['Is it free to play?', 'Yes. You can play the whole game for free. FARM is only needed if you want to buy things on the market or in the shop.'],
  ['How do I sign in?', 'Tap Play as guest to start straight away: your farm is made for you, nothing to sign up for. Or sign in with email or Google, or any NEAR wallet, to keep your farm with your account.'],
  ['What is FARM?', 'Farmverse’s own coin. You earn it by selling what you make to other players, and spend it on the market and in the shop. There will only ever be one billion FARM: 35% for player rewards, 20% for trading pools, 20% for the Farmverse treasury, 15% for the team (released over two years) and 10% for partners and grants.'],
  ['How do I get my first FARM?', 'Sell something you minted on the market. While Farmverse is in testing, the market and the game also have a free tap: 100 test FARM a day, plus a little test NEAR the first time so you can mint.'],
  ['What does “Mint” mean?', 'Minting packs something from your island — a stack of wheat, a barn, a tractor — into an item you own outright. While it’s minted it leaves your island, and you can keep it, give it away or sell it. Minting costs a few coins from your island, and goods are minted in stacks of at least 10.'],
  ['And “Redeem”?', 'Redeeming brings a minted item back into the game, onto the island of whoever holds it. The item is used up when you do, so it can only be redeemed once. You can redeem from the game’s NFTs tab or from the market.'],
  ['What is the Farm Pass?', 'A season of premium rewards and longer offline work for your farmhands. Buy it for FARM right in the game with one approval, or in the market’s shop. If you already have premium this season, an extra pass is saved for the next one.'],
  ['What are Farmhands?', 'Named workers with their own look, job and skills — some fields, some animals, some machines or freight. They work on the island of whoever holds them, get better the more they work, and keep their level when traded. Each workers’ house level gives one Farmhand a bed, up to six.'],
  ['Can I rent out my Farmhands?', 'Yes. Put them up for hire on the market with a daily price. Until someone hires them they keep working for you; while hired they work on the hirer’s farm, and what they earn waits for you to collect.'],
  ['How do offers work?', 'Offer FARM for any item, or for any stack of a good (“any wheat, 100 or more”). The market holds your FARM until the owner accepts or the offer ends, and you can cancel any time to get it back.'],
  ['What are land deeds?', 'Genesis land deeds clear a wild plot on your island for good. Only 1,000 will ever exist, and each one is used up when redeemed.'],
  ['What are golden crops?', 'Now and then a harvest comes up golden. Golden crops are worth far more than plain ones, and you can mint them one at a time to sell or keep.'],
  ['Are there fees on the market?', 'A 5% fee is taken on each sale, offer or hire, and the original maker of studio items gets a small share of resales. You always see the full amount before you confirm.'],
  ['What do I need to play?', 'A modern browser. A desktop or laptop gives the best experience.'],
];

export function Home() {
  const [live, setLive] = useState<number | null>(null);
  return (
    <>
      <section className="hero" style={{ backgroundImage: `url(${sprite('Home_Bg')})` }}>
        <div className="hero-inner">
          <div className="hero-copy">
            <img className="hero-logo" src={sprite('Home_Logo')} alt="Farmverse" />
            <h1 className="fv-title hero-tag">Grow it. Own it. Trade it.</h1>
            <p className="hero-lead">
              A cosy island farming game in your browser. Everything you grow or build can become yours to keep — trade it with
              other farmers, or bring it home to your island any time.
            </p>
            <div className="hero-ctas">
              <a className="fv-btn green lg" href={PLAY_URL}>
                Play free
              </a>
              <a className="fv-btn yellow lg" href={MARKET_URL}>
                Visit the market
              </a>
            </div>
          </div>
          <div className="hero-art" aria-hidden="true">
            <div className="hero-card main fv-frame lg yellow">
              <NftStage meta={HERO_WORKER} className="stage" framing="hero" />
            </div>
            <div className="hero-card small a fv-frame purple">
              <NftStage meta={HERO_WHEAT} className="stage" />
              <span className="qty">×250</span>
            </div>
            <div className="hero-card small b fv-frame blue">
              <NftStage meta={HERO_TRACTOR} className="stage" />
            </div>
          </div>
        </div>
      </section>

      <section className="section" id="how">
        <h2 className="fv-title section-title">How it works</h2>
        <ol className="steps">
          {STEPS.map((s, i) => (
            <li key={s.title} className="step fv-panel">
              <span className="step-no fv-title">{i + 1}</span>
              <img src={sprite(s.icon)} alt="" />
              <h3 className="fv-title">{s.title}</h3>
              <p>{s.text}</p>
            </li>
          ))}
        </ol>
      </section>

      <section className="section split" id="items">
        <div className="mint-demo" aria-hidden="true">
          <MintDemo />
        </div>
        <div className="split-copy">
          <h2 className="fv-title section-title left">Everything on your island can travel</h2>
          <p>
            Wheat and milk, barns and greenhouses, tractors and cows. Mint what you have too much of, and buy what you need to
            finish that next build. Every item looks the same on the market as it does in the game.
          </p>
          <a className="fv-btn blue" href={MARKET_URL}>
            Browse items
          </a>
        </div>
      </section>

      <section className="section" id="workers">
        <div className="workers fv-panel">
          <div className="workers-copy">
            <h2 className="fv-title section-title left">Hire a helping hand</h2>
            <p>
              Workers tend your fields, feed your animals and keep the machines running while you plan the next build. Every
              worker has their own outfit and skills — and they come along when you trade them.
            </p>
            <a className="fv-btn yellow" href={marketType('worker')}>
              Meet the workers
            </a>
          </div>
          <div className="worker-row">
            {WORKERS.map((w, i) => (
              <div
                key={w.name}
                className="worker fv-frame yellow"
                onPointerEnter={() => setLive(i)}
                onPointerLeave={() => setLive(null)}
              >
                <NftStage meta={w.meta} className="stage" animate={live === i} />
                <div className="worker-label">
                  <b className="fv-title">{w.name}</b>
                  <span>{w.skill}</span>
                </div>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section className="section split" id="farm">
        <div className="split-copy">
          <h2 className="fv-title section-title left">FARM, the coin of the islands</h2>
          <p>
            FARM is what farmers use to buy and sell on the market and to pick up gem packs. There is a fixed amount of FARM —
            no more will ever be made — so the only way to get it is from another player.
          </p>
          <a className="more-link" href="#/farm">
            Read the FARM disclosure →
          </a>
        </div>
        <div className="coin" aria-hidden="true">
          <FarmIcon size={200} />
        </div>
      </section>

      <section className="section" id="faq">
        <h2 className="fv-title section-title">Questions</h2>
        <div className="faq">
          {FAQ.map(([q, a]) => (
            <details key={q} className="fv-panel">
              <summary className="fv-title">{q}</summary>
              <p>{a}</p>
            </details>
          ))}
        </div>
      </section>

      <section className="cta" style={{ backgroundImage: `url(${sprite('Home_LoadingBg')})` }}>
        <img className="cta-npc" src={sprite('Home_Npc')} alt="" />
        <div className="cta-copy">
          <h2 className="fv-title">Your island is waiting</h2>
          <a className="fv-btn green lg" href={PLAY_URL}>
            Play free
          </a>
        </div>
      </section>
    </>
  );
}
