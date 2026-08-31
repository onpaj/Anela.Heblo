import React, { useState } from "react";
import { Printer, Play } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import { QUERY_KEYS } from "../../../api/client";
import { useRunExpeditionListPrintFix } from "../../../api/hooks/useExpeditionList";
import {
  useTriggerRecurringJobMutation,
  useRecurringJobQuery,
  useUpdateRecurringJobStatusMutation,
} from "../../../api/hooks/useRecurringJobs";
import { usePermissionsContext } from "../../../auth/PermissionsContext";
import { useToast } from "../../../contexts/ToastContext";
import PrintOrderModal from "../../../components/modals/PrintOrderModal";

const PRINT_JOB_NAME = "print-picking-list";
const TRIGGER_JOBS_PERMISSION = "jobs.trigger.read";
const DISABLE_JOBS_PERMISSION = "jobs.disable.read";

const formatDateTime = (iso: string | null): string => {
  if (!iso) return "–";
  return new Date(iso).toLocaleString("cs-CZ", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
};

const ExpeditionJobControlsBar: React.FC = () => {
  const { showSuccess, showError } = useToast();
  const queryClient = useQueryClient();
  const [isPrintOrderModalOpen, setIsPrintOrderModalOpen] = useState(false);

  const triggerJobMutation = useTriggerRecurringJobMutation();
  const runFixMutation = useRunExpeditionListPrintFix();

  const { hasPermission } = usePermissionsContext();
  const canTriggerJob = hasPermission(TRIGGER_JOBS_PERMISSION);
  const canToggleJob = hasPermission(DISABLE_JOBS_PERMISSION);
  const { data: printJob } = useRecurringJobQuery(PRINT_JOB_NAME, canTriggerJob || canToggleJob);
  const updateStatusMutation = useUpdateRecurringJobStatusMutation();

  const handleRunJob = async () => {
    try {
      await triggerJobMutation.mutateAsync(PRINT_JOB_NAME);
      showSuccess('Spuštěno', 'Tisk expedičního listu byl spuštěn.');
    } catch {
      showError('Chyba', 'Nepodařilo se spustit tisk expedičního listu.');
    }
  };

  const handleToggleJob = async () => {
    if (!printJob) return;
    try {
      await updateStatusMutation.mutateAsync({
        jobName: PRINT_JOB_NAME,
        isEnabled: !printJob.isEnabled,
      });
      showSuccess(
        'Uloženo',
        printJob.isEnabled
          ? 'Expediční robot byl vypnut.'
          : 'Expediční robot byl zapnut.',
      );
    } catch {
      showError('Chyba', 'Nepodařilo se změnit nastavení expedičního robota.');
    }
  };

  const handleRunFix = async () => {
    try {
      const result = await runFixMutation.mutateAsync();
      showSuccess('Spuštěno', `Tisk oprav dokončen. Celkem objednávek: ${result.totalCount}.`);
    } catch {
      showError('Chyba', 'Nepodařilo se spustit tisk oprav.');
    }
  };

  // Invalidates the shared QueryClientProvider from App.tsx (this component and
  // ExpeditionListArchivePage sit under the same provider), so the archive page's
  // date/items queries refetch after a successful order print, with no prop/callback needed.
  const handlePrintOrderSuccess = async (orderCode: string) => {
    setIsPrintOrderModalOpen(false);
    showSuccess("Zakázka vytištěna", `Zakázka ${orderCode} byla odeslána na tisk a převedena do stavu „Balí se".`);
    await queryClient.invalidateQueries({ queryKey: QUERY_KEYS.expeditionListArchive });
  };

  return (
    <>
      {(canTriggerJob || canToggleJob) && (
        <div className="flex items-center gap-3">
          {canToggleJob && (
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium text-gray-700 dark:text-graphite-muted">Expediční robot</span>
              <button
                type="button"
                role="switch"
                aria-checked={printJob?.isEnabled ?? false}
                aria-label="Expediční robot"
                onClick={handleToggleJob}
                disabled={!printJob || updateStatusMutation.isPending}
                className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 disabled:opacity-50 disabled:cursor-not-allowed ${
                  printJob?.isEnabled ? "bg-indigo-600" : "bg-gray-200"
                }`}
              >
                <span
                  className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                    printJob?.isEnabled ? "translate-x-6" : "translate-x-1"
                  }`}
                />
              </button>
            </div>
          )}
          <span className="text-sm text-gray-500 dark:text-graphite-muted whitespace-nowrap">
            Další běh: {formatDateTime(printJob?.nextRunAt ? printJob.nextRunAt.toISOString() : null)}
          </span>
        </div>
      )}
      <div className="flex items-center gap-2">
        <button
          onClick={() => setIsPrintOrderModalOpen(true)}
          className="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 disabled:opacity-50 transition-colors"
        >
          <Printer size={14} />
          Tisknout zakázku
        </button>
        <button
          onClick={handleRunFix}
          disabled={runFixMutation.isPending}
          className="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-amber-600 rounded-lg hover:bg-amber-700 disabled:opacity-50 transition-colors"
        >
          {runFixMutation.isPending ? (
            <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white" />
          ) : (
            <Play size={14} />
          )}
          Spustit tisk oprav
        </button>
        {canTriggerJob && (
          <button
            onClick={handleRunJob}
            disabled={triggerJobMutation.isPending}
            className="inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 disabled:opacity-50 transition-colors"
          >
            {triggerJobMutation.isPending ? (
              <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white" />
            ) : (
              <Play size={14} />
            )}
            Spustit tisk
          </button>
        )}
      </div>

      <PrintOrderModal
        isOpen={isPrintOrderModalOpen}
        onClose={() => setIsPrintOrderModalOpen(false)}
        onSuccess={handlePrintOrderSuccess}
      />
    </>
  );
};

export default ExpeditionJobControlsBar;
