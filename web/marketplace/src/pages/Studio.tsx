import { useState } from 'react';
import {
  ITEM_MODELS, MODEL_TYPES, OUTFIT_PARTS, OUTFIT_SLOTS, SLOT_LABEL, TYPE_SINGULAR, modelLabel, partLabel,
  type NftMetadata, type NftType, type OutfitSlot,
} from '@farmverse/shared/nft/metadata';
import { NftStage } from '@farmverse/shared/three/NftStage';
import { sprite } from '@farmverse/shared/gameTheme';
import { Select } from '../components/Select';
import { FRAME, TYPE_ICON } from '../components/ui';

const START: NftMetadata = {
  type: 'worker',
  outfit: { hair: 'Hairstyle_Male_005', face: 'Male_emotion_happy_002', top: 'Outwear_043', bottom: 'Pants_010', glove: 'Gloves_006', shoes: 'Shoe_Sneakers_009' },
};

/** Radix Select reserves '' for "no value", so an empty slot needs its own value. */
const NONE = '__none';

const firstItem = (type: NftType) => Object.keys(ITEM_MODELS).find((k) => ITEM_MODELS[k].type === type);

/** Dressing room: mix a worker's look, or preview any item, exactly as the market shows it. */
export function Studio() {
  const [meta, setMeta] = useState<NftMetadata>(START);

  const setType = (type: NftType) => {
    if (type === 'worker') return setMeta(START);
    setMeta({ type, item_id: firstItem(type), ...(type === 'good' ? { quantity: 100 } : {}) });
  };
  const setSlot = (slot: OutfitSlot, part: string) => {
    const outfit = { ...meta.outfit };
    if (part) outfit[slot] = part;
    else delete outfit[slot];
    setMeta({ ...meta, outfit });
  };
  const shuffle = () => {
    const outfit: NftMetadata['outfit'] = {};
    for (const s of OUTFIT_SLOTS) {
      if (s === 'costume') continue;
      const opts = OUTFIT_PARTS[s];
      if (s === 'face' || Math.random() < 0.7) outfit[s] = opts[Math.floor(Math.random() * opts.length)];
    }
    if (outfit.hat) delete outfit.hair;
    setMeta({ type: 'worker', outfit });
  };

  return (
    <>
      <div className="explore-head">
        <div>
          <h1 className="fv-title">Dressing room</h1>
          <p className="muted">Try on outfits for your workers, or take a closer look at any item.</p>
        </div>
      </div>

      <div className="studio">
        <section className={`hero fv-frame lg ${FRAME[meta.type]}`} aria-label="Preview">
          <NftStage meta={meta} className="hero-stage" framing="hero" interactive />
          <header className="hero-head">
            <h1 className="fv-title">{modelLabel(meta)}</h1>
            {meta.quantity != null && <span className="fv-title">×{meta.quantity}</span>}
          </header>
        </section>

        <div className="studio-panel card fv-panel">
          <div className="field">
            <span className="field-label">Show me</span>
            <div className="chips small">
              {MODEL_TYPES.map((t) => (
                <button key={t} className={meta.type === t ? 'on' : ''} onClick={() => setType(t)}>
                  <img className="fv-icon" src={sprite(TYPE_ICON[t])} alt="" />
                  {TYPE_SINGULAR[t]}
                </button>
              ))}
            </div>
          </div>

          {meta.type === 'worker' ? (
            <div className="slots">
              <div className="slots-head">
                <span className="field-label">Outfit</span>
                <button className="fv-btn yellow sm" onClick={shuffle}>
                  <img src={sprite('Icon_Clover')} alt="" /> Surprise me
                </button>
              </div>
              {OUTFIT_SLOTS.map((s) => (
                <div key={s} className="slot">
                  <span>{SLOT_LABEL[s]}</span>
                  <Select
                    size="sm"
                    label={SLOT_LABEL[s]}
                    value={meta.outfit?.[s] ?? NONE}
                    onChange={(v) => setSlot(s, v === NONE ? '' : v)}
                    options={[{ value: NONE, label: 'None' }, ...OUTFIT_PARTS[s].map((p) => ({ value: p, label: partLabel(p) }))]}
                  />
                </div>
              ))}
            </div>
          ) : (
            <div className="slots">
              <div className="slot">
                <span>Item</span>
                <Select
                  size="sm"
                  label="Item"
                  value={meta.item_id ?? ''}
                  onChange={(v) => setMeta({ ...meta, item_id: v })}
                  options={Object.entries(ITEM_MODELS)
                    .filter(([, v]) => v.type === meta.type)
                    .map(([k, v]) => ({ value: k, label: v.label, icon: TYPE_ICON[v.type] }))}
                />
              </div>
              {meta.type === 'good' && (
                <label className="slot">
                  <span>Amount</span>
                  <input type="range" min={1} max={500} value={meta.quantity ?? 1} onChange={(e) => setMeta({ ...meta, quantity: +e.target.value })} />
                  <b className="qty-out">{meta.quantity ?? 1}</b>
                </label>
              )}
            </div>
          )}
        </div>
      </div>
    </>
  );
}
