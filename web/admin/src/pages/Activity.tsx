import { shortAccount } from '@farmverse/shared/account';
import { useState } from 'react';
import { useAudit } from '../api/admin';
import { Select } from '../components/Select';
import { Avatar, dateTime, Empty, Failed, Loading, PageHead } from '../components/ui';
import { actionName, grantSummary } from '../lib/names';
import { href } from '../router';

const ABOUT = [
  { value: 'all', label: 'Everything' },
  { value: 'player', label: 'Players' },
  { value: 'coupon', label: 'Coupon codes' },
  { value: 'catalogue', label: 'Coupon tasks' },
  { value: 'ops', label: 'Game settings' },
  { value: 'news', label: 'News' },
];

export function Activity({ targetType }: { targetType: string }) {
  const [pages, setPages] = useState<number[]>([]);
  const before = pages[pages.length - 1];
  const { data, error, isLoading, isFetching, refetch } = useAudit({ targetType: targetType || undefined, before });
  const entries = data?.entries ?? [];

  return (
    <>
      <PageHead icon="Icon_Quest" title="Activity">
        Every change the team made here, newest first.
      </PageHead>
      <div className="toolbar">
        <Select
          label="About"
          value={targetType || 'all'}
          onChange={(v) => {
            setPages([]);
            location.hash = href.activity(v === 'all' ? '' : v);
          }}
          options={ABOUT}
        />
      </div>
      {isLoading ? (
        <Loading />
      ) : error ? (
        <Failed error={error} retry={() => void refetch()} />
      ) : !entries.length ? (
        <Empty title="Nothing here yet" art="Icon_Quest">
          Changes made from this site show up here.
        </Empty>
      ) : (
        <>
          <ul className={`rows${isFetching ? ' refreshing' : ''}`}>
            {entries.map((e) => (
              <li key={e.id} className="fv-row activity-row">
                <span className="who">
                  <span className="fv-row-ava">
                    <Avatar seed={e.actor} />
                  </span>
                  <span className="who-text">
                    <b>{actionName(e.action)}</b>
                    <small className="muted" title={e.actor}>
                      {shortAccount(e.actor)}
                    </small>
                  </span>
                </span>
                <span className="activity-what">
                  {e.targetType === 'player' ? (
                    <a className="link" href={href.player(e.targetId)}>
                      Open player
                    </a>
                  ) : e.targetType === 'coupon' ? (
                    <a className="link" href={href.coupons('codes', { q: e.targetId })}>
                      <code>{e.targetId}</code>
                    </a>
                  ) : e.targetType === 'news' ? (
                    String(e.details?.['title'] ?? '')
                  ) : null}
                  {e.action === 'player.grant' && <span className="muted"> {grantSummary(e.details)}</span>}
                  <p>“{e.reason}”</p>
                </span>
                <span className="muted activity-at">{dateTime(e.at)}</span>
              </li>
            ))}
          </ul>
          <nav className="pager" aria-label="Pages">
            <button className="fv-btn gray sm" disabled={!pages.length} onClick={() => setPages(pages.slice(0, -1))}>
              Newer
            </button>
            <button className="fv-btn gray sm" disabled={entries.length < 50} onClick={() => setPages([...pages, entries[entries.length - 1]!.at])}>
              Older
            </button>
          </nav>
        </>
      )}
    </>
  );
}
