import React, { useState } from 'react';
import { useSetAdKillSwitchMutation } from '../../../../api/hooks/useMarketingAdsSettings';
import { describeAdsError } from '../../../../api/hooks/marketingAds/errors';
import { AD_PLATFORMS, type AdKillSwitchState } from '../../../../api/hooks/marketingAds/types';
import { PLATFORM_LABELS, labelFor } from '../shared/adLabels';
import { GREEN_PILL, Pill, RED_PILL } from '../shared/Badges';
import ConfirmActionDialog from '../shared/ConfirmActionDialog';

interface PendingToggle {
  platform: string | null;
  enable: boolean;
}

const ALL_PLATFORMS_LABEL = 'Všechny platformy';
const TOGGLE_BUTTON =
  'px-3 py-1 text-sm font-medium border border-gray-300 dark:border-graphite-border rounded-md text-gray-700 dark:text-graphite-muted bg-white dark:bg-graphite-surface hover:bg-gray-50 dark:hover:bg-white/5 disabled:opacity-50';

interface KillSwitchRowProps {
  label: string;
  isEnabled: boolean;
  note: string | null;
  isDisabled: boolean;
  onToggle: () => void;
}

const KillSwitchRow: React.FC<KillSwitchRowProps> = ({ label, isEnabled, note, isDisabled, onToggle }) => {
  const verb = isEnabled ? 'Vypnout' : 'Zapnout';
  return (
    <li className="flex flex-wrap items-center justify-between gap-2 py-2">
      <div className="flex items-center gap-2">
        <span className="text-sm text-gray-900 dark:text-graphite-text">{label}</span>
        <Pill className={isEnabled ? GREEN_PILL : RED_PILL}>{isEnabled ? 'Zapnuto' : 'Vypnuto'}</Pill>
        {note && <span className="text-xs text-gray-500 dark:text-graphite-muted">{note}</span>}
      </div>
      <button type="button" className={TOGGLE_BUTTON} disabled={isDisabled} onClick={onToggle} aria-label={`${verb} provádění: ${label}`}>
        {verb}
      </button>
    </li>
  );
};

const KillSwitchCard: React.FC<{ state: AdKillSwitchState }> = ({ state }) => {
  const mutation = useSetAdKillSwitchMutation();
  const [pending, setPending] = useState<PendingToggle | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const overrideFor = (platform: string) => state.platformOverrides.find((o) => o.platform === platform) ?? null;
  const scope = pending?.platform ? ` pro ${labelFor(PLATFORM_LABELS, pending.platform)}` : '';

  const handleConfirm = () => {
    if (!pending) {
      return;
    }
    setErrorMessage(null);
    mutation.mutate(
      { platform: pending.platform, executionEnabled: pending.enable },
      {
        onSuccess: () => setPending(null),
        onError: (error) => {
          setPending(null);
          setErrorMessage(describeAdsError(error));
        },
      },
    );
  };

  return (
    <section data-testid="kill-switch" aria-labelledby="kill-switch-title" className="bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-md p-4">
      <h2 id="kill-switch-title" className="text-base font-semibold text-gray-900 dark:text-graphite-text">
        Provádění schválených změn
      </h2>
      <p className="mt-1 text-sm text-gray-600 dark:text-graphite-muted">
        Hlavní vypínač. Heblo provede schválenou změnu jen tehdy, když je zapnutý hlavní vypínač i vypínač dané platformy.
        Kontroluje se těsně před každou akcí.
      </p>
      <p className="mt-1 text-sm text-gray-600 dark:text-graphite-muted">
        Zapnout lze jen po přihlášení ve webové aplikaci; vypnout může správce vždy.
      </p>
      {!state.executionEnabled && (
        <p className="mt-2 text-sm text-amber-800 dark:text-amber-300">
          Hlavní vypínač je vypnutý — Heblo teď v reklamních účtech neprovede žádnou změnu.
        </p>
      )}
      {errorMessage && (
        <p role="alert" className="mt-2 text-sm text-red-700 dark:text-red-300">
          {errorMessage}
        </p>
      )}
      <ul className="mt-3 divide-y divide-gray-200 dark:divide-graphite-border">
        <KillSwitchRow
          label={ALL_PLATFORMS_LABEL}
          isEnabled={state.executionEnabled}
          note={null}
          isDisabled={mutation.isPending}
          onToggle={() => setPending({ platform: null, enable: !state.executionEnabled })}
        />
        {AD_PLATFORMS.map((platform) => {
          const override = overrideFor(platform);
          const isEnabled = override?.executionEnabled ?? true;
          return (
            <KillSwitchRow
              key={platform}
              label={labelFor(PLATFORM_LABELS, platform)}
              isEnabled={isEnabled}
              note={override ? null : 'bez vlastního nastavení'}
              isDisabled={mutation.isPending}
              onToggle={() => setPending({ platform, enable: !isEnabled })}
            />
          );
        })}
      </ul>

      <ConfirmActionDialog
        isOpen={pending !== null}
        title={pending?.enable ? 'Zapnout provádění změn?' : 'Vypnout provádění změn?'}
        message={
          pending?.enable
            ? `Heblo začne provádět schválené návrhy${scope} v živých reklamních účtech.`
            : `Heblo přestane provádět schválené návrhy${scope}. Rozběhnuté provádění se zastaví před další akcí.`
        }
        confirmLabel={pending?.enable ? 'Zapnout provádění' : 'Vypnout provádění'}
        tone={pending?.enable ? 'danger' : 'primary'}
        isPending={mutation.isPending}
        onConfirm={handleConfirm}
        onCancel={() => setPending(null)}
      />
    </section>
  );
};

export default KillSwitchCard;
