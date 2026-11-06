import { sprite } from '@farmverse/shared/gameTheme';
import { displayNo, modelLabel } from '@farmverse/shared/nft/metadata';
import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import { useMarket } from '../actions/useMarket';
import { useBalance, useFeeRate, useMarkNotesRead, useNotes, useToken } from '../api/queries';
import type { Offer, Token } from '../api/types';
import { useWallet } from '../chain/wallet';
import { href } from '../router';
import { isAccount } from '../lib/account';
import { Dialog, Done, WalletWait, toast, useRun } from './Dialog';
import { Select } from './Select';
import { AccountAvatar, FRAME, FarmIcon, TYPE_ICON, fmt, timeAgo } from './ui';

export type Flow =
  | { kind: 'buy' | 'offer' | 'list' | 'delist' | 'send' | 'redeem'; token: Token }
  | { kind: 'accept' | 'decline' | 'cancelOffer'; offer: Offer; token: Token }
  | { kind: 'wallet' }
  | { kind: 'notes' };

const FlowContext = createContext<(f: Flow) => void>(() => {});
/** Open a marketplace flow (buy, list, offer…) from anywhere. Asks to connect a wallet first if needed. */
export const useFlow = () => useContext(FlowContext);

const needsAccount = (f: Flow) => f.kind !== 'wallet' && f.kind !== 'notes';
/** Signed in as the owner of the item they were about to buy or bid on: nothing to do. */
const ownItem = (f: Flow, account: string) => (f.kind === 'buy' || f.kind === 'offer') && f.token.owner_id === account;

export function FlowHost({ children }: { children: ReactNode }) {
  const [flow, setFlow] = useState<Flow | null>(null);
  const wallet = useWallet();

  const continueWith = useCallback((f: Flow | null, account: string) => {
    if (f && ownItem(f, account)) {
      toast('Signed in — this item is already yours', 'Icon_PictoIcon_User');
      setFlow(null);
    } else setFlow(f);
  }, []);

  const open = useCallback(
    (f: Flow) => {
      if ((needsAccount(f) || f.kind === 'wallet') && !wallet.accountId) {
        // NEAR Connect shows its own wallet picker; then carry on with what they were doing.
        wallet.connect().then(
          (account) => needsAccount(f) && continueWith(f, account),
          () => toast('Wallet not connected', 'Icon_ImageIcon_Info'),
        );
        return;
      }
      setFlow(f);
    },
    [wallet, continueWith],
  );

  const close = () => setFlow(null);

  return (
    <FlowContext.Provider value={open}>
      {children}
      {flow && <FlowDialog key={flowKey(flow)} flow={flow} onClose={close} />}
    </FlowContext.Provider>
  );
}

const flowKey = (f: Flow) => f.kind + ('token' in f ? f.token.token_id : '') + ('offer' in f ? f.offer.id : '');

function FlowDialog({ flow, onClose }: { flow: Flow; onClose: () => void }) {
  switch (flow.kind) {
    case 'wallet':
      return <WalletFlow onClose={onClose} />;
    case 'notes':
      return <NotesFlow onClose={onClose} />;
    case 'accept':
    case 'decline':
    case 'cancelOffer':
      return <OfferDecision kind={flow.kind} offer={flow.offer} token={flow.token} onClose={onClose} />;
    case 'buy':
      return <BuyFlow token={flow.token} onClose={onClose} />;
    case 'offer':
      return <OfferFlow token={flow.token} onClose={onClose} />;
    case 'list':
      return <ListFlow token={flow.token} onClose={onClose} />;
    case 'delist':
      return <DelistFlow token={flow.token} onClose={onClose} />;
    case 'send':
      return <SendFlow token={flow.token} onClose={onClose} />;
    case 'redeem':
      return <RedeemFlow token={flow.token} onClose={onClose} />;
  }
}

/* ---------- pieces ---------- */

function ItemLine({ token }: { token: Token }) {
  const m = token.metadata;
  return (
    <div className="item-line">
      <span className={`item-slot fv-frame ${FRAME[m.type]}`}>
        <img src={sprite(TYPE_ICON[m.type])} alt="" />
      </span>
      <span className="item-line-text">
        <b className="fv-title">{token.title}</b>
        <small>
          {modelLabel(m)} · #{displayNo(token.token_id)}
        </small>
      </span>
    </div>
  );
}

function Rows({ rows }: { rows: [string, ReactNode, string?][] }) {
  return (
    <dl className="sum">
      {rows.map(([k, v, cls]) => (
        <div key={k} className={cls}>
          <dt>{k}</dt>
          <dd>{v}</dd>
        </div>
      ))}
    </dl>
  );
}

const Farm = ({ n }: { n?: number }) => (
  <span className="farm-amt">
    <FarmIcon size={16} /> {n == null ? '…' : fmt(n)} <small>FARM</small>
  </span>
);

const Actions = ({ children }: { children: ReactNode }) => <div className="dlg-actions">{children}</div>;

const Err = ({ error }: { error: string | null }) =>
  error ? (
    <p className="dlg-err" role="alert">
      {error}
    </p>
  ) : null;

function AmountInput({ value, onChange, label, suggestions }: { value: string; onChange: (v: string) => void; label: string; suggestions: [string, number | undefined][] }) {
  return (
    <div className="amount">
      <label className="amount-field">
        <span>{label}</span>
        <span className="amount-box">
          <FarmIcon size={22} />
          <input inputMode="decimal" value={value} onChange={(e) => onChange(e.target.value.replace(',', '.').replace(/[^\d.]/g, ''))} placeholder="0" aria-label={`${label} in FARM`} />
          <small>FARM</small>
        </span>
      </label>
      <div className="amount-sugg">
        {suggestions
          .filter(([, v]) => v != null && v > 0)
          .map(([k, v]) => (
            <button key={k} type="button" onClick={() => onChange(String(v))}>
              {k} · {fmt(v!)}
            </button>
          ))}
      </div>
    </div>
  );
}

/** "Market fee (5%)", from the market contract's fee_bps. */
const feeLabel = (rate: number | undefined) => `Market fee (${rate == null ? '…' : `${+(rate * 100).toFixed(2)}`}%)`;

const lastSale = (t: Token) => t.events.find((e) => e.kind === 'bought')?.price;
/** Floor from the item query already in cache (the item page loaded it). */
const useFloor = (t: Token) => useToken(t.token_id).data?.floor;

/* ---------- flows ---------- */

function BuyFlow({ token, onClose }: { token: Token; onClose: () => void }) {
  const { data: balance, isPending } = useBalance();
  const market = useMarket();
  const { status, error, run } = useRun();
  const price = token.price ?? 0;
  const short = balance == null ? 0 : price - balance;
  return (
    <Dialog open onClose={onClose} title={status === 'done' ? 'It’s yours!' : 'Buy item'} icon="Icon_Shop">
      {status === 'busy' ? (
        <WalletWait />
      ) : status === 'done' ? (
        <Done title={token.title}>
          <p>Find it under My items, and use it in the game whenever you’re ready.</p>
          <Actions>
            <a className="fv-btn green" href={href.mine()} onClick={onClose}>
              My items
            </a>
            <button className="fv-btn gray" onClick={onClose}>
              Done
            </button>
          </Actions>
        </Done>
      ) : (
        <>
          <ItemLine token={token} />
          <Rows rows={[['Price', <Farm n={price} />], ['Your FARM', <Farm n={balance} />], ['After buying', <Farm n={balance == null ? undefined : Math.max(0, balance - price)} />, 'total']]} />
          {short > 0 ? <p className="dlg-err">You need {fmt(short)} more FARM to buy this.</p> : <p className="dlg-note">Purchases are final.</p>}
          <Err error={error} />
          <Actions>
            <button className="fv-btn gray" onClick={onClose}>
              Cancel
            </button>
            <button className="fv-btn green" disabled={isPending || short > 0} onClick={() => run(() => market.buy(token))}>
              Buy for {fmt(price)}
            </button>
          </Actions>
        </>
      )}
    </Dialog>
  );
}

function OfferFlow({ token, onClose }: { token: Token; onClose: () => void }) {
  const { data: balance = 0 } = useBalance();
  const market = useMarket();
  const floor = useFloor(token);
  const [amount, setAmount] = useState('');
  const [days, setDays] = useState(3);
  const { status, error, run } = useRun();
  const n = Number(amount);
  const problem = !amount ? null : !(n > 0) ? 'Enter an amount above 0.' : n > balance ? `You have ${fmt(balance)} FARM.` : null;
  return (
    <Dialog open onClose={onClose} title={status === 'done' ? 'Offer sent' : 'Make an offer'} icon="Icon_Mail">
      {status === 'busy' ? (
        <WalletWait text="Sending your offer…" />
      ) : status === 'done' ? (
        <Done title={`${fmt(n)} FARM for ${token.title}`}>
          <p>
            We’ll let you know if {token.owner_id} accepts. It expires in {days} day{days > 1 ? 's' : ''}.
          </p>
          <Actions>
            <button className="fv-btn green" onClick={onClose}>
              Done
            </button>
          </Actions>
        </Done>
      ) : (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            if (n > 0 && !problem) run(() => market.offer(token, n, days));
          }}
        >
          <ItemLine token={token} />
          <AmountInput
            label="Your offer"
            value={amount}
            onChange={setAmount}
            suggestions={[
              ['Floor', floor],
              ['Listed', token.price],
              ['Last sale', lastSale(token)],
            ]}
          />
          <div className="field-row">
            <span>Expires in</span>
            <Select
              size="sm"
              label="Expires in"
              value={String(days)}
              onChange={(v) => setDays(Number(v))}
              options={[
                { value: '1', label: '1 day' },
                { value: '3', label: '3 days' },
                { value: '7', label: '7 days' },
              ]}
            />
          </div>
          <p className="dlg-note">Your FARM stays in your wallet until the owner accepts.</p>
          {problem && <p className="dlg-err">{problem}</p>}
          <Err error={error} />
          <Actions>
            <button type="button" className="fv-btn gray" onClick={onClose}>
              Cancel
            </button>
            <button type="submit" className="fv-btn blue" disabled={!(n > 0) || !!problem}>
              Send offer
            </button>
          </Actions>
        </form>
      )}
    </Dialog>
  );
}

function ListFlow({ token, onClose }: { token: Token; onClose: () => void }) {
  const editing = token.price != null;
  const market = useMarket();
  const floor = useFloor(token);
  const [amount, setAmount] = useState(editing ? String(token.price) : '');
  const { status, error, run } = useRun();
  const n = Number(amount);
  const rate = useFeeRate();
  const fee = rate == null ? undefined : n > 0 ? n * rate : 0;
  return (
    <Dialog open onClose={onClose} title={status === 'done' ? 'On the market!' : editing ? 'Change price' : 'Sell item'} icon="Icon_Shop">
      {status === 'busy' ? (
        <WalletWait />
      ) : status === 'done' ? (
        <Done title={`${token.title} · ${fmt(n)} FARM`}>
          <p>Other farmers can buy it now. You can change the price or take it down any time.</p>
          <Actions>
            <button className="fv-btn green" onClick={onClose}>
              Done
            </button>
          </Actions>
        </Done>
      ) : (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            if (n > 0) run(() => market.list(token, n));
          }}
        >
          <ItemLine token={token} />
          <AmountInput
            label="Price"
            value={amount}
            onChange={setAmount}
            suggestions={[
              ['Floor', floor],
              ['Last sale', lastSale(token)],
            ]}
          />
          <Rows
            rows={[
              ['Price', <Farm n={n || 0} />],
              [feeLabel(rate), <span className="minus">−{fee == null ? '…' : fmt(fee)} FARM</span>],
              ['You get', <Farm n={fee == null ? undefined : Math.max(0, (n || 0) - fee)} />, 'total'],
            ]}
          />
          {!editing && <p className="dlg-note">The first time you sell, your wallet also asks for a small NEAR deposit to hold the listing.</p>}
          <Err error={error} />
          <Actions>
            <button type="button" className="fv-btn gray" onClick={onClose}>
              Cancel
            </button>
            <button type="submit" className="fv-btn green" disabled={!(n > 0)}>
              {editing ? 'Update price' : 'Put up for sale'}
            </button>
          </Actions>
        </form>
      )}
    </Dialog>
  );
}

/** Yes/no dialog with the pending and done screens. */
function Confirm(props: {
  title: string;
  icon: string;
  token?: Token;
  body: ReactNode;
  confirm: string;
  tone: 'green' | 'yellow' | 'blue' | 'gray';
  doneTitle: string;
  doneBody?: ReactNode;
  action: () => Promise<void>;
  onClose: () => void;
  onDone?: () => void;
}) {
  const { status, error, run } = useRun();
  return (
    <Dialog open onClose={props.onClose} title={status === 'done' ? props.doneTitle : props.title} icon={props.icon}>
      {status === 'busy' ? (
        <WalletWait />
      ) : status === 'done' ? (
        <Done title={props.token?.title}>
          {props.doneBody}
          <Actions>
            <button
              className="fv-btn green"
              onClick={() => {
                props.onClose();
                props.onDone?.();
              }}
            >
              Done
            </button>
          </Actions>
        </Done>
      ) : (
        <>
          {props.token && <ItemLine token={props.token} />}
          {props.body}
          <Err error={error} />
          <Actions>
            <button className="fv-btn gray" onClick={props.onClose}>
              Cancel
            </button>
            <button className={`fv-btn ${props.tone}`} onClick={() => run(props.action)}>
              {props.confirm}
            </button>
          </Actions>
        </>
      )}
    </Dialog>
  );
}

function DelistFlow({ token, onClose }: { token: Token; onClose: () => void }) {
  const market = useMarket();
  return (
    <Confirm
      title="Take it down?"
      icon="Icon_PictoIcon_Delete"
      token={token}
      body={<p className="dlg-note">It stays in your wallet. You can put it up for sale again any time.</p>}
      confirm="Take it down"
      tone="yellow"
      doneTitle="Taken off the market"
      action={() => market.delist(token)}
      onClose={onClose}
    />
  );
}

function RedeemFlow({ token, onClose }: { token: Token; onClose: () => void }) {
  const market = useMarket();
  return (
    <Confirm
      title="Use in game?"
      icon="Paint_fac_farmhouse_l1"
      token={token}
      body={
        <>
          <p className="dlg-note">{token.title} will be waiting on your island the next time you play.</p>
          <p className="dlg-warn">
            <img src={sprite('Icon_ImageIcon_Info')} alt="" />
            This uses the item up — it can’t be sold or used again afterwards.
          </p>
        </>
      }
      confirm="Use it"
      tone="yellow"
      doneTitle="On its way!"
      doneBody={<p>Open the game to find {token.title} on your island.</p>}
      action={() => market.redeem(token)}
      onClose={onClose}
      onDone={() => (location.hash = href.mine())}
    />
  );
}

function SendFlow({ token, onClose }: { token: Token; onClose: () => void }) {
  const market = useMarket();
  const { accountId } = useWallet();
  const [to, setTo] = useState('');
  const { status, error, run } = useRun();
  const v = to.trim().toLowerCase();
  const bad = !v ? null : !isAccount(v) ? 'That doesn’t look like a NEAR account, e.g. friend.near' : v === accountId ? 'That’s your own account.' : null;
  return (
    <Dialog open onClose={onClose} title={status === 'done' ? 'Sent!' : 'Send item'} icon="Icon_Gift">
      {status === 'busy' ? (
        <WalletWait />
      ) : status === 'done' ? (
        <Done title={`${token.title} → ${v}`}>
          <p>It’s in their wallet now.</p>
          <Actions>
            <button className="fv-btn green" onClick={onClose}>
              Done
            </button>
          </Actions>
        </Done>
      ) : (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            if (v && !bad) run(() => market.send(token, v));
          }}
        >
          <ItemLine token={token} />
          <label className="text-field">
            <span>Send to</span>
            <input value={to} onChange={(e) => setTo(e.target.value)} placeholder="friend.near" autoCapitalize="none" spellCheck={false} />
          </label>
          {bad ? <p className="dlg-err">{bad}</p> : <p className="dlg-note">Double-check the name — sending can’t be undone.</p>}
          <Err error={error} />
          <Actions>
            <button type="button" className="fv-btn gray" onClick={onClose}>
              Cancel
            </button>
            <button type="submit" className="fv-btn blue" disabled={!v || !!bad}>
              Send
            </button>
          </Actions>
        </form>
      )}
    </Dialog>
  );
}

function OfferDecision({ kind, offer: o, token: t, onClose }: { kind: 'accept' | 'decline' | 'cancelOffer'; offer: Offer; token: Token; onClose: () => void }) {
  const market = useMarket();
  const rate = useFeeRate();
  if (kind === 'accept') {
    const fee = rate == null ? undefined : o.amount * rate;
    return (
      <Confirm
        title="Accept offer?"
        icon="Icon_Golds"
        token={t}
        body={
          <Rows
            rows={[
              ['Offer from', o.from],
              ['Offer', <Farm n={o.amount} />],
              [feeLabel(rate), <span className="minus">−{fee == null ? '…' : fmt(fee)} FARM</span>],
              ['You get', <Farm n={fee == null ? undefined : o.amount - fee} />, 'total'],
            ]}
          />
        }
        confirm={`Sell for ${fmt(o.amount)}`}
        tone="green"
        doneTitle="Sold!"
        doneBody={<p>{t.title} now belongs to {o.from}. Your FARM is in your wallet.</p>}
        action={() => market.acceptOffer(o)}
        onClose={onClose}
      />
    );
  }
  const mine = kind === 'cancelOffer';
  return (
    <Confirm
      title={mine ? 'Cancel your offer?' : 'Decline offer?'}
      icon="Icon_Mail"
      token={t}
      body={<p className="dlg-note">{mine ? `Your ${fmt(o.amount)} FARM offer will be withdrawn.` : `${o.from} offered ${fmt(o.amount)} FARM. They’ll be told it was declined.`}</p>}
      confirm={mine ? 'Cancel offer' : 'Decline'}
      tone="yellow"
      doneTitle={mine ? 'Offer cancelled' : 'Offer declined'}
      action={() => (mine ? market.cancelOffer(o) : market.declineOffer(o))}
      onClose={onClose}
    />
  );
}

function WalletFlow({ onClose }: { onClose: () => void }) {
  const wallet = useWallet();
  const { data: balance } = useBalance();
  const { status, error, run } = useRun();
  if (!wallet.accountId) {
    return (
      <Dialog open onClose={onClose} title="Connect wallet" icon="Icon_PictoIcon_User">
        {status === 'busy' ? (
          <WalletWait text="Approve the sign-in in your wallet…" />
        ) : (
          <>
            <p className="dlg-note">Sign in with your NEAR wallet to buy and sell. It’s the same wallet you play with.</p>
            <Err error={error} />
            <Actions>
              <button className="fv-btn green" onClick={() => run(async () => void (await wallet.connect()))}>
                Connect wallet
              </button>
            </Actions>
          </>
        )}
      </Dialog>
    );
  }
  const account = wallet.accountId;
  return (
    <Dialog open onClose={onClose} title="Your wallet" icon="ResourceBar_Icon_Gold">
      <div className="wallet-me">
        <AccountAvatar account={account} size={56} />
        <b className="fv-title">{account}</b>
      </div>
      <div className="wallet-bal fv-row">
        <FarmIcon size={34} />
        <strong className="fv-title fv-gold">{balance == null ? '…' : fmt(balance)}</strong>
        <span>FARM</span>
      </div>
      <Actions>
        <button
          className="fv-btn blue"
          onClick={() =>
            navigator.clipboard?.writeText(account).then(
              () => toast('Account name copied', 'Icon_PictoIcon_Edit'),
              () => toast('Couldn’t copy — select it instead', 'Icon_ImageIcon_Info'),
            )
          }
        >
          Copy name
        </button>
        <button
          className="fv-btn gray"
          onClick={async () => {
            onClose();
            await wallet.disconnect();
            toast('Signed out', 'Icon_PictoIcon_User');
          }}
        >
          Sign out
        </button>
      </Actions>
    </Dialog>
  );
}

function NotesFlow({ onClose }: { onClose: () => void }) {
  const { accountId } = useWallet();
  const notesQ = useNotes();
  const markRead = useMarkNotesRead();
  // Keep what was unread when the panel opened, so the gold markers don't vanish at once.
  const [snapshot, setSnapshot] = useState<typeof notesQ.data>(undefined);
  useEffect(() => {
    if (notesQ.data && !snapshot) {
      setSnapshot(notesQ.data);
      if (notesQ.data.some((n) => !n.read)) markRead.mutate();
    }
  }, [notesQ.data, snapshot, markRead]);
  const list = snapshot ?? notesQ.data;
  return (
    <Dialog open onClose={onClose} title="Notifications" icon="Icon_PictoIcon_Bell">
      {!accountId ? (
        <p className="dlg-note">Connect your wallet to hear when your items sell.</p>
      ) : notesQ.isPending ? (
        <WalletWait text="Loading…" />
      ) : notesQ.error ? (
        <p className="dlg-err">{(notesQ.error as Error).message}</p>
      ) : !list?.length ? (
        <p className="dlg-note">Nothing yet. Sales and items sent to you show up here.</p>
      ) : (
        <ul className="notes">
          {list.map((n) => (
            <li key={n.id} className={n.read ? '' : 'unread'}>
              <a href={n.href ?? '#'} onClick={onClose} className="fv-row">
                <img src={sprite(n.icon)} alt="" />
                <span>
                  {n.text}
                  <small>{timeAgo(n.at)}</small>
                </span>
              </a>
            </li>
          ))}
        </ul>
      )}
    </Dialog>
  );
}
