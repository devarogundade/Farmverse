import { listTokens, type Sort } from '../api/market';
import { sprite } from '@farmverse/shared/gameTheme';
import { NftCard } from '../components/NftCard';
import { TYPE_ICON } from '../components/ui';
import { ME } from '../data/mock';
import { NFT_TYPES, TYPE_LABEL, type NftType } from '@farmverse/shared/nft/metadata';
import { href } from '../router';
import { useState } from 'react';

export function Explore({ type, mine, search }: { type?: string; mine?: boolean; search: string }) {
  const [sort, setSort] = useState<Sort>('newest');
  const active = NFT_TYPES.includes(type as NftType) ? (type as NftType) : undefined;
  const rows = listTokens({ type: active, q: search, sort, owner: mine ? ME : undefined });

  return (
    <>
      <div className="explore-head">
        <div>
          <h1 className="fv-title">{mine ? 'My items' : 'Explore'}</h1>
          <p className="muted">
            {mine
              ? 'Everything you own. Use an item in the game to put it back on your island.'
              : 'Workers, machines, buildings, land and goods from Farmverse islands. Buy what you need, sell what you don’t.'}
          </p>
        </div>
        <label className="sort">
          <span>Sort</span>
          <select value={sort} onChange={(e) => setSort(e.target.value as Sort)}>
            <option value="newest">Newest</option>
            <option value="price_asc">Price: low to high</option>
            <option value="price_desc">Price: high to low</option>
          </select>
        </label>
      </div>

      <div className="chips" role="tablist" aria-label="Type">
        <a role="tab" aria-selected={!active} className={!active ? 'on' : ''} href={mine ? href.mine() : href.explore()}>
          <img className="fv-icon" src={sprite('Icon_Shop')} alt="" />
          All
        </a>
        {NFT_TYPES.map((t) => (
          <a key={t} role="tab" aria-selected={active === t} className={active === t ? 'on' : ''} href={href.explore(t)}>
            <img className="fv-icon" src={sprite(TYPE_ICON[t])} alt="" />
            {TYPE_LABEL[t]}
          </a>
        ))}
      </div>

      {rows.length === 0 ? (
        <div className="empty">
          <img className="empty-art" src={sprite('Home_Npc')} alt="" />
          <h2 className="fv-title">Nothing here yet</h2>
          <p className="muted">{search ? `No results for “${search}”.` : 'Send something from your island to the market to see it here.'}</p>
        </div>
      ) : (
        <div className="grid">
          {rows.map((t) => (
            <NftCard key={t.token_id} token={t} />
          ))}
        </div>
      )}
    </>
  );
}
