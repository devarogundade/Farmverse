import { sprite } from '@farmverse/shared/gameTheme';
import { useState } from 'react';
import { useDeleteNews, useNews, usePublishNews, type NewsItem } from '../api/admin';
import { Dialog, toast } from '../components/Dialog';
import { dateTime, Empty, Failed, Field, Input, Loading, PageHead, Pill, ReasonDialog, ReasonField, Toggle } from '../components/ui';

export function News() {
  const { data, error, isLoading, refetch } = useNews();
  const [writing, setWriting] = useState(false);
  const [removing, setRemoving] = useState<NewsItem | null>(null);
  const remove = useDeleteNews();

  return (
    <>
      <PageHead
        icon="Icon_Megaphone"
        title="News"
        actions={
          <button className="fv-btn yellow" onClick={() => setWriting(true)}>
            <img src={sprite('Icon_PictoIcon_Add01')} alt="" />
            Post news
          </button>
        }
      >
        What players see in the game’s News.
      </PageHead>
      {isLoading ? (
        <Loading />
      ) : error ? (
        <Failed error={error} retry={() => void refetch()} />
      ) : !data?.items.length ? (
        <Empty title="No news yet" art="Icon_Megaphone">
          Post the first item and every player sees it in News.
        </Empty>
      ) : (
        <ul className="news">
          {data.items.map((n) => (
            <li key={n.id} className="news-item fv-panel">
              <img className="news-art" src={sprite(n.art || 'Icon_Megaphone')} alt="" onError={(e) => (e.currentTarget.src = sprite('Icon_Megaphone'))} />
              <div className="news-text">
                <h3 className="fv-title">{n.title}</h3>
                <p>{n.body}</p>
                <small className="muted">
                  {dateTime(n.publishedAt)}
                  {n.url && (
                    <>
                      {' · '}
                      <a className="link" href={n.url} target="_blank" rel="noreferrer">
                        Read more link
                      </a>
                    </>
                  )}
                </small>
              </div>
              <div className="news-side">
                {n.push && <Pill tone="sky">Sent as a notification</Pill>}
                <button className="fv-chip sm-chip" aria-label={`Remove ${n.title}`} onClick={() => setRemoving(n)}>
                  <img className="fv-icon" src={sprite('Icon_PictoIcon_Delete')} alt="" />
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
      <Compose open={writing} onClose={() => setWriting(false)} />
      <ReasonDialog
        open={removing !== null}
        onClose={() => setRemoving(null)}
        title="Remove this news?"
        icon="Icon_PictoIcon_Delete"
        confirm="Remove"
        tone="purple"
        done="News removed"
        onConfirm={(reason) => remove.mutateAsync({ id: removing!.id, reason })}
      >
        <p className="note">“{removing?.title}” disappears from the game. Notifications already sent can’t be taken back.</p>
      </ReasonDialog>
    </>
  );
}

function Compose({ open, onClose }: { open: boolean; onClose: () => void }) {
  const publish = usePublishNews();
  const [title, setTitle] = useState('');
  const [body, setBody] = useState('');
  const [url, setUrl] = useState('');
  const [push, setPush] = useState(false);
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const reset = () => {
    setTitle('');
    setBody('');
    setUrl('');
    setPush(false);
    setReason('');
    setError(null);
  };
  return (
    <Dialog open={open} onClose={() => !publish.isPending && (reset(), onClose())} title="Post news" icon="Icon_Megaphone">
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          setError(null);
          try {
            await publish.mutateAsync({ title: title.trim(), body: body.trim(), push, reason: reason.trim(), ...(url.trim() ? { url: url.trim() } : {}) });
            toast('News posted', 'Icon_Megaphone');
            reset();
            onClose();
          } catch (err) {
            setError((err as Error).message);
          }
        }}
      >
        <Field label="Title">
          <Input value={title} maxLength={60} onChange={(e) => setTitle(e.target.value)} required placeholder="Harvest festival this weekend" />
        </Field>
        <Field label="Text" hint={`${body.length}/600`}>
          <textarea className="input" rows={5} maxLength={600} value={body} onChange={(e) => setBody(e.target.value)} required />
        </Field>
        <Field label="Read more link (optional)">
          <Input type="url" value={url} onChange={(e) => setUrl(e.target.value)} placeholder="https://farmverse.world/…" />
        </Field>
        <div className="field-row">
          <span>
            <b>Also send a notification</b>
            <br />
            <small className="muted">To players from the last 30 days who allow news, at a good hour for each.</small>
          </span>
          <Toggle on={push} onChange={setPush} label="Also send a notification" />
        </div>
        <ReasonField value={reason} onChange={setReason} />
        {error && <p className="form-err">{error}</p>}
        <div className="dlg-actions">
          <button type="button" className="fv-btn gray" onClick={() => (reset(), onClose())}>
            Cancel
          </button>
          <button type="submit" className="fv-btn yellow" disabled={publish.isPending || !title.trim() || !body.trim() || reason.trim().length < 3}>
            {publish.isPending ? 'Posting…' : 'Post'}
          </button>
        </div>
      </form>
    </Dialog>
  );
}
