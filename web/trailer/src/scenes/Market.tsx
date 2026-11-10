import { FarmIcon } from '@farmverse/shared/brand';
import { sprite } from '@farmverse/shared/gameTheme';
import { pop, ramp, rise } from '../anim';
import { useT } from '../clock';
import { Model3D } from '../three/Model3D';
import { Browser, Cursor, Farm, Ground, ItemCard, Title, nft, pressed, rollValue } from '../ui';
import { WHEAT } from './Mint';

const LISTINGS: { id: string; name: string; price: number; frame: 'blue' | 'yellow' | 'green' | 'purple' | 'brown'; unit?: string }[] = [
  { id: 'wheat', name: 'Wheat ×250', price: 120, frame: 'purple' },
  { id: 'barn', name: 'Barn', price: 850, frame: 'brown' },
  { id: 'tractor_red', name: 'Red Tractor', price: 1400, frame: 'blue' },
  { id: 'farmhand', name: 'Juniper Beat', price: 12, frame: 'yellow', unit: '/ day' },
  { id: 'greenhouse', name: 'Greenhouse', price: 640, frame: 'brown' },
  { id: 'cow', name: 'Cow', price: 180, frame: 'green' },
  { id: 'golden_wheat', name: 'Golden Wheat ×10', price: 95, frame: 'yellow' },
  { id: 'flour', name: 'Flour ×100', price: 60, frame: 'purple' },
  { id: 'farm_pass', name: 'Farm Pass', price: 250, frame: 'green' },
  { id: 'deed', name: 'Land Deed', price: 2000, frame: 'blue' },
];

const RAIL = ['Icon_Shop', 'Icon_MenuIcon02_Inventory', 'Icon_PictoIcon_Cloth', 'Icon_Map', 'Icon_Friends'];

/** 1:06 — the player market: listings in FARM, an offer on any wheat, a sale. */
export function Market() {
  const t = useT();
  const hover = ramp(t, 3.0, 0.4);
  const offer = ramp(t, 5.4, 0.55) * (1 - ramp(t, 10.2, 0.45, 'inOut'));
  const sendAt = 8.0;
  const sent = t > sendAt + 0.15;
  const sold = t > 11.0;
  const camera = 1 + Math.max(0, t - 1.5) * 0.0035;
  return (
    <div className="sc sc-market">
      <Ground image="Home_Bg" dim={0.9} />
      <div className="sc-market__head">
        <Title at={0.3} size={76} gold={['FARM']}>
          Trade it for FARM
        </Title>
      </div>
      <div className="sc-market__win" style={{ ...rise(t, 0, 0.8, 60), transform: `${rise(t, 0, 0.8, 60).transform} scale(${camera})` }}>
        <Browser url="marketplace.farmverse.world">
          <div className="mk">
            <aside className="mk-rail">
              {RAIL.map((r, i) => (
                <span key={r} className={`mk-rail__btn ${i === 0 ? 'is-on' : ''}`}>
                  <img src={sprite(r)} alt="" />
                </span>
              ))}
            </aside>
            <main className="mk-main">
              <div className="mk-head">
                <b className="fv-title">Newest</b>
                <span className="mk-wallet">
                  <Farm value={Math.round(rollValue(t, 11.3, 1240, 2048, 1.1))} size={26} />
                </span>
              </div>
              <div className="mk-grid">
                {LISTINGS.map((l, i) => {
                  const isWheat = i === 0;
                  const isBarn = l.id === 'barn';
                  const lift = isWheat ? hover * (1 - ramp(t, 5.2, 0.4)) : 0;
                  return (
                    <div key={l.id} className="mk-cell" style={{ ...pop(t, 0.5 + i * 0.07, 0.45, 0.85), zIndex: isWheat ? 2 : 1 }}>
                      <ItemCard frame={l.frame} image={nft(l.id)} name={l.name} className={`mk-card ${lift > 0.01 ? 'is-hover' : ''}`} style={{ transform: `translateY(${-lift * 10}px) scale(${1 + lift * 0.04})` }}>
                        <span className="mk-price">
                          <FarmIcon size={22} />
                          <b>{l.price.toLocaleString('en-US')}</b>
                          {l.unit && <em>{l.unit}</em>}
                        </span>
                        {isWheat && (
                          <div className="mk-card__3d" style={{ opacity: lift }}>
                            <Model3D meta={WHEAT} className="mk-card__stage" angle={-0.6} spin={0.6} />
                          </div>
                        )}
                        {isBarn && (
                          <span className="mk-sold fv-title" style={pop(t, 11.0, 0.45, 1.5)}>
                            Sold
                          </span>
                        )}
                      </ItemCard>
                    </div>
                  );
                })}
              </div>
            </main>
            <div className="mk-offer fv-panel" style={{ opacity: offer, transform: `translateX(${(1 - offer) * 80}px)` }}>
              <b className="fv-title mk-offer__title">Make an offer</b>
              <div className="mk-offer__what">
                <img src={sprite('Paint_crop_wheat')} alt="" />
                <span>
                  <b>Any Wheat</b>
                  <small>100 or more</small>
                </span>
              </div>
              <div className="mk-offer__amt">
                <span>You offer</span>
                <Farm value={90} size={34} />
              </div>
              <span className={`fv-btn ${sent ? 'blue' : 'green'} mk-offer__btn`} style={{ transform: `scale(${pressed(t, sendAt)})` }}>
                {sent ? 'Offer sent' : 'Send offer'}
              </span>
              <small className="mk-offer__note">The market holds your FARM until someone accepts.</small>
            </div>
            <div className="mk-toast fv-panel" style={{ ...pop(t, 11.0, 0.45, 0.8), opacity: sold ? pop(t, 11.0, 0.45, 0.8).opacity : 0 }}>
              <img src={nft('barn')} alt="" />
              <span>
                Barn sold for <Farm value={850} size={22} />
              </span>
            </div>
          </div>
          <Cursor
            appear={1.8}
            path={[
              [1.8, 900, 760],
              [3.0, 277, 262],
              [5.0, 300, 270],
              [7.8, 1368, 442],
              [8.6, 1368, 442],
              [9.8, 1100, 700],
            ]}
            clicks={[sendAt]}
            hide={10.0}
          />
        </Browser>
      </div>
    </div>
  );
}
