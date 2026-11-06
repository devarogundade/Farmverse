import { sprite } from '@farmverse/shared/gameTheme';
import { useState, type ReactNode } from 'react';
import { useGrant, useMe, usePlayer, usePlayerAction, type AuditEntry, type GrantInput } from '../api/admin';
import { Dialog, toast } from '../components/Dialog';
import { Select } from '../components/Select';
import { Avatar, dateTime, Failed, Field, fmt, Input, Loading, Pill, ReasonDialog, ReasonField, timeAgo } from '../components/ui';
import { actionName, boostName, GOODS, goodName, grantSummary } from '../lib/names';
import { href } from '../router';
import { useSession } from '../session';

function Card({ title, icon, children, wide }: { title: string; icon: string; children: ReactNode; wide?: boolean }) {
  return (
    <section className={`card fv-panel${wide ? ' wide' : ''}`}>
      <h2 className="card-title">
        <img src={sprite(icon)} alt="" />
        {title}
      </h2>
      {children}
    </section>
  );
}

function Facts({ items }: { items: [string, ReactNode][] }) {
  return (
    <dl className="facts">
      {items.map(([k, v]) => (
        <div key={k}>
          <dt>{k}</dt>
          <dd>{v}</dd>
        </div>
      ))}
    </dl>
  );
}

export function AuditList({ entries }: { entries: AuditEntry[] }) {
  if (!entries.length) return <p className="muted">Nothing yet.</p>;
  return (
    <ul className="log">
      {entries.map((e) => (
        <li key={e.id}>
          <b>{actionName(e.action)}</b>
          {e.action === 'player.grant' && <span> · {grantSummary(e.details)}</span>}
          <p>“{e.reason}”</p>
          <small className="muted">
            {e.actor} · {dateTime(e.at)}
          </small>
        </li>
      ))}
    </ul>
  );
}

export function PlayerPage({ uid }: { uid: string }) {
  const { data: d, error, isLoading, refetch } = usePlayer(uid);
  const action = usePlayerAction(uid);
  const [open, setOpen] = useState<null | 'gift' | 'ban' | 'unban' | 'signout'>(null);

  // The way back stays put while the player loads
  const back = (
    <nav className="crumbs">
      <a href={href.players()}>
        <img className="crumb-back" src={sprite('Icon_PictoIcon_Back')} alt="" />
        Players
      </a>
    </nav>
  );
  if (isLoading) return <>{back}<Loading /></>;
  if (error || !d) return <>{back}<Failed error={error} retry={() => void refetch()} /></>;
  const p = d.player;
  const unclaimed = d.rewards.filter((r) => !r.claimedAt);

  return (
    <>
      {back}
      <div className="player-head fv-panel">
        <Avatar seed={p.accountId} label={p.name ?? p.accountId} size={72} />
        <div className="player-id">
          <h1 className="fv-title">
            {p.name ?? 'No island yet'} {p.bannedAt && <Pill tone="pink">Banned</Pill>}
          </h1>
          <p className="muted">
            {p.accountId}
            {p.shortId && ` · Player #${p.shortId}`}
          </p>
          {p.bannedAt && (
            <p className="ban-note">
              Banned {timeAgo(p.bannedAt)}: “{d.banReason}”
            </p>
          )}
        </div>
        <div className="player-actions">
          <button className="fv-btn yellow" onClick={() => setOpen('gift')}>
            <img src={sprite('Icon_Gift')} alt="" />
            Send a gift
          </button>
          <button className="fv-btn blue" onClick={() => setOpen('signout')}>
            Sign out everywhere
          </button>
          {p.bannedAt ? (
            <button className="fv-btn green" onClick={() => setOpen('unban')}>
              Unban
            </button>
          ) : (
            <button className="fv-btn purple" onClick={() => setOpen('ban')}>
              Ban
            </button>
          )}
        </div>
      </div>

      <div className="cards">
        <Card title="Island" icon="Isl_b_farmhouse">
          <Facts
            items={[
              ['Level', p.level ?? '—'],
              ['Worth', p.netWorth == null ? '—' : `${fmt(p.netWorth)} coins`],
              ['XP', fmt(d.xp)],
              ['Last saved', timeAgo(d.islandUpdatedAt)],
              ['Last played', timeAgo(p.lastSeenAt)],
              ['Joined', dateTime(p.createdAt)],
            ]}
          />
        </Card>
        <Card title="Wallet" icon="ResourceBar_Icon_Gem">
          <Facts
            items={[
              ['Gems', fmt(d.gems)],
              ['Boosts', Object.keys(d.boosts).length ? Object.entries(d.boosts).map(([id, n]) => `${n} ${boostName(id)}`).join(', ') : 'none'],
              ['Signed in on', `${d.sessions.active} device${d.sessions.active === 1 ? '' : 's'}`],
              ['Last sign-in', timeAgo(p.lastLoginAt)],
            ]}
          />
        </Card>
        <Card title="Waiting to collect" icon="Icon_Mail">
          {!unclaimed.length && !d.pendingGrants.length ? (
            <p className="muted">Nothing waiting. Gifts show here until the player collects them in the game.</p>
          ) : (
            <ul className="mini">
              {unclaimed.map((r) => (
                <li key={r.id}>
                  <span>
                    {[r.coins ? `${fmt(r.coins)} coins` : '', r.gems ? `${fmt(r.gems)} gems` : ''].filter(Boolean).join(' + ')}
                    <small className="muted"> · {r.reason === 'mail' ? 'gift' : String(r.reason ?? '').replace(/_/g, ' ')}</small>
                  </span>
                  <small className="muted">{timeAgo(r.createdAt)}</small>
                </li>
              ))}
              {d.pendingGrants.map((g) => (
                <li key={g.id}>
                  <span>
                    {g.ops.map((o) => (o.op === 'give' ? `${o.n} ${goodName(o.item ?? '')}` : goodName(o.id ?? ''))).join(', ')}
                    <small className="muted"> · arrives on the island next time it loads</small>
                  </span>
                  <small className="muted">{timeAgo(g.at)}</small>
                </li>
              ))}
            </ul>
          )}
        </Card>
        <Card title="Coupons" icon="Icon_Gift">
          {!d.coupons.length ? (
            <p className="muted">No coupons yet.</p>
          ) : (
            <ul className="mini">
              {d.coupons.map((c) => (
                <li key={c.id}>
                  <a href={href.coupons('codes', { q: c.code })}>
                    <code>{c.code}</code> <small className="muted">{c.task}</small>
                  </a>
                  <small className="muted">{timeAgo(c.issuedAt)}</small>
                </li>
              ))}
            </ul>
          )}
        </Card>
        <Card title="Items on the market side" icon="Icon_Shop">
          {!d.nfts.length ? (
            <p className="muted">This wallet holds no Farmverse items.</p>
          ) : (
            <ul className="mini">
              {d.nfts.map((n) => (
                <li key={n.tokenId}>
                  <span>{n.title}</span>
                  <small className="muted">minted {timeAgo(n.mintedAt)}</small>
                </li>
              ))}
            </ul>
          )}
        </Card>
        <Card title="Team activity" icon="Icon_Quest">
          <AuditList entries={d.audit} />
        </Card>
      </div>

      <GiftDialog open={open === 'gift'} onClose={() => setOpen(null)} uid={uid} name={p.name ?? p.accountId} />
      <ReasonDialog
        open={open === 'ban'}
        onClose={() => setOpen(null)}
        title={`Ban ${p.name ?? p.accountId}?`}
        icon="Icon_ImageIcon_Lock01_s"
        confirm="Ban"
        tone="purple"
        done="Player banned"
        onConfirm={(reason) => action.mutateAsync({ action: 'ban', reason })}
      >
        <p className="note">They are signed out everywhere and can’t sign in again until unbanned. A game already open stops working within 15 minutes.</p>
      </ReasonDialog>
      <ReasonDialog
        open={open === 'unban'}
        onClose={() => setOpen(null)}
        title={`Unban ${p.name ?? p.accountId}?`}
        confirm="Unban"
        done="Player unbanned"
        onConfirm={(reason) => action.mutateAsync({ action: 'unban', reason })}
      >
        <p className="note">They can sign in again straight away.</p>
      </ReasonDialog>
      <ReasonDialog
        open={open === 'signout'}
        onClose={() => setOpen(null)}
        title="Sign out everywhere?"
        confirm="Sign out"
        tone="yellow"
        done="Signed out on every device"
        onConfirm={(reason) => action.mutateAsync({ action: 'signout', reason })}
      >
        <p className="note">Every device has to sign in again with the wallet. Their island is not changed.</p>
      </ReasonDialog>
    </>
  );
}

type Row = { id: string; n: number };

function Rows({ rows, setRows, options, addLabel, max }: { rows: Row[]; setRows: (r: Row[]) => void; options: { value: string; label: string }[]; addLabel: string; max: number }) {
  const free = options.filter((o) => !rows.some((r) => r.id === o.value));
  return (
    <div className="grant-rows">
      {rows.map((r, i) => (
        <div className="grant-row" key={r.id}>
          <Select size="sm" value={r.id} onChange={(v) => setRows(rows.map((x, j) => (j === i ? { ...x, id: v } : x)))} options={[...options.filter((o) => o.value === r.id), ...free]} label="Item" />
          <Input type="number" min={1} max={max} value={r.n} onChange={(e) => setRows(rows.map((x, j) => (j === i ? { ...x, n: Number(e.target.value) } : x)))} aria-label="How many" />
          <button type="button" className="fv-chip sm-chip" aria-label="Remove" onClick={() => setRows(rows.filter((_, j) => j !== i))}>
            <img className="fv-icon" src={sprite('Icon_PictoIcon_Close')} alt="" />
          </button>
        </div>
      ))}
      {free.length > 0 && (
        <button type="button" className="link-btn" onClick={() => setRows([...rows, { id: free[0]!.value, n: 1 }])}>
          + {addLabel}
        </button>
      )}
    </div>
  );
}

function GiftDialog({ open, onClose, uid, name }: { open: boolean; onClose: () => void; uid: string; name: string }) {
  const { status } = useSession();
  const me = useMe(status === 'signed-in');
  const grant = useGrant(uid);
  const [coins, setCoins] = useState(0);
  const [gems, setGems] = useState(0);
  const [goods, setGoods] = useState<Row[]>([]);
  const [boosts, setBoosts] = useState<Row[]>([]);
  const [title, setTitle] = useState('A gift from the Farmverse team');
  const [message, setMessage] = useState('');
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const nothing = coins <= 0 && gems <= 0 && !goods.length && !boosts.length;

  const reset = () => {
    setCoins(0);
    setGems(0);
    setGoods([]);
    setBoosts([]);
    setMessage('');
    setReason('');
    setError(null);
  };

  return (
    <Dialog open={open} onClose={() => !grant.isPending && (reset(), onClose())} title={`Gift for ${name}`} icon="Icon_Gift">
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          setError(null);
          const input: GrantInput = {
            coins,
            gems,
            goods: Object.fromEntries(goods.filter((g) => g.n > 0).map((g) => [g.id, g.n])),
            boosts: Object.fromEntries(boosts.filter((b) => b.n > 0).map((b) => [b.id, b.n])),
            title: title.trim(),
            message: message.trim(),
            reason: reason.trim(),
          };
          try {
            await grant.mutateAsync(input);
            toast(`Gift sent to ${name}`, 'Icon_Gift');
            reset();
            onClose();
          } catch (err) {
            setError((err as Error).message);
          }
        }}
      >
        <div className="two">
          <Field label="Coins">
            <div className="amount-box">
              <img src={sprite('ResourceBar_Icon_Gold')} alt="" />
              <input type="number" min={0} max={1_000_000} value={coins} onChange={(e) => setCoins(Number(e.target.value))} aria-label="Coins" />
            </div>
          </Field>
          <Field label="Gems">
            <div className="amount-box">
              <img src={sprite('ResourceBar_Icon_Gem')} alt="" />
              <input type="number" min={0} max={10_000} value={gems} onChange={(e) => setGems(Number(e.target.value))} aria-label="Gems" />
            </div>
          </Field>
        </div>
        <Field label="Goods" hint="Go into the island’s farmhouses the next time it loads.">
          <Rows rows={goods} setRows={setGoods} options={GOODS.map((g) => ({ value: g.id, label: g.label }))} addLabel="Add goods" max={100_000} />
        </Field>
        <Field label="Boosts">
          <Rows rows={boosts} setRows={setBoosts} options={(me.data?.boosts ?? []).map((b) => ({ value: b, label: boostName(b) }))} addLabel="Add a boost" max={1_000} />
        </Field>
        <Field label="Message title" hint="The player sees this in their inbox. Coins and gems are collected from there.">
          <Input value={title} maxLength={60} onChange={(e) => setTitle(e.target.value)} required />
        </Field>
        <Field label="Message">
          <Input value={message} maxLength={64} onChange={(e) => setMessage(e.target.value)} placeholder="Sorry about the lost harvest!" />
        </Field>
        <ReasonField value={reason} onChange={setReason} />
        {error && <p className="form-err">{error}</p>}
        <div className="dlg-actions">
          <button type="button" className="fv-btn gray" onClick={() => (reset(), onClose())}>
            Cancel
          </button>
          <button type="submit" className="fv-btn yellow" disabled={grant.isPending || nothing || reason.trim().length < 3}>
            {grant.isPending ? 'Sending…' : 'Send gift'}
          </button>
        </div>
      </form>
    </Dialog>
  );
}
