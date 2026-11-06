import { sprite } from '@farmverse/shared/gameTheme';
import { NFT_TYPES, TYPE_LABEL, type NftType } from '@farmverse/shared/nft/metadata';
import { useState } from 'react';
import { useListings, useTokensByIds } from '../api/queries';
import type { Sort, Token } from '../api/types';
import { useWallet } from '../chain/wallet';
import { useFlow } from '../components/Flows';
import { NftCard } from '../components/NftCard';
import { Select } from '../components/Select';
import { EmptyState, ErrorState, LoadMore, SkeletonGrid } from '../components/states';
import { TYPE_ICON } from '../components/ui';
import { useSaved } from '../lib/saved';
import { href } from '../router';

type Shelf = 'owned' | 'saved';

export function Explore({ type, mine, search }: { type?: string; mine?: boolean; search: string }) {
  const [sort, setSort] = useState<Sort>('newest');
  const [shelf, setShelf] = useState<Shelf>('owned');
  const open = useFlow();
  const { accountId, ready } = useWallet();
  const saved = useSaved();
  const active = NFT_TYPES.includes(type as NftType) ? (type as NftType) : undefined;
  const showSaved = !!mine && shelf === 'saved';

  const listings = useListings({ type: active, q: search || undefined, sort, owner: mine ? (accountId ?? undefined) : undefined }, !mine || (!!accountId && !showSaved));
  const savedQ = useTokensByIds(showSaved ? saved : []);

  if (mine && ready && !accountId) {
    return (
      <EmptyState art="Icon_MenuIcon02_Inventory" title="Your items live in your wallet">
        <p className="muted">Connect it to see what you own, sell things and use them in the game.</p>
        <button className="fv-btn green" onClick={() => open({ kind: 'wallet' })}>
          Connect wallet
        </button>
      </EmptyState>
    );
  }

  const pages = listings.data?.pages ?? [];
  let rows: Token[] = showSaved ? (savedQ.data ?? []).filter((t) => !active || t.metadata.type === active) : pages.flatMap((p) => p.items);
  if (showSaved && search) rows = rows.filter((t) => t.title.toLowerCase().includes(search.toLowerCase()));
  const total = showSaved ? rows.length : (pages[0]?.total ?? 0);
  const loading = showSaved ? savedQ.isPending && saved.length > 0 : listings.isPending || !ready;
  const error = showSaved ? savedQ.error : listings.error;
  const typeHref = (t?: NftType) => (mine ? href.mine(t) : href.explore(t));

  return (
    <>
      <div className="explore-head">
        <div>
          <h1 className="fv-title">{mine ? 'My items' : 'Explore'}</h1>
          <p className="muted">
            {mine
              ? 'Everything you own. Sell it, send it to a friend, or use it in the game.'
              : 'Workers, machines, buildings, land and goods from Farmverse islands. Buy what you need, sell what you don’t.'}
          </p>
        </div>
        {mine && (
          <div className="seg shelf" role="tablist" aria-label="Show">
            {(['owned', 'saved'] as Shelf[]).map((s) => (
              <button key={s} role="tab" aria-selected={shelf === s} className={shelf === s ? 'on' : ''} onClick={() => setShelf(s)}>
                {s === 'owned' ? 'Owned' : `Saved${saved.length ? ` · ${saved.length}` : ''}`}
              </button>
            ))}
          </div>
        )}
        <div className="sort">
          <span>Sort</span>
          <Select
            label="Sort"
            value={sort}
            onChange={(v) => setSort(v as Sort)}
            options={[
              { value: 'newest', label: 'Newest', icon: 'Icon_Star' },
              { value: 'price_asc', label: 'Price: low to high', icon: 'ResourceBar_Icon_Gold' },
              { value: 'price_desc', label: 'Price: high to low', icon: 'Icon_Golds' },
            ]}
          />
        </div>
      </div>

      <div className="chips" role="tablist" aria-label="Type">
        <a role="tab" aria-selected={!active} className={!active ? 'on' : ''} href={typeHref()}>
          <img className="fv-icon" src={sprite('Icon_Shop')} alt="" />
          All
        </a>
        {NFT_TYPES.map((t) => (
          <a key={t} role="tab" aria-selected={active === t} className={active === t ? 'on' : ''} href={typeHref(t)}>
            <img className="fv-icon" src={sprite(TYPE_ICON[t])} alt="" />
            {TYPE_LABEL[t]}
          </a>
        ))}
      </div>

      {loading ? (
        <SkeletonGrid />
      ) : error ? (
        <ErrorState title="Couldn’t load items" error={error} onRetry={() => (showSaved ? savedQ.refetch() : listings.refetch())} />
      ) : rows.length === 0 ? (
        <EmptyState art={showSaved ? 'Icon_Heart' : 'Home_Npc'} title={showSaved ? 'Nothing saved yet' : 'Nothing here yet'}>
          <p className="muted">
            {search
              ? `No results for “${search}”.`
              : showSaved
                ? 'Tap the heart on any item to keep it here.'
                : mine
                  ? 'Mint something on your island, or buy it here.'
                  : 'Nothing in this category yet.'}
          </p>
          {mine && (
            <a className="fv-btn green" href={href.explore()}>
              Browse the market
            </a>
          )}
        </EmptyState>
      ) : (
        <>
          <div className={`grid${listings.isPlaceholderData ? ' refreshing' : ''}`}>
            {rows.map((t) => (
              <NftCard key={t.token_id} token={t} badge={mine && !showSaved && t.price != null ? 'For sale' : undefined} />
            ))}
          </div>
          {!showSaved && (
            <LoadMore
              hasMore={!!listings.hasNextPage}
              loading={listings.isFetchingNextPage}
              onMore={() => listings.fetchNextPage()}
              shown={rows.length}
              total={total}
            />
          )}
        </>
      )}
    </>
  );
}
