import React from 'react';
import type { AdActionResult } from '../../../../api/hooks/marketingAds/types';
import { ACTION_OUTCOME_LABELS, REVERT_STATE_LABELS, labelFor } from '../shared/adLabels';

interface ProposalDiffProps {
  /** Change lines rendered by Heblo from the structured actions (spec 6.1) — never from reasoning. */
  lines: string[];
  results: AdActionResult[];
}

const CELL = 'px-3 py-2 align-top';
const HEAD = 'px-3 py-2 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase';
const NOT_REVERTED = 'NotReverted';
const VALIDATE_ONLY_SUFFIX = ' (jen ověření)';
const ERROR_TEXT = 'text-xs text-red-700 dark:text-red-300 break-words';

const ResultCell: React.FC<{ result: AdActionResult | undefined }> = ({ result }) => {
  if (!result) {
    return <>—</>;
  }
  const outcome = labelFor(ACTION_OUTCOME_LABELS, result.outcome);
  return (
    <>
      <span title={result.error ?? undefined}>{result.validateOnly ? `${outcome}${VALIDATE_ONLY_SUFFIX}` : outcome}</span>
      {result.error && <div className={ERROR_TEXT}>{result.error}</div>}
      {result.revertState !== NOT_REVERTED && (
        <div className="text-xs text-gray-500 dark:text-graphite-muted">
          <span title={result.revertError ?? undefined}>{labelFor(REVERT_STATE_LABELS, result.revertState)}</span>
          {result.revertError && <div className={ERROR_TEXT}>{result.revertError}</div>}
        </div>
      )}
    </>
  );
};

const ProposalDiff: React.FC<ProposalDiffProps> = ({ lines, results }) => {
  const resultByIndex = new Map(results.map((result) => [result.actionIndex, result]));

  return (
    <section aria-labelledby="proposal-diff-title" data-testid="proposal-diff">
      <h3 id="proposal-diff-title" className="text-sm font-semibold text-gray-900 dark:text-graphite-text">
        {`Změny (${lines.length})`}
      </h3>
      <p className="text-xs text-gray-500 dark:text-graphite-muted mb-2">
        Seznam sestavil Heblo ze strukturovaného návrhu. Schvalujete přesně tyto změny.
      </p>
      <div className="overflow-x-auto border border-gray-200 dark:border-graphite-border rounded-md">
        <table className="min-w-full text-sm divide-y divide-gray-200 dark:divide-graphite-border">
          <thead className="bg-gray-50 dark:bg-graphite-surface-2">
            <tr>
              <th className={HEAD}>#</th>
              <th className={HEAD}>Změna</th>
              <th className={HEAD}>Výsledek</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200 dark:divide-graphite-border">
            {lines.map((line, index) => (
              // Lines are a fixed, ordered list per version; the index is the action's identity.
              <tr key={index}>
                <td className={`${CELL} text-gray-500 dark:text-graphite-muted`}>{index + 1}</td>
                <td className={`${CELL} text-gray-900 dark:text-graphite-text break-words`}>{line}</td>
                <td className={`${CELL} text-gray-700 dark:text-graphite-muted`}>
                  <ResultCell result={resultByIndex.get(index)} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
};

export default ProposalDiff;
