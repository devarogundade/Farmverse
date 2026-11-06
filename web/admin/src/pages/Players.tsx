import { sprite } from '@farmverse/shared/gameTheme';
import { useEffect, useState } from 'react';
import { usePlayers } from '../api/admin';
import { Select } from '../components/Select';
import { Avatar, Empty, Failed, fmt, Loading, PageHead, Pager, Pill, timeAgo } from '../components/ui';
import { href } from '../router';

export function Players({ q, status, sort, page }: { q: string; status: string; sort: string; page: number }) {
  const [text, setText] = useState(q);
  useEffect(() => setText(q), [q]);
  const { data, error, isLoading, isFetching, refetch } = usePlayers({ q, status, sort, page });
  const go = (o: Partial<{ q: string; status: string; sort: string; page: number }>) => (location.hash = href.players({ q, status, sort, page: 0, ...o }));

  return (
    <>
      <PageHead icon="Icon_Friends" title="Players">
        Find a player by wallet, name or player number.
      </PageHead>
      <div className="toolbar">
        <form
          className="search"
          role="search"
          onSubmit={(e) => {
            e.preventDefault();
            go({ q: text.trim() });
          }}
        >
          <input value={text} onChange={(e) => setText(e.target.value)} placeholder="Wallet, name or player number" aria-label="Search players" />
          <button type="submit" className="fv-chip" aria-label="Search">
            <img className="fv-icon" src={sprite('Icon_PictoIcon_Search')} alt="" />
          </button>
        </form>
        <Select
          label="Show"
          value={status}
          onChange={(v) => go({ status: v })}
          options={[
            { value: 'all', label: 'Everyone' },
            { value: 'banned', label: 'Banned only' },
          ]}
        />
        <Select
          label="Sort"
          value={sort}
          onChange={(v) => go({ sort: v })}
          options={[
            { value: 'newest', label: 'Newest first' },
            { value: 'last_seen', label: 'Played most recently' },
            { value: 'level', label: 'Highest level' },
          ]}
        />
      </div>

      {isLoading ? (
        <Loading />
      ) : error ? (
        <Failed error={error} retry={() => void refetch()} />
      ) : !data?.rows.length ? (
        <Empty title="No players found">{q ? 'Try part of the wallet name, or the exact player number.' : 'Nobody has signed in yet.'}</Empty>
      ) : (
        <>
          <ul className={`rows${isFetching ? ' refreshing' : ''}`}>
            <li className="rows-head" aria-hidden="true">
              <span>Player</span>
              <span>Level</span>
              <span>Worth</span>
              <span>Last played</span>
              <span>Joined</span>
            </li>
            {data.rows.map((p) => (
              <li key={p.uid}>
                <a className="row-link fv-row player-row" href={href.player(p.uid)}>
                  <span className="who">
                    <span className="fv-row-ava">
                      <Avatar seed={p.accountId} label={p.name ?? p.accountId} />
                    </span>
                    <span className="who-text">
                      <b>
                        <span className="who-name">{p.name ?? 'No island yet'}</span>
                        {p.bannedAt && <Pill tone="pink">Banned</Pill>}
                      </b>
                      <small className="muted">
                        {p.accountId}
                        {p.shortId && ` · #${p.shortId}`}
                      </small>
                    </span>
                  </span>
                  <span data-label="Level">{p.level ?? '—'}</span>
                  <span data-label="Worth">{fmt(p.netWorth)}</span>
                  <span data-label="Last played">{timeAgo(p.lastSeenAt)}</span>
                  <span data-label="Joined" className="muted">
                    {timeAgo(p.createdAt)}
                  </span>
                </a>
              </li>
            ))}
          </ul>
          <Pager page={page} more={data.more} go={(n) => go({ page: n })} />
        </>
      )}
    </>
  );
}
