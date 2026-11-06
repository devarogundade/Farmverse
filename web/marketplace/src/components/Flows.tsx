import { sprite } from '@farmverse/shared/gameTheme';
import { displayNo, flatArt, modelLabel } from '@farmverse/shared/nft/metadata';
import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import { useMarket } from '../actions/useMarket';
import { useBalance, useFeeRate, useMarkNotesRead, useNotes, useOwed, useToken } from '../api/queries';
import type { Drop, Offer, Rental, Token } from '../api/types';
import { useWallet } from '../chain/wallet';
import { config, gameLink } from '../config';
import { href } from '../router';
import { isAccount } from '../lib/account';
import { Dialog, Done, WalletWait, toast, useRun } from './Dialog';
import { Select } from './Select';
import { AccountAvatar, FarmIcon, TYPE_ICON, fmt, frameOf, timeAgo } from './ui';

export type Flow =
  | { kind: 'buy' | 'offer' | 'list' | 'delist' | 'send' | 'redeem' | 'rentOut'; token: Token }
  | { kind: 'accept' | 'cancelOffer'; offer: Offer; token: Token }
  | { kind: 'rent' | 'rentManage'; rental: Rental }
  | { kind: 'drop'; drop: Drop }
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

const flowKey = (f: Flow) =>
  f.kind + ('token' in f ? f.token.token_id : '') + ('offer' in f ? f.offer.id : '') + ('rental' in f ? f.rental.token.token_id : '') + ('drop' in f ? f.drop.id : '');

function FlowDialog({ flow, onClose }: { flow: Flow; onClose: () => void }) {
  switch (flow.kind) {
    case 'wallet':
      return <WalletFlow onClose={onClose} />;
    case 'notes':
      return <NotesFlow onClose={onClose} />;
    case 'accept':
    case 'cancelOffer':
      return <OfferDecision kind={flow.kind} offer={flow.offer} token={flow.token} onClose={onClose} />;
    case 'drop':
      return <DropFlow drop={flow.drop} onClose={onClose} />;
    case 'rentOut':
      return <RentOutFlow token={flow.token} onClose={onClose} />;
    case 'rent':
      return <RentFlow rental={flow.rental} onClose={onClose} />;
    case 'rentManage':
      return <RentManageFlow rental={flow.rental} onClose={onClose} />;
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
      <span className={`item-slot fv-frame ${frameOf(m)}`}>
        <img src={sprite(flatArt(m) ?? TYPE_ICON[m.type])} alt="" />
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
          <p className="dlg-note">The market holds your FARM until the owner accepts or the offer ends. Cancel any time to get it back. The first time, your wallet also asks for a small NEAR deposit.</p>
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
          <p className="dlg-note">Your wallet asks twice: once to use the item, once to sign in to the game (free).</p>
          <p className="dlg-warn">
            <img src={sprite('Icon_ImageIcon_Info')} alt="" />
            This uses the item up — it can’t be sold or used again afterwards.
          </p>
        </>
      }
      confirm="Use it"
      tone="yellow"
      doneTitle="On its way!"
      doneBody={
        <>
          <p>{token.title} is waiting on your island.</p>
          <Actions>
            <a className="fv-btn yellow" href={gameLink.redeemed(token.token_id)} target="_blank" rel="noreferrer">
              Open the game
            </a>
          </Actions>
        </>
      }
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

function OfferDecision({ kind, offer: o, token: t, onClose }: { kind: 'accept' | 'cancelOffer'; offer: Offer; token: Token; onClose: () => void }) {
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
        action={() => market.acceptOffer(o, t)}
        onClose={onClose}
      />
    );
  }
  return (
    <Confirm
      title="Cancel your offer?"
      icon="Icon_Mail"
      token={t}
      body={<p className="dlg-note">Your {fmt(o.amount)} FARM comes back to your wallet.</p>}
      confirm="Cancel offer"
      tone="yellow"
      doneTitle="Offer cancelled"
      action={() => market.cancelOffer(o)}
      onClose={onClose}
    />
  );
}

function WalletFlow({ onClose }: { onClose: () => void }) {
  const wallet = useWallet();
  const market = useMarket();
  const { data: balance } = useBalance();
  const { data: owed } = useOwed();
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
      {config.network === 'testnet' && <FaucetRow />}
      {owed != null && owed > 0 && (
        <div className="wallet-owed fv-row">
          <span>
            <b>{fmt(owed)} FARM</b> from your sales is waiting for you.
          </span>
          <button className="fv-btn green sm" disabled={status === 'busy'} onClick={() => run(() => market.collect())}>
            {status === 'busy' ? 'Collecting…' : 'Collect'}
          </button>
        </div>
      )}
      <Err error={error} />
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

/* ---------- v2: shop, rentals, faucet (plan 15 §5, §6.6, §12.1) ---------- */

function DropFlow({ drop: d, onClose }: { drop: Drop; onClose: () => void }) {
  const { data: balance, isPending } = useBalance();
  const market = useMarket();
  const { status, error, run } = useRun();
  const short = balance == null ? 0 : d.price - balance;
  const left = d.cap != null ? d.cap - d.sold : undefined;
  return (
    <Dialog open onClose={onClose} title={status === 'done' ? 'It’s yours!' : 'Buy from the shop'} icon="Icon_Shop">
      {status === 'busy' ? (
        <WalletWait />
      ) : status === 'done' ? (
        <Done title={d.title}>
          <p>Find it under My items. Use it in the game whenever you like, or sell it here.</p>
          <Actions>
            <a className="fv-btn green" href={href.mine()} onClick={onClose}>
              My items
            </a>
            <a className="fv-btn yellow" href={gameLink.nfts()} target="_blank" rel="noreferrer">
              Use in game
            </a>
          </Actions>
        </Done>
      ) : (
        <>
          <div className="item-line">
            <span className="item-slot fv-frame blue">
              <img src={sprite(flatArt({ type: d.kind === 'land' ? 'deed' : 'pack', item_id: d.item_id }) ?? 'Icon_Gift')} alt="" />
            </span>
            <span className="item-line-text">
              <b className="fv-title">{d.title}</b>
              <small>{left != null ? `${fmt(left)} of ${fmt(d.cap!)} left` : 'From the Farmverse shop'}</small>
            </span>
          </div>
          <Rows rows={[['Price', <Farm n={d.price} />], ['Your FARM', <Farm n={balance} />], ['After buying', <Farm n={balance == null ? undefined : Math.max(0, balance - d.price)} />, 'total']]} />
          {short > 0 ? <p className="dlg-err">You need {fmt(short)} more FARM.</p> : <p className="dlg-note">If it sells out before your wallet confirms, your FARM comes straight back.</p>}
          <Err error={error} />
          <Actions>
            <button className="fv-btn gray" onClick={onClose}>
              Cancel
            </button>
            <button className="fv-btn green" disabled={isPending || short > 0} onClick={() => run(() => market.buyDrop(d))}>
              Buy for {fmt(d.price)}
            </button>
          </Actions>
        </>
      )}
    </Dialog>
  );
}

const DAY_OPTIONS = [1, 3, 7, 14, 30];

function RentOutFlow({ token, onClose }: { token: Token; onClose: () => void }) {
  const market = useMarket();
  const [amount, setAmount] = useState('');
  const [maxDays, setMaxDays] = useState(7);
  const { status, error, run } = useRun();
  const rate = useFeeRate();
  const n = Number(amount);
  return (
    <Dialog open onClose={onClose} title={status === 'done' ? 'Ready for hire!' : 'Rent out'} icon="Icon_PictoIcon_User">
      {status === 'busy' ? (
        <WalletWait />
      ) : status === 'done' ? (
        <Done title={token.title}>
          <p>Until someone hires {token.title}, they keep working on your farm. What they earn waits for you to collect.</p>
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
            if (n > 0) run(() => market.rentOut(token, n, maxDays));
          }}
        >
          <ItemLine token={token} />
          <AmountInput label="Price per day" value={amount} onChange={setAmount} suggestions={[]} />
          <div className="field-row">
            <span>Longest hire</span>
            <Select
              size="sm"
              label="Longest hire"
              value={String(maxDays)}
              onChange={(v) => setMaxDays(Number(v))}
              options={DAY_OPTIONS.map((d) => ({ value: String(d), label: `${d} day${d > 1 ? 's' : ''}` }))}
            />
          </div>
          <Rows
            rows={[
              ['A day’s hire', <Farm n={n || 0} />],
              [feeLabel(rate), <span className="minus">−{rate == null ? '…' : fmt((n || 0) * rate)} FARM</span>],
              ['You earn a day', <Farm n={rate == null ? undefined : Math.max(0, (n || 0) * (1 - rate))} />, 'total'],
            ]}
          />
          <p className="dlg-note">The market looks after {token.title} while they’re up for hire. You can take them back whenever nobody is renting them.</p>
          <Err error={error} />
          <Actions>
            <button type="button" className="fv-btn gray" onClick={onClose}>
              Cancel
            </button>
            <button type="submit" className="fv-btn green" disabled={!(n > 0)}>
              Rent out
            </button>
          </Actions>
        </form>
      )}
    </Dialog>
  );
}

function RentFlow({ rental: r, onClose }: { rental: Rental; onClose: () => void }) {
  const { data: balance, isPending } = useBalance();
  const market = useMarket();
  const [days, setDays] = useState(Math.min(3, r.max_days));
  const { status, error, run } = useRun();
  const total = r.price_per_day * days;
  const short = balance == null ? 0 : total - balance;
  return (
    <Dialog open onClose={onClose} title={status === 'done' ? 'Hired!' : 'Hire a worker'} icon="Icon_PictoIcon_User">
      {status === 'busy' ? (
        <WalletWait />
      ) : status === 'done' ? (
        <Done title={r.token.title}>
          <p>
            {r.token.title} works on your farm for {days} day{days > 1 ? 's' : ''}. Open the game to see them at work.
          </p>
          <Actions>
            <a className="fv-btn yellow" href={gameLink.nfts()} target="_blank" rel="noreferrer">
              Open the game
            </a>
            <button className="fv-btn green" onClick={onClose}>
              Done
            </button>
          </Actions>
        </Done>
      ) : (
        <>
          <ItemLine token={r.token} />
          <div className="field-row">
            <span>How long</span>
            <Select
              size="sm"
              label="How long"
              value={String(days)}
              onChange={(v) => setDays(Number(v))}
              options={DAY_OPTIONS.filter((d) => d <= r.max_days).map((d) => ({ value: String(d), label: `${d} day${d > 1 ? 's' : ''}` }))}
            />
          </div>
          <Rows rows={[['A day', <Farm n={r.price_per_day} />], ['Your FARM', <Farm n={balance} />], ['Total', <Farm n={total} />, 'total']]} />
          {short > 0 ? <p className="dlg-err">You need {fmt(short)} more FARM.</p> : <p className="dlg-note">They need a free bed in a workers’ house on your farm.</p>}
          <Err error={error} />
          <Actions>
            <button className="fv-btn gray" onClick={onClose}>
              Cancel
            </button>
            <button className="fv-btn green" disabled={isPending || short > 0} onClick={() => run(() => market.rent(r, days))}>
              Hire for {fmt(total)}
            </button>
          </Actions>
        </>
      )}
    </Dialog>
  );
}

/** The lender's side: collect what the worker earned, or take them back. */
function RentManageFlow({ rental: r, onClose }: { rental: Rental; onClose: () => void }) {
  const market = useMarket();
  const { status, error, run } = useRun();
  const busy = r.rented_until > Date.now();
  return (
    <Dialog open onClose={onClose} title={status === 'done' ? 'Done' : 'Your worker for hire'} icon="Icon_PictoIcon_User">
      {status === 'busy' ? (
        <WalletWait />
      ) : status === 'done' ? (
        <Done title={r.token.title}>
          <Actions>
            <button className="fv-btn green" onClick={onClose}>
              Done
            </button>
          </Actions>
        </Done>
      ) : (
        <>
          <ItemLine token={r.token} />
          <Rows
            rows={[
              ['A day', <Farm n={r.price_per_day} />],
              ['Hired by', busy ? `${r.renter_id} · ${Math.ceil((r.rented_until - Date.now()) / 86_400_000)}d left` : 'Nobody right now'],
              ['Earned', <Farm n={r.earned} />, 'total'],
            ]}
          />
          <Err error={error} />
          <Actions>
            <button className="fv-btn green" disabled={!(r.earned > 0)} onClick={() => run(() => market.rentWithdraw(r))}>
              Collect {fmt(r.earned)} FARM
            </button>
            <button className="fv-btn yellow" disabled={busy} title={busy ? 'You can take them back when the hire ends' : undefined} onClick={() => run(() => market.rentEnd(r))}>
              Take back
            </button>
          </Actions>
        </>
      )}
    </Dialog>
  );
}

/** Testnet: today's free FARM, in the wallet panel. */
function FaucetRow() {
  const market = useMarket();
  const [state, setState] = useState<{ busy?: boolean; text?: string; error?: string }>({});
  return (
    <div className="wallet-owed fv-row faucet-row">
      <span>{state.error ? <span className="dlg-err">{state.error}</span> : state.text ? <b>{state.text}</b> : 'Free test FARM to try things out, once a day.'}</span>
      <button
        className="fv-btn yellow sm"
        disabled={state.busy}
        onClick={async () => {
          setState({ busy: true });
          try {
            const got = await market.faucet();
            const text = `${got.farm} FARM sent${got.near !== '0' ? ` (and ${got.near} NEAR)` : ''}`;
            setState({ text });
            toast(text, 'ResourceBar_Icon_Gold');
          } catch (e) {
            setState({ error: (e as Error).message });
          }
        }}
      >
        {state.busy ? 'Sending…' : 'Get FARM'}
      </button>
      <a className="muted" href="#/buy-farm">
        Or pay with any token
      </a>
    </div>
  );
}
