import { sprite } from '@farmverse/shared/gameTheme';
import { useEffect, useState } from 'react';
import { useOps, useSaveOps } from '../api/admin';
import { Failed, Field, Input, Loading, PageHead, ReasonDialog, timeAgo, Toggle } from '../components/ui';

export function Settings() {
  const { data, error, isLoading, refetch } = useOps();
  const save = useSaveOps();
  const [maintenance, setMaintenance] = useState(false);
  const [message, setMessage] = useState('');
  const [version, setVersion] = useState('');
  const [confirming, setConfirming] = useState(false);

  useEffect(() => {
    if (!data) return;
    setMaintenance(data.maintenance);
    setMessage(data.message);
    setVersion(data.stored?.minClientVersion ?? '');
  }, [data]);

  if (isLoading) return <Loading />;
  if (error || !data) return <Failed error={error} retry={() => void refetch()} />;

  const versionOk = version === '' || /^\d{1,4}\.\d{1,4}\.\d{1,6}$/.test(version);
  const dirty = maintenance !== data.maintenance || message !== data.message || version !== (data.stored?.minClientVersion ?? '');

  return (
    <>
      <PageHead icon="Icon_Hammer" title="Game">
        Switches that change the live game straight away, for every player.
      </PageHead>

      <div className={`status-banner ${data.maintenance ? 'down' : 'up'}`}>
        <img src={sprite(data.maintenance ? 'Icon_Hammer' : 'Icon_Heart')} alt="" />
        <div>
          <b className="fv-title">{data.maintenance ? 'The game is closed for maintenance' : 'The game is open'}</b>
          {data.stored && (
            <small>
              Last changed by {data.stored.updatedBy} {timeAgo(data.stored.updatedAt)}
            </small>
          )}
        </div>
      </div>

      <section className="card fv-panel settings">
        <div className="field-row">
          <span>
            <b>Maintenance</b>
            <br />
            <small className="muted">Players see the maintenance screen and can’t play. Admin wallets still can.</small>
          </span>
          <Toggle on={maintenance} onChange={setMaintenance} label="Maintenance" />
        </div>
        <Field label="Message during maintenance" hint="Leave empty to use the game’s own words.">
          <Input value={message} maxLength={200} onChange={(e) => setMessage(e.target.value)} placeholder="We’re back at 15:00 UTC." />
        </Field>
        <Field label="Oldest game version allowed" hint={`Older games are asked to update. Empty uses the server default (${data.fallbackMinClientVersion}). Now: ${data.minClientVersion}.`}>
          <Input value={version} onChange={(e) => setVersion(e.target.value.trim())} placeholder={data.fallbackMinClientVersion} aria-invalid={!versionOk} />
        </Field>
        {!versionOk && <p className="form-err">Use a version like 1.4.0.</p>}
        <div className="dlg-actions">
          <button className="fv-btn gray" disabled={!dirty} onClick={() => (setMaintenance(data.maintenance), setMessage(data.message), setVersion(data.stored?.minClientVersion ?? ''))}>
            Undo
          </button>
          <button className={`fv-btn ${maintenance && !data.maintenance ? 'purple' : 'yellow'}`} disabled={!dirty || !versionOk} onClick={() => setConfirming(true)}>
            Save
          </button>
        </div>
      </section>

      <ReasonDialog
        open={confirming}
        onClose={() => setConfirming(false)}
        title={maintenance && !data.maintenance ? 'Close the game for maintenance?' : !maintenance && data.maintenance ? 'Open the game again?' : 'Save game settings?'}
        icon="Icon_Hammer"
        confirm="Save"
        tone={maintenance && !data.maintenance ? 'purple' : 'yellow'}
        done="Game settings saved"
        onConfirm={(reason) => save.mutateAsync({ maintenance, message: message.trim(), minClientVersion: version, reason })}
      >
        <p className="note">Takes effect for every player within 10 seconds.</p>
      </ReasonDialog>
    </>
  );
}
