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
  const [mintHours, setMintHours] = useState('');
  const [mintCap, setMintCap] = useState('');
  const [confirming, setConfirming] = useState(false);

  useEffect(() => {
    if (!data) return;
    setMaintenance(data.maintenance);
    setMessage(data.message);
    setVersion(data.stored?.minClientVersion ?? '');
    setMintHours(storedText(data.stored?.mintMinAccountHours));
    setMintCap(storedText(data.stored?.mintDailyValueCap));
  }, [data]);

  if (isLoading) return <Loading />;
  if (error || !data) return <Failed error={error} retry={() => void refetch()} />;

  const versionOk = version === '' || /^\d{1,4}\.\d{1,4}\.\d{1,6}$/.test(version);
  const hoursOk = mintHours === '' || (/^\d+(\.\d+)?$/.test(mintHours) && Number(mintHours) <= 24 * 365);
  const capOk = mintCap === '' || /^\d{1,9}$/.test(mintCap);
  const mintDirty = mintHours !== storedText(data.stored?.mintMinAccountHours) || mintCap !== storedText(data.stored?.mintDailyValueCap);
  const dirty = maintenance !== data.maintenance || message !== data.message || version !== (data.stored?.minClientVersion ?? '') || mintDirty;
  const undo = () => {
    setMaintenance(data.maintenance);
    setMessage(data.message);
    setVersion(data.stored?.minClientVersion ?? '');
    setMintHours(storedText(data.stored?.mintMinAccountHours));
    setMintCap(storedText(data.stored?.mintDailyValueCap));
  };

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
        <Field
          label="Hours before a new farm can mint"
          hint={`Empty uses the server default (${data.mintDefaults.minAccountHours}). Now: ${data.mintMinAccountHours}. 0 lets new farms mint at once.`}
        >
          <Input value={mintHours} inputMode="decimal" onChange={(e) => setMintHours(e.target.value.trim())} placeholder={String(data.mintDefaults.minAccountHours)} aria-invalid={!hoursOk} />
        </Field>
        {!hoursOk && <p className="form-err">Use a number of hours, up to a year.</p>}
        <Field
          label="Mint value per player per day (coins)"
          hint={`The market value a player can mint each UTC day. Empty uses the server default (${data.mintDefaults.dailyValueCap}). Now: ${data.mintDailyValueCap}.`}
        >
          <Input value={mintCap} inputMode="numeric" onChange={(e) => setMintCap(e.target.value.trim())} placeholder={String(data.mintDefaults.dailyValueCap)} aria-invalid={!capOk} />
        </Field>
        {!capOk && <p className="form-err">Use a whole number of coins.</p>}
        <div className="dlg-actions">
          <button className="fv-btn gray" disabled={!dirty} onClick={undo}>
            Undo
          </button>
          <button className={`fv-btn ${maintenance && !data.maintenance ? 'purple' : 'yellow'}`} disabled={!dirty || !versionOk || !hoursOk || !capOk} onClick={() => setConfirming(true)}>
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
        onConfirm={(reason) =>
          save.mutateAsync({
            maintenance,
            message: message.trim(),
            minClientVersion: version,
            mintMinAccountHours: mintHours === '' ? null : Number(mintHours),
            mintDailyValueCap: mintCap === '' ? null : Number(mintCap),
            reason,
          })
        }
      >
        <p className="note">Takes effect for every player within 10 seconds.</p>
      </ReasonDialog>
    </>
  );
}

/** A stored override as the field shows it ('' when the server default applies). */
function storedText(value: number | null | undefined): string {
  return value === null || value === undefined ? '' : String(value);
}
