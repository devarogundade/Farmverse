import { sprite } from '@farmverse/shared/gameTheme';
import { useEffect, useMemo, useState } from 'react';
import { useCodes, useCoupons, useRedeemCode, useSaveCoupons, type CodeRow, type CouponTask, type CouponType, type CouponsConfig } from '../api/admin';
import { Select } from '../components/Select';
import { dateTime, Empty, Failed, Field, fmt, Input, Loading, PageHead, Pager, Pill, ReasonDialog, timeAgo, Toggle } from '../components/ui';
import { href } from '../router';

type Tab = 'codes' | 'tasks' | 'types';

export function Coupons(r: { tab: Tab; q: string; status: string; typeId: string; page: number }) {
  return (
    <>
      <PageHead icon="Icon_Gift" title="Coupons">
        Partner vouchers players earn by finishing tasks.
      </PageHead>
      <div className="tabs" role="tablist">
        {(
          [
            ['codes', 'Codes'],
            ['tasks', 'Tasks'],
            ['types', 'Coupon types'],
          ] as const
        ).map(([k, label]) => (
          <a key={k} role="tab" aria-selected={r.tab === k} className={r.tab === k ? 'on' : ''} href={href.coupons(k)}>
            {label}
          </a>
        ))}
      </div>
      {r.tab === 'codes' ? <Codes {...r} /> : <Editor tab={r.tab} />}
    </>
  );
}

/* ------------------------------------------------------------------ codes */

function codeState(c: CodeRow): { tone: 'green' | 'gold' | 'muted'; label: string } {
  if (c.redeemedAt) return { tone: 'green', label: 'Redeemed' };
  if (c.expiresAt < Date.now()) return { tone: 'muted', label: 'Expired' };
  return { tone: 'gold', label: 'Waiting' };
}

function Codes({ q, status, typeId, page }: { q: string; status: string; typeId: string; page: number }) {
  const [text, setText] = useState(q);
  useEffect(() => setText(q), [q]);
  const types = useCoupons();
  const { data, error, isLoading, isFetching, refetch } = useCodes({ q, status, typeId, page });
  const redeem = useRedeemCode();
  const [picked, setPicked] = useState<CodeRow | null>(null);
  const go = (o: Partial<{ q: string; status: string; type: string; page: number }>) => (location.hash = href.coupons('codes', { q, status, type: typeId, page: 0, ...o }));

  return (
    <>
      <div className="toolbar">
        <form
          className="search"
          role="search"
          onSubmit={(e) => {
            e.preventDefault();
            go({ q: text.trim() });
          }}
        >
          <input value={text} onChange={(e) => setText(e.target.value)} placeholder="Code, or a player’s wallet" aria-label="Search codes" />
          <button type="submit" className="fv-chip" aria-label="Search">
            <img className="fv-icon" src={sprite('Icon_PictoIcon_Search')} alt="" />
          </button>
        </form>
        <Select
          label="Status"
          value={status}
          onChange={(v) => go({ status: v })}
          options={[
            { value: 'all', label: 'All codes' },
            { value: 'open', label: 'Waiting to be redeemed' },
            { value: 'redeemed', label: 'Redeemed' },
            { value: 'expired', label: 'Expired' },
          ]}
        />
        <Select
          label="Coupon type"
          value={typeId || 'all'}
          onChange={(v) => go({ type: v === 'all' ? '' : v })}
          options={[{ value: 'all', label: 'Every type' }, ...Object.entries(types.data?.types ?? {}).map(([id, t]) => ({ value: id, label: t.name }))]}
        />
      </div>
      {isLoading ? (
        <Loading />
      ) : error ? (
        <Failed error={error} retry={() => void refetch()} />
      ) : !data?.rows.length ? (
        <Empty title="No codes" art="Icon_Gift">
          {q || status !== 'all' || typeId ? 'Nothing matches. Try another search.' : 'No player has earned a coupon yet.'}
        </Empty>
      ) : (
        <>
          <ul className={`rows${isFetching ? ' refreshing' : ''}`}>
            <li className="rows-head code-row" aria-hidden="true">
              <span>Code</span>
              <span>Worth</span>
              <span>Player</span>
              <span>Given</span>
              <span>Status</span>
            </li>
            {data.rows.map((c) => {
              const s = codeState(c);
              return (
                <li key={c.code} className="fv-row code-row">
                  <span className="code-cell">
                    <code>{c.code}</code>
                    <small className="muted">{c.task}</small>
                  </span>
                  <span data-label="Worth">
                    {fmt(c.amount)} {c.unit}
                  </span>
                  <span data-label="Player">
                    <a className="link" href={href.player(c.uid)}>
                      {c.accountId ?? 'unknown'}
                    </a>
                  </span>
                  <span data-label="Given" className="muted" title={dateTime(c.issuedAt)}>
                    {timeAgo(c.issuedAt)}
                  </span>
                  <span className="code-status">
                    <Pill tone={s.tone}>{s.label}</Pill>
                    {s.label === 'Waiting' && (
                      <button className="fv-btn green sm" onClick={() => setPicked(c)}>
                        Redeem
                      </button>
                    )}
                    {c.redeemedAt > 0 && <small className="muted">{timeAgo(c.redeemedAt)}</small>}
                  </span>
                </li>
              );
            })}
          </ul>
          <Pager page={page} more={data.more} go={(n) => go({ page: n })} />
        </>
      )}
      <ReasonDialog
        open={picked !== null}
        onClose={() => setPicked(null)}
        title="Mark as redeemed?"
        icon="Icon_ImageIcon_ClearStamp_l"
        confirm="Mark redeemed"
        done="Code marked redeemed"
        onConfirm={(reason) => redeem.mutateAsync({ code: picked!.code, reason })}
      >
        {picked && (
          <dl className="sum">
            <div>
              <dt>Code</dt>
              <dd>
                <code>{picked.code}</code>
              </dd>
            </div>
            <div>
              <dt>Worth</dt>
              <dd>
                {fmt(picked.amount)} {picked.unit}
              </dd>
            </div>
            <div>
              <dt>Player</dt>
              <dd>{picked.accountId}</dd>
            </div>
            <div>
              <dt>Expires</dt>
              <dd>{dateTime(picked.expiresAt)}</dd>
            </div>
          </dl>
        )}
        <p className="note">Do this once you have paid it out. A code can only be redeemed once.</p>
      </ReasonDialog>
    </>
  );
}

/* ------------------------------------------------------------------ tasks and types */

const PROGRESS: Record<string, string> = {
  'today:harvested': 'Crops harvested today',
  'today:planted': 'Crops planted today',
  'today:orders': 'Orders filled today',
  'today:chopped': 'Trees chopped today',
  'today:sold': 'Goods sold today',
  harvested: 'Crops harvested, ever',
  orders: 'Orders filled, ever',
  building_upgrades: 'Building upgrades, ever',
  goals: 'Story goals finished',
  level: 'Island level reached',
};

const NEW_TASK = (typeId: string, order: number): CouponTask => ({
  name: 'New task',
  art: 'Icon_Star',
  typeId,
  daily: true,
  progress: 'today:harvested',
  of: 10,
  lockLevel: 0,
  order,
  amount: 1,
});

/** Common partner colours; the hex box takes any other. */
const SWATCHES = ['#26a17b', '#2775ca', '#f3ba2f', '#e84142', '#8247e5'];

function ColourPick({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const [text, setText] = useState(value);
  useEffect(() => setText(value), [value]);
  return (
    <span className="swatches" role="group" aria-label="Logo colour">
      {SWATCHES.map((c) => (
        <button key={c} type="button" className="swatch" style={{ background: c }} aria-label={c} aria-pressed={value.toLowerCase() === c} onClick={() => onChange(c)} />
      ))}
      <Input
        value={text}
        maxLength={7}
        aria-label="Colour as a hex code"
        onChange={(e) => {
          const v = e.target.value.trim();
          setText(v);
          if (/^#[0-9a-f]{6}$/i.test(v)) onChange(v.toLowerCase());
        }}
      />
    </span>
  );
}

const NEW_TYPE: CouponType = {
  name: 'New coupon',
  logoArt: '',
  logoGlyph: '?',
  logoBg: '#26a17b',
  accentA: '#7fe3c4',
  accentB: '#26a17b',
  ink: '#073b2c',
  unit: 'UNIT',
  codePrefix: 'NEW',
  redeemUrl: 'https://farmverse.world/redeem',
  redeemLabel: 'Open redeem page',
  steps: ['Tap <b>Open redeem page</b>.', 'Paste your code and confirm.'],
  expiryDays: 30,
  linkedOnly: true,
};

function nextId(prefix: string, taken: Record<string, unknown>) {
  for (let i = 1; ; i++) if (!taken[`${prefix}${i}`]) return `${prefix}${i}`;
}

function Editor({ tab }: { tab: 'tasks' | 'types' }) {
  const { data, error, isLoading, refetch } = useCoupons();
  const save = useSaveCoupons();
  const [draft, setDraft] = useState<Pick<CouponsConfig, 'types' | 'tasks'> | null>(null);
  const [confirming, setConfirming] = useState(false);
  useEffect(() => {
    if (data) setDraft({ types: data.types, tasks: data.tasks });
  }, [data]);
  const dirty = useMemo(() => !!data && !!draft && JSON.stringify({ types: data.types, tasks: data.tasks }) !== JSON.stringify(draft), [data, draft]);

  if (isLoading || !draft) return error ? <Failed error={error} retry={() => void refetch()} /> : <Loading />;
  if (!data) return null;

  const typeOptions = Object.entries(draft.types).map(([id, t]) => ({ value: id, label: t.name }));
  const setTask = (id: string, patch: Partial<CouponTask>) => setDraft({ ...draft, tasks: { ...draft.tasks, [id]: { ...draft.tasks[id]!, ...patch } } });
  const setType = (id: string, patch: Partial<CouponType>) => setDraft({ ...draft, types: { ...draft.types, [id]: { ...draft.types[id]!, ...patch } } });
  const drop = <K extends 'tasks' | 'types'>(k: K, id: string) => {
    const next = { ...draft[k] };
    delete next[id];
    setDraft({ ...draft, [k]: next });
  };
  const used = (typeId: string) => Object.values(draft.tasks).some((t) => t.typeId === typeId);
  const tasks = Object.entries(draft.tasks).sort(([, a], [, b]) => a.order - b.order);

  return (
    <>
      <div className={`source-note ${data.source}`}>
        <img src={sprite(data.source === 'published' ? 'Icon_ImageIcon_ClearStamp_l' : 'Icon_ImageIcon_Info')} alt="" />
        <span>
          {data.source === 'published'
            ? `Live since ${dateTime(data.publishedAt)}. Saving replaces it for every player within a minute.`
            : 'These are the coupons built into the game. Saving makes your copy live for every player within a minute.'}{' '}
          A game update with new built-in coupons replaces saved changes.
        </span>
      </div>

      {tab === 'tasks' ? (
        <div className="editor">
          {tasks.map(([id, t]) => (
            <div key={id} className="edit-card fv-panel">
              <div className="edit-head">
                <Input className="edit-name" value={t.name} maxLength={40} onChange={(e) => setTask(id, { name: e.target.value })} aria-label="Task name" />
                <Pill tone="muted">{t.daily ? 'Daily' : 'Once'}</Pill>
                <button className="fv-chip sm-chip" aria-label={`Remove ${t.name}`} onClick={() => drop('tasks', id)}>
                  <img className="fv-icon" src={sprite('Icon_PictoIcon_Delete')} alt="" />
                </button>
              </div>
              <div className="edit-grid">
                <Field label="Pays">
                  <Select size="sm" value={t.typeId} onChange={(v) => setTask(id, { typeId: v })} options={typeOptions} label="Coupon type" />
                </Field>
                <Field label="Worth">
                  <Input type="number" min={0.01} step="any" value={t.amount} onChange={(e) => setTask(id, { amount: Number(e.target.value) })} />
                </Field>
                <Field label="Counts">
                  <Select
                    size="sm"
                    value={t.progress}
                    onChange={(v) => setTask(id, { progress: v, daily: v.startsWith('today:') })}
                    options={[...Object.entries(PROGRESS), ...(PROGRESS[t.progress] ? [] : [[t.progress, t.progress]])].map(([value, label]) => ({ value, label }))}
                    label="What it counts"
                  />
                </Field>
                <Field label="Target">
                  <Input type="number" min={1} value={t.of} onChange={(e) => setTask(id, { of: Number(e.target.value) })} />
                </Field>
                <Field label="Unlocks at level">
                  <Input type="number" min={0} max={200} value={t.lockLevel} onChange={(e) => setTask(id, { lockLevel: Number(e.target.value) })} />
                </Field>
                <Field label="Position">
                  <Input type="number" min={0} value={t.order} onChange={(e) => setTask(id, { order: Number(e.target.value) })} />
                </Field>
              </div>
            </div>
          ))}
          <button className="add-card" onClick={() => setDraft({ ...draft, tasks: { ...draft.tasks, [nextId('t', draft.tasks)]: NEW_TASK(typeOptions[0]?.value ?? '', tasks.length + 1) } })} disabled={!typeOptions.length}>
            <img src={sprite('Icon_PictoIcon_Add01')} alt="" />
            Add a task
          </button>
        </div>
      ) : (
        <div className="editor">
          {Object.entries(draft.types).map(([id, t]) => (
            <div key={id} className="edit-card fv-panel">
              <div className="edit-head">
                <span className="type-logo" style={{ background: t.logoBg, color: t.ink }}>
                  {t.logoGlyph || t.name[0]}
                </span>
                <Input className="edit-name" value={t.name} maxLength={16} onChange={(e) => setType(id, { name: e.target.value })} aria-label="Coupon name" />
                <button
                  className="fv-chip sm-chip"
                  aria-label={`Remove ${t.name}`}
                  disabled={used(id)}
                  title={used(id) ? 'A task still pays this coupon' : undefined}
                  onClick={() => drop('types', id)}
                >
                  <img className="fv-icon" src={sprite('Icon_PictoIcon_Delete')} alt="" />
                </button>
              </div>
              <div className="edit-grid">
                <Field label="Unit">
                  <Input value={t.unit} maxLength={12} onChange={(e) => setType(id, { unit: e.target.value })} />
                </Field>
                <Field label="Code starts with">
                  <Input value={t.codePrefix} maxLength={8} placeholder="2–8 letters or digits" onChange={(e) => setType(id, { codePrefix: e.target.value.toUpperCase() })} />
                </Field>
                <Field label="Expires after (days)">
                  <Input type="number" min={1} max={365} value={t.expiryDays} onChange={(e) => setType(id, { expiryDays: Number(e.target.value) })} />
                </Field>
                <Field label="Redeem page">
                  <Input type="url" value={t.redeemUrl} onChange={(e) => setType(id, { redeemUrl: e.target.value })} />
                </Field>
                <Field label="Button text">
                  <Input value={t.redeemLabel} maxLength={32} onChange={(e) => setType(id, { redeemLabel: e.target.value })} />
                </Field>
                <Field label="Wallet players only">
                  <Toggle on={t.linkedOnly} onChange={(v) => setType(id, { linkedOnly: v })} label="Wallet players only" />
                </Field>
                <Field label="Logo colour" group>
                  <ColourPick value={t.logoBg} onChange={(logoBg) => setType(id, { logoBg })} />
                </Field>
                <Field label="Logo letter">
                  <Input value={t.logoGlyph} maxLength={4} onChange={(e) => setType(id, { logoGlyph: e.target.value })} />
                </Field>
              </div>
              <Field label="How to redeem" hint="One step per line. Put words in <b>…</b> to show them in gold.">
                <textarea className="input" rows={4} value={t.steps.join('\n')} onChange={(e) => setType(id, { steps: e.target.value.split('\n').slice(0, 8) })} />
              </Field>
            </div>
          ))}
          <button className="add-card" onClick={() => setDraft({ ...draft, types: { ...draft.types, [nextId('c', draft.types)]: NEW_TYPE } })}>
            <img src={sprite('Icon_PictoIcon_Add01')} alt="" />
            Add a coupon type
          </button>
        </div>
      )}

      <div className={`save-bar fv-panel${dirty ? ' show' : ''}`} aria-hidden={!dirty}>
        <span>You have unsaved changes.</span>
        <button className="fv-btn gray sm" onClick={() => setDraft({ types: data.types, tasks: data.tasks })} tabIndex={dirty ? 0 : -1}>
          Undo
        </button>
        <button className="fv-btn yellow sm" onClick={() => setConfirming(true)} tabIndex={dirty ? 0 : -1}>
          Save and go live
        </button>
      </div>

      <ReasonDialog
        open={confirming}
        onClose={() => setConfirming(false)}
        title="Make these coupons live?"
        icon="Icon_Gift"
        confirm="Go live"
        tone="yellow"
        done="Coupons updated"
        onConfirm={(reason) => save.mutateAsync({ ...draft, tasks: Object.fromEntries(Object.entries(draft.tasks).map(([k, v]) => [k, { ...v, daily: v.progress.startsWith('today:') }])), reason })}
      >
        <p className="note">Every player sees the new tasks and amounts within a minute. Codes already given out keep their value.</p>
      </ReasonDialog>
    </>
  );
}
