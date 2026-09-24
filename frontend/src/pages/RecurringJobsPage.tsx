import React, { useState, useCallback, useMemo } from 'react';
import { Clock, RefreshCw, AlertCircle, Search } from 'lucide-react';
import { useRecurringJobsQuery, useUpdateRecurringJobStatusMutation, useTriggerRecurringJobMutation, useUpdateRecurringJobCronMutation, RecurringJobCategory, RecurringJobDto } from '../api/hooks/useRecurringJobs';
import { LoadingIndicator } from '../components/ui/LoadingIndicator';
import ConfirmTriggerJobDialog from '../components/dialogs/ConfirmTriggerJobDialog';
import RecurringJobRow from '../components/recurring-jobs/RecurringJobRow';
import RecurringJobCategorySection from '../components/recurring-jobs/RecurringJobCategorySection';
import { groupJobsByCategory } from '../components/recurring-jobs/recurringJobCategories';
import { useScreenView } from '../telemetry/useScreenView';

const RecurringJobsPage: React.FC = () => {
  useScreenView('Automation', 'RecurringJobs');
  const { data: jobs, isLoading, error, refetch } = useRecurringJobsQuery();
  const updateJobStatus = useUpdateRecurringJobStatusMutation();
  const triggerJob = useTriggerRecurringJobMutation();
  const [updatingJobName, setUpdatingJobName] = useState<string | null>(null);
  const [triggeringJobName, setTriggeringJobName] = useState<string | null>(null);
  const [confirmDialogOpen, setConfirmDialogOpen] = useState(false);
  const [selectedJob, setSelectedJob] = useState<RecurringJobDto | null>(null);
  const updateCron = useUpdateRecurringJobCronMutation();
  const [editingCronJobName, setEditingCronJobName] = useState<string | null>(null);
  const [editingCronValue, setEditingCronValue] = useState<string>('');
  const [cronEditError, setCronEditError] = useState<string | null>(null);
  const [searchTerm, setSearchTerm] = useState<string>('');
  const [collapsedCategories, setCollapsedCategories] = useState<ReadonlySet<RecurringJobCategory>>(
    new Set<RecurringJobCategory>()
  );

  const jobsList = useMemo(() => jobs || [], [jobs]);
  const categoryGroups = useMemo(
    () => groupJobsByCategory(jobsList, searchTerm),
    [jobsList, searchTerm]
  );
  // Collapsing is a browsing aid; while searching, every match must be visible.
  const isSearchActive = searchTerm.trim() !== '';

  const handleToggleCategory = useCallback((category: RecurringJobCategory) => {
    setCollapsedCategories((previous) => {
      const next = new Set(previous);
      if (next.has(category)) {
        next.delete(category);
      } else {
        next.add(category);
      }
      return next;
    });
  }, []);

  const handleToggle = async (job: RecurringJobDto) => {
    if (!job.jobName) return;

    setUpdatingJobName(job.jobName);
    try {
      await updateJobStatus.mutateAsync({
        jobName: job.jobName,
        isEnabled: !job.isEnabled
      });
    } catch (error) {
      console.error('Chyba při přepínání stavu jobu:', error);
    } finally {
      setUpdatingJobName(null);
    }
  };

  const handleTriggerClick = (job: RecurringJobDto) => {
    setSelectedJob(job);
    setConfirmDialogOpen(true);
  };

  const handleConfirmTrigger = async () => {
    if (!selectedJob?.jobName) return;

    setConfirmDialogOpen(false);
    setTriggeringJobName(selectedJob.jobName);

    try {
      await triggerJob.mutateAsync(selectedJob.jobName);
    } catch (error) {
      console.error('Chyba při spouštění jobu:', error);
    } finally {
      setTriggeringJobName(null);
      setSelectedJob(null);
    }
  };

  const handleCancelTrigger = () => {
    setConfirmDialogOpen(false);
    setSelectedJob(null);
  };

  const handleEditCron = (job: RecurringJobDto) => {
    setEditingCronJobName(job.jobName || null);
    setEditingCronValue(job.cronExpression || '');
    setCronEditError(null);
  };

  const handleCancelCronEdit = () => {
    setEditingCronJobName(null);
    setEditingCronValue('');
    setCronEditError(null);
  };

  const handleSaveCron = async (job: RecurringJobDto) => {
    if (!job.jobName) return;
    setCronEditError(null);

    try {
      await updateCron.mutateAsync({
        jobName: job.jobName,
        cronExpression: editingCronValue
      });
      setEditingCronJobName(null);
      setEditingCronValue('');
    } catch (error: unknown) {
      const message =
        error instanceof Error ? error.message : 'Chyba při ukládání CRON výrazu';
      setCronEditError(message);
    }
  };

  const formatDate = useCallback((date?: string | Date | null) => {
    if (!date) return 'N/A';
    const dateObj = date instanceof Date ? date : new Date(date);
    return dateObj.toLocaleString('cs-CZ', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  }, []);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <LoadingIndicator isVisible={true} />
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex flex-col h-full w-full">
        {/* Header - Fixed, title only */}
        <div className="flex-shrink-0 mb-3">
          <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">Správa Recurring Jobs</h1>
        </div>

        {/* Main Content - Scrollable */}
        <div className="flex-1 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg overflow-hidden flex flex-col min-h-0">
          <div className="p-6">
            <div className="bg-red-50 dark:bg-red-400/15 border border-red-200 text-red-700 dark:text-red-400 px-4 py-3 rounded flex items-center justify-between">
              <div className="flex items-center">
                <AlertCircle className="h-5 w-5 mr-3" />
                <span>Chyba při načítání recurring jobs: {(error as Error).message}</span>
              </div>
              <button
                onClick={() => refetch()}
                className="inline-flex items-center px-3 py-1.5 bg-red-600 hover:bg-red-700 text-white text-sm font-medium rounded-md transition-colors duration-200"
              >
                <RefreshCw className="h-4 w-4 mr-1.5" />
                Zkusit znovu
              </button>
            </div>
          </div>
        </div>
      </div>
    );
  }

  if (jobsList.length === 0) {
    return (
      <div className="flex flex-col h-full w-full">
        {/* Header - Fixed, title only */}
        <div className="flex-shrink-0 mb-3">
          <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">Správa Recurring Jobs</h1>
        </div>

        {/* Main Content - Scrollable */}
        <div className="flex-1 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg overflow-hidden flex flex-col min-h-0">
          <div className="p-12 text-center">
            <Clock className="h-12 w-12 mx-auto text-gray-300 dark:text-graphite-faint mb-3" />
            <p className="text-gray-500 dark:text-graphite-muted">Žádné recurring jobs nenalezeny</p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full w-full">
      {/* Header - Fixed, title only */}
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">Správa Recurring Jobs</h1>
      </div>

      {/* Main Content - Scrollable */}
      <div className="flex-1 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg overflow-hidden flex flex-col min-h-0">
        {/* Action bar inside content */}
        <div className="px-6 py-4 border-b border-gray-200 dark:border-graphite-border flex items-center justify-between gap-4">
          <div className="flex items-center flex-shrink-0">
            <Clock className="h-5 w-5 text-gray-400 dark:text-graphite-faint mr-2" />
            <p className="text-sm text-gray-500 dark:text-graphite-muted">Zapínání/vypínání Hangfire úloh</p>
          </div>
          <div className="flex items-center gap-3">
            <div className="relative w-64">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400 dark:text-graphite-faint pointer-events-none" />
              <input
                type="search"
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                placeholder="Hledat úlohu…"
                aria-label="Hledat úlohu"
                className="w-full pl-9 pr-3 py-2 text-sm border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md focus:outline-none focus:ring-1 focus:ring-indigo-500"
              />
            </div>
            <button
              onClick={() => refetch()}
              className="inline-flex items-center px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white text-sm font-medium rounded-md transition-colors duration-200 flex-shrink-0"
            >
              <RefreshCw className="h-4 w-4 mr-2" />
              Obnovit
            </button>
          </div>
        </div>

        {/* Table */}
        <div className="overflow-auto flex-1">
          <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
            <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0 z-10">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                  Display Name
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                  Description
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                  Cron Expression
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                  Last Modified
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                  Next Run
                </th>
                <th className="px-6 py-3 text-center text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                  Status
                </th>
                <th className="px-6 py-3 text-center text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                  Actions
                </th>
              </tr>
            </thead>
            {categoryGroups.map((group) => (
              <RecurringJobCategorySection
                key={group.category}
                category={group.category}
                jobCount={group.jobs.length}
                isExpanded={isSearchActive || !collapsedCategories.has(group.category)}
                onToggleExpanded={handleToggleCategory}
              >
                {group.jobs.map((job) => (
                  <RecurringJobRow
                    key={job.jobName}
                    job={job}
                    isUpdatingStatus={updatingJobName === job.jobName}
                    isTriggering={triggeringJobName === job.jobName}
                    isEditingCron={editingCronJobName === job.jobName}
                    isSavingCron={updateCron.isPending}
                    cronDraft={editingCronValue}
                    cronEditError={cronEditError}
                    onCronDraftChange={setEditingCronValue}
                    onStartCronEdit={handleEditCron}
                    onSaveCron={handleSaveCron}
                    onCancelCronEdit={handleCancelCronEdit}
                    onToggle={handleToggle}
                    onTrigger={handleTriggerClick}
                    formatDate={formatDate}
                  />
                ))}
              </RecurringJobCategorySection>
            ))}
          </table>

          {categoryGroups.length === 0 && (
            <div className="p-12 text-center">
              <Search className="h-12 w-12 mx-auto text-gray-300 dark:text-graphite-faint mb-3" />
              <p className="text-gray-500 dark:text-graphite-muted">
                Žádná úloha neodpovídá hledání „{searchTerm}“
              </p>
            </div>
          )}
        </div>
      </div>

      {/* Confirm Trigger Dialog */}
      <ConfirmTriggerJobDialog
        isOpen={confirmDialogOpen}
        jobName={selectedJob?.jobName || ''}
        jobDisplayName={selectedJob?.displayName || selectedJob?.jobName || ''}
        isJobDisabled={!selectedJob?.isEnabled}
        onConfirm={handleConfirmTrigger}
        onCancel={handleCancelTrigger}
      />
    </div>
  );
};

export default RecurringJobsPage;
