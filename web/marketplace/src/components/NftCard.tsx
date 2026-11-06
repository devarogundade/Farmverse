import { sprite } from '@farmverse/shared/gameTheme';
import { useState } from 'react';
import type { Token } from '../data/mock';
import { href } from '../router';
import { NftStage } from '@farmverse/shared/three/NftStage';
import { displayNo } from '@farmverse/shared/nft/metadata';
import { FRAME, Price, fmt } from './ui';

/** Tile with a live 3D model, name chip and arrow, as in the "related" row of the design. */
export function NftCard({ token, showPrice = true, badge }: { token: Token; showPrice?: boolean; badge?: string }) {
  const m = token.metadata;
  // Still until the pointer (or keyboard focus) is on the card.
  const [live, setLive] = useState(false);
  return (
    <a
      className={`nft-card fv-frame ${FRAME[m.type]}`}
      href={href.token(token.token_id)}
      aria-label={`${token.title}, #${displayNo(token.token_id)}`}
      onPointerEnter={() => setLive(true)}
      onPointerLeave={() => setLive(false)}
      onFocus={() => setLive(true)}
      onBlur={() => setLive(false)}
    >
      <NftStage meta={m} className="nft-card-stage" animate={live} />
      {m.quantity != null && <span className="qty-badge">×{fmt(m.quantity)}</span>}
      {badge && <span className="card-ribbon fv-title">{badge}</span>}
      <div className="nft-card-foot">
        <div className="nft-card-text">
          <strong className="fv-title">{token.title}</strong>
          {showPrice && <Price value={token.price} />}
        </div>
        <span className="arrow-btn fv-chip" aria-hidden="true">
          <img className="fv-icon" src={sprite('Icon_PictoIcon_Next01')} alt="" />
        </span>
      </div>
    </a>
  );
}
