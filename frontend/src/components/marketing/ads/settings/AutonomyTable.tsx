import React, { useState } from 'react';
import { useUpdateAdAutonomyModeMutation } from '../../../../api/hooks/useMarketingAdsSettings';
import { describeAdsError } from '../../../../api/hooks/marketingAds/errors';
import { AUTONOMY_MODES, type AdAutonomySetting } from '../../../../api/hooks/marketingAds/types';
import { AUTONOMY_MODE_LABELS, labelFor } from '../shared/adLabels';
import ConfirmActionDialog from '../shared/ConfirmActionDialog';
import { autonomyRowLabel as rowLabel, describeModeChange } from './autonomyCopy';

interface PendingChange {
  row: AdAutonomySetting;
  mode: string;
}

const HEAD = 'px-3 py-2 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase';
const CELL = 'px-3 py-2 text-sm text-gray-700 dark:text-graphite-muted';
const SELECT =
  'rounded-md border border-gray-300 dark:border-graphite-border bg-white dark:bg-graphite-surface-2 text-sm text-gray-900 dark:text-graphite-text py-1 px-2';
const PERCENT = 100;

const rateText = (row: AdAutonomySetting): string =>
  row.approvedWithoutEditRate === null || row.decidedCount === 0
    ? 'zatím bez rozhodnutí'
    : `${Math.round(row.approvedWithoutEditRate * PERCENT)} % z ${row.decidedCount}`;

const AutonomyTable: React.FC<{ rows: AdAutonomySetting[] }> = ({ rows }) => {
  const mutation = useUpdateAdAutonomyModeMutation();
  const [pending, setPending] = useState<PendingChange | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const handleConfirm = () => {
    if (!pending) {
      return;
    }
    setErrorMessage(null);
    mutation.mutate(
      { platform: pending.row.platform, actionType: pending.row.actionType, mode: pending.mode },
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
    <section aria-labelledby="autonomy-title" className="bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-md p-4">
      <h2 id="autonomy-title" className="text-base font-semibold text-gray-900 dark:text-graphite-text">Režim autonomie</h2>
      <p className="mt-1 text-sm text-gray-600 dark:text-graphite-muted">
        Po nasazení je všude „Jen návrhy“. Režim zvyšujte podle podílu návrhů schválených beze změn.
      </p>
      {errorMessage && (
        <p role="alert" className="mt-2 text-sm text-red-700 dark:text-red-300">
          {errorMessage}
        </p>
      )}
      <div className="mt-3 overflow-x-auto">
        <table data-testid="autonomy-table" className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
          <thead className="bg-gray-50 dark:bg-graphite-surface-2">
            <tr>
              <th className={HEAD}>Platforma a typ akce</th>
              <th className={HEAD}>Režim</th>
              <th className={HEAD}>Schváleno beze změn</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200 dark:divide-graphite-border">
            {rows.map((row) => (
              <tr key={`${row.platform}|${row.actionType}`}>
                <td className={`${CELL} text-gray-900 dark:text-graphite-text`}>{rowLabel(row)}</td>
                <td className={CELL}>
                  <select
                    aria-label={`Režim: ${rowLabel(row)}`}
                    className={SELECT}
                    value={row.mode}
                    disabled={mutation.isPending}
                    onChange={(event) => setPending({ row, mode: event.target.value })}
                  >
                    {AUTONOMY_MODES.map((mode) => (
                      <option key={mode} value={mode}>
                        {labelFor(AUTONOMY_MODE_LABELS, mode)}
                      </option>
                    ))}
                  </select>
                </td>
                <td className={CELL}>{rateText(row)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <ConfirmActionDialog
        isOpen={pending !== null}
        title="Změnit režim autonomie?"
        message={pending ? describeModeChange(pending.row, pending.mode) : ''}
        confirmLabel="Změnit režim"
        tone={pending?.mode === 'ProposeOnly' ? 'primary' : 'danger'}
        isPending={mutation.isPending}
        onConfirm={handleConfirm}
        onCancel={() => setPending(null)}
      />
    </section>
  );
};

export default AutonomyTable;
