import React from 'react';
import { RefreshCw, ToggleLeft, ToggleRight, Play, Pencil, Check, X } from 'lucide-react';
import { RecurringJobDto } from '../../api/hooks/useRecurringJobs';

export interface RecurringJobRowProps {
  job: RecurringJobDto;
  isUpdatingStatus: boolean;
  isTriggering: boolean;
  isEditingCron: boolean;
  isSavingCron: boolean;
  cronDraft: string;
  cronEditError: string | null;
  onCronDraftChange: (value: string) => void;
  onStartCronEdit: (job: RecurringJobDto) => void;
  onSaveCron: (job: RecurringJobDto) => void;
  onCancelCronEdit: () => void;
  onToggle: (job: RecurringJobDto) => void;
  onTrigger: (job: RecurringJobDto) => void;
  formatDate: (date?: string | Date | null) => string;
}

const RecurringJobRow: React.FC<RecurringJobRowProps> = ({
  job,
  isUpdatingStatus,
  isTriggering,
  isEditingCron,
  isSavingCron,
  cronDraft,
  cronEditError,
  onCronDraftChange,
  onStartCronEdit,
  onSaveCron,
  onCancelCronEdit,
  onToggle,
  onTrigger,
  formatDate
}) => {
  const jobLabel = job.displayName || job.jobName;

  return (
    <tr data-testid="recurring-job-row" className="hover:bg-gray-50 dark:hover:bg-white/5">
      <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-graphite-text">
        {jobLabel}
      </td>
      <td className="px-6 py-4 text-sm text-gray-700 dark:text-graphite-muted max-w-xs">
        {job.description || '-'}
      </td>
      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-600 dark:text-graphite-muted">
        {isEditingCron ? (
          <div className="flex flex-col gap-1">
            <div className="flex items-center gap-1">
              <input
                type="text"
                value={cronDraft}
                onChange={(e) => onCronDraftChange(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') onSaveCron(job);
                  if (e.key === 'Escape') onCancelCronEdit();
                }}
                className="font-mono text-xs border border-gray-300 dark:border-graphite-border rounded px-2 py-1 w-32 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                autoFocus
                aria-label="CRON výraz"
              />
              <button
                onClick={() => onSaveCron(job)}
                disabled={isSavingCron}
                aria-label="Uložit CRON výraz"
                className="text-green-600 dark:text-emerald-400 hover:text-green-800 disabled:opacity-50"
                title="Uložit"
              >
                {isSavingCron ? (
                  <RefreshCw className="h-4 w-4 animate-spin" />
                ) : (
                  <Check className="h-4 w-4" />
                )}
              </button>
              <button
                onClick={onCancelCronEdit}
                aria-label="Zrušit úpravu CRON výrazu"
                className="text-gray-400 dark:text-graphite-faint hover:text-gray-600"
                title="Zrušit"
              >
                <X className="h-4 w-4" />
              </button>
            </div>
            {cronEditError && (
              <span className="text-xs text-red-600 dark:text-red-400">{cronEditError}</span>
            )}
          </div>
        ) : (
          <div className="flex items-center gap-1 group">
            <span className="font-mono">{job.cronExpression || '-'}</span>
            <button
              onClick={() => onStartCronEdit(job)}
              aria-label={`Upravit CRON výraz pro ${jobLabel}`}
              className="opacity-0 group-hover:opacity-100 transition-opacity text-gray-400 dark:text-graphite-faint hover:text-gray-600"
              title="Upravit CRON"
            >
              <Pencil className="h-3.5 w-3.5" />
            </button>
          </div>
        )}
      </td>
      <td className="px-6 py-4 whitespace-nowrap">
        <div className="text-sm text-gray-900 dark:text-graphite-text">
          {formatDate(job.lastModifiedAt)}
        </div>
        {job.lastModifiedBy && (
          <div className="text-xs text-gray-500 dark:text-graphite-muted mt-0.5">
            {job.lastModifiedBy}
          </div>
        )}
      </td>
      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-600 dark:text-graphite-muted">
        {job.nextRunAt ? formatDate(job.nextRunAt) : '—'}
      </td>
      <td className="px-6 py-4 whitespace-nowrap text-center">
        <button
          onClick={() => onToggle(job)}
          disabled={isUpdatingStatus}
          aria-label={`${job.isEnabled ? 'Vypnout' : 'Zapnout'} úlohu ${jobLabel}`}
          role="switch"
          aria-checked={job.isEnabled}
          className={`
            inline-flex items-center px-3 py-1.5 rounded-full text-xs font-medium transition-all duration-200
            ${job.isEnabled
              ? 'bg-emerald-100 dark:bg-emerald-400/15 text-emerald-800 dark:text-emerald-400 hover:bg-emerald-200 dark:hover:bg-emerald-400/25'
              : 'bg-gray-100 dark:bg-white/10 text-gray-800 dark:text-graphite-muted hover:bg-gray-200'
            }
            ${isUpdatingStatus ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}
          `}
          title={job.isEnabled ? 'Klikněte pro vypnutí' : 'Klikněte pro zapnutí'}
        >
          {isUpdatingStatus ? (
            <RefreshCw className="h-3 w-3 mr-1 animate-spin" />
          ) : job.isEnabled ? (
            <ToggleRight className="h-3.5 w-3.5 mr-1" />
          ) : (
            <ToggleLeft className="h-3.5 w-3.5 mr-1" />
          )}
          {job.isEnabled ? 'Zapnuto' : 'Vypnuto'}
        </button>
      </td>
      <td className="px-6 py-4 whitespace-nowrap text-center">
        <button
          onClick={() => onTrigger(job)}
          disabled={isTriggering}
          aria-label={`Spustit úlohu ${jobLabel} nyní`}
          className={`
            inline-flex items-center px-3 py-1.5 rounded-md text-xs font-medium transition-all duration-200
            bg-indigo-100 dark:bg-graphite-accent/10 text-indigo-800 dark:text-graphite-accent hover:bg-indigo-200 dark:hover:bg-graphite-accent/20
            ${isTriggering ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}
          `}
          title="Spustit úlohu nyní"
        >
          {isTriggering ? (
            <RefreshCw className="h-3.5 w-3.5 mr-1 animate-spin" />
          ) : (
            <Play className="h-3.5 w-3.5 mr-1" />
          )}
          Run Now
        </button>
      </td>
    </tr>
  );
};

export default RecurringJobRow;
