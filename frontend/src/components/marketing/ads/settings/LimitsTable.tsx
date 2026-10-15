import React, { useState } from 'react';
import { useUpdateAdLimitMutation } from '../../../../api/hooks/useMarketingAdsSettings';
import { describeAdsError } from '../../../../api/hooks/marketingAds/errors';
import type { AdLimitSetting } from '../../../../api/hooks/marketingAds/types';
import { ACTION_TYPE_LABELS, PLATFORM_LABELS, labelFor } from '../shared/adLabels';

const HEAD = 'px-3 py-2 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase';
const CELL = 'px-3 py-2 text-sm text-gray-700 dark:text-graphite-muted align-top';
const INPUT =
  'w-24 rounded-md border border-gray-300 dark:border-graphite-border bg-white dark:bg-graphite-surface-2 text-sm text-gray-900 dark:text-graphite-text py-1 px-2';
const SAVE_BUTTON =
  'ml-2 px-3 py-1 text-sm font-medium text-white bg-indigo-600 rounded-md hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed';
const NON_NEGATIVE_INTEGER = /^\d+$/;

const rowKey = (row: AdLimitSetting): string => `${row.platform}|${row.actionType}|${row.key}`;

const scopeText = (row: AdLimitSetting): string =>
  `${labelFor(PLATFORM_LABELS, row.platform)} – ${labelFor(ACTION_TYPE_LABELS, row.actionType)}`;

const parseLimit = (raw: string): number | null => (NON_NEGATIVE_INTEGER.test(raw.trim()) ? Number(raw.trim()) : null);

const LimitsTable: React.FC<{ rows: AdLimitSetting[] }> = ({ rows }) => {
  const mutation = useUpdateAdLimitMutation();
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const clearDraft = (key: string) =>
    setDrafts((previous) => Object.fromEntries(Object.entries(previous).filter(([draftKey]) => draftKey !== key)));

  const handleSave = (row: AdLimitSetting, value: number) => {
    setErrorMessage(null);
    mutation.mutate(
      { platform: row.platform, actionType: row.actionType, key: row.key, value },
      {
        onSuccess: () => clearDraft(rowKey(row)),
        onError: (error) => setErrorMessage(describeAdsError(error)),
      },
    );
  };

  return (
    <section aria-labelledby="limits-title" className="bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-md p-4">
      <h2 id="limits-title" className="text-base font-semibold text-gray-900 dark:text-graphite-text">Limity</h2>
      <p className="mt-1 text-sm text-gray-600 dark:text-graphite-muted">
        Platí na serveru vždy, bez ohledu na to, kdo návrh schválil. Každá změna se zapíše do auditního logu.
      </p>
      {errorMessage && (
        <p role="alert" className="mt-2 text-sm text-red-700 dark:text-red-300">
          {errorMessage}
        </p>
      )}
      <div className="mt-3 overflow-x-auto">
        <table data-testid="limits-table" className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
          <thead className="bg-gray-50 dark:bg-graphite-surface-2">
            <tr>
              <th className={HEAD}>Limit</th>
              <th className={HEAD}>Platí pro</th>
              <th className={HEAD}>Hodnota</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200 dark:divide-graphite-border">
            {rows.map((row) => {
              const key = rowKey(row);
              const draft = drafts[key] ?? String(row.value);
              const parsed = parseLimit(draft);
              const isDirty = parsed !== row.value;
              return (
                <tr key={key}>
                  <td className={`${CELL} font-mono text-gray-900 dark:text-graphite-text`}>{row.key}</td>
                  <td className={CELL}>{scopeText(row)}</td>
                  <td className={CELL}>
                    <input
                      type="number"
                      min={0}
                      step={1}
                      aria-label={`Limit: ${row.key} (${scopeText(row)})`}
                      className={INPUT}
                      value={draft}
                      onChange={(event) => setDrafts((previous) => ({ ...previous, [key]: event.target.value }))}
                    />
                    <button
                      type="button"
                      className={SAVE_BUTTON}
                      aria-label={`Uložit limit ${row.key}`}
                      disabled={parsed === null || !isDirty || mutation.isPending}
                      onClick={() => parsed !== null && handleSave(row, parsed)}
                    >
                      Uložit
                    </button>
                    {parsed === null && (
                      <div className="mt-1 text-xs text-red-700 dark:text-red-300">Zadejte celé číslo 0 nebo větší.</div>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </section>
  );
};

export default LimitsTable;
