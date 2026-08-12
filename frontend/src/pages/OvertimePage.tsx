import React, { useState } from 'react';
import { ChevronLeft, ChevronRight, Download, Upload, Lock, AlertCircle, AlertTriangle, Plus, RefreshCw, ChevronDown, ChevronUp } from 'lucide-react';
import {
  useOvertimeEmployeesQuery,
  useMonthlyStatementsQuery,
  useSetReviewedMutation,
  useCloseMonthMutation,
  usePublishReportMutation,
  downloadOvertimeReport,
  type OvertimeStatementDto,
} from '../api/hooks/useOvertime';
import { usePermissionsContext } from '../auth/PermissionsContext';
import { useToast } from '../contexts/ToastContext';
import { LoadingIndicator } from '../components/ui/LoadingIndicator';
import CloseOvertimeMonthDialog from '../components/dialogs/CloseOvertimeMonthDialog';
import StatementAdjustmentsPanel from '../components/overtime/StatementAdjustmentsPanel';
import EmployeeSettingsSection from '../components/overtime/EmployeeSettingsSection';
import { useScreenView } from '../telemetry/useScreenView';
import { formatNumber } from '../utils/formatters';
import { extractErrorMessage } from '../utils/errorHandler';

const WRITE_PERMISSION = 'attendance.overtime.write';
const MONTH_LABELS = [
  'leden', 'únor', 'březen', 'duben', 'květen', 'červen',
  'červenec', 'srpen', 'září', 'říjen', 'listopad', 'prosinec',
];

const previousMonth = (): { year: number; month: number } => {
  const now = new Date();
  const month = now.getMonth() === 0 ? 12 : now.getMonth(); // getMonth() is 0-based → already the previous month, 1-based
  const year = now.getMonth() === 0 ? now.getFullYear() - 1 : now.getFullYear();
  return { year, month };
};

const deltaClass = (value: number | undefined): string =>
  (value ?? 0) >= 0 ? 'text-green-600' : 'text-red-600';

const OvertimePage: React.FC = () => {
  useScreenView('Admin', 'Overtime');
  const [{ year, month }, setPeriod] = useState(previousMonth);
  const [closeDialogOpen, setCloseDialogOpen] = useState(false);
  const [expandedPerson, setExpandedPerson] = useState<string | null>(null);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const { hasPermission } = usePermissionsContext();
  const canWrite = hasPermission(WRITE_PERMISSION);
  const { showSuccess, showError } = useToast();

  const statementsQuery = useMonthlyStatementsQuery(year, month);
  const employeesQuery = useOvertimeEmployeesQuery();
  const setReviewed = useSetReviewedMutation();
  const closeMonth = useCloseMonthMutation();
  const publishReport = usePublishReportMutation();

  const monthLabel = `${MONTH_LABELS[month - 1]} ${year}`;
  const isClosed = statementsQuery.data?.isClosed ?? false;
  const statements: OvertimeStatementDto[] = statementsQuery.data?.statements ?? [];
  const employees = employeesQuery.data?.employees ?? [];
  const availablePeople = employeesQuery.data?.availablePeople ?? [];

  const shiftMonth = (delta: number) =>
    setPeriod(({ year: y, month: m }) => {
      const next = m + delta;
      if (next < 1) return { year: y - 1, month: 12 };
      if (next > 12) return { year: y + 1, month: 1 };
      return { year: y, month: next };
    });

  const handleToggleReviewed = async (personId: string | undefined, isReviewed: boolean) => {
    if (!personId) return;
    try {
      await setReviewed.mutateAsync({ personId, year, month, isReviewed: !isReviewed });
    } catch (err) {
      showError('Uložení selhalo', extractErrorMessage(err));
    }
  };

  const handleClose = async (force: boolean) => {
    setCloseDialogOpen(false);
    try {
      const result = await closeMonth.mutateAsync({ year, month, force });
      if (result?.publishFailed) {
        showError('Měsíc uzavřen', 'Nahrání reportu na SharePoint ale selhalo.');
      } else {
        showSuccess('Měsíc uzavřen', `Uzavřeno ${result?.closedCount ?? 0} zaměstnanců.`);
      }
    } catch (err) {
      showError('Uzavření selhalo', extractErrorMessage(err));
    }
  };

  const handlePublish = async () => {
    try {
      await publishReport.mutateAsync(undefined);
      showSuccess('Report nahrán', 'Report byl nahrán na SharePoint.');
    } catch (err) {
      showError('Nahrání selhalo', extractErrorMessage(err));
    }
  };

  const handleDownloadReport = async () => {
    try {
      await downloadOvertimeReport();
    } catch (err) {
      showError('Stažení selhalo', extractErrorMessage(err));
    }
  };

  const unreviewedNames = statements
    .filter((s) => !s.isReviewed)
    .map((s) => s.displayName ?? '');

  if (statementsQuery.isLoading || employeesQuery.isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <LoadingIndicator isVisible={true} />
      </div>
    );
  }

  if (statementsQuery.error) {
    return (
      <div className="flex flex-col h-full w-full">
        <div className="flex-shrink-0 mb-3">
          <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">Evidence přesčasů</h1>
        </div>
        <div className="flex-1 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg overflow-hidden flex flex-col min-h-0">
          <div className="p-6">
            <div className="bg-red-50 dark:bg-red-400/15 border border-red-200 text-red-700 dark:text-red-400 px-4 py-3 rounded flex items-center justify-between">
              <div className="flex items-center">
                <AlertCircle className="h-5 w-5 mr-3" />
                <span>Chyba při načítání výkazů: {(statementsQuery.error as Error).message}</span>
              </div>
              <button
                onClick={() => statementsQuery.refetch()}
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

  return (
    <div className="flex flex-col h-full w-full">
      {/* Header - Fixed */}
      <div className="flex-shrink-0 mb-3 flex items-center justify-between">
        <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">Evidence přesčasů</h1>
        <div className="flex items-center gap-2">
          <button
            onClick={() => shiftMonth(-1)}
            aria-label="Předchozí měsíc"
            className="p-1.5 rounded-md text-gray-500 dark:text-graphite-muted hover:bg-gray-100 dark:hover:bg-white/5"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <span className="text-sm font-medium text-gray-900 dark:text-graphite-text w-28 text-center">
            {monthLabel}
          </span>
          <button
            onClick={() => shiftMonth(1)}
            aria-label="Následující měsíc"
            className="p-1.5 rounded-md text-gray-500 dark:text-graphite-muted hover:bg-gray-100 dark:hover:bg-white/5"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>
      </div>

      {/* Main Content - Scrollable */}
      <div className="flex-1 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg overflow-hidden flex flex-col min-h-0">
        {/* Action bar */}
        <div className="px-6 py-4 border-b border-gray-200 dark:border-graphite-border flex items-center justify-between">
          <div className="flex items-center gap-2">
            {isClosed && (
              <span className="inline-flex items-center px-3 py-1.5 rounded-full text-xs font-medium bg-gray-100 dark:bg-white/10 text-gray-800 dark:text-graphite-muted">
                <Lock className="h-3.5 w-3.5 mr-1.5" />
                Uzavřeno
              </span>
            )}
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={handleDownloadReport}
              className="inline-flex items-center px-4 py-2 bg-white dark:bg-graphite-surface-2 border border-gray-300 dark:border-graphite-border text-gray-700 dark:text-graphite-muted text-sm font-medium rounded-md hover:bg-gray-50 dark:hover:bg-white/5 transition-colors duration-200"
            >
              <Download className="h-4 w-4 mr-2" />
              Stáhnout Excel
            </button>
            {canWrite && (
              <button
                onClick={handlePublish}
                disabled={publishReport.isPending}
                className="inline-flex items-center px-4 py-2 bg-white dark:bg-graphite-surface-2 border border-gray-300 dark:border-graphite-border text-gray-700 dark:text-graphite-muted text-sm font-medium rounded-md hover:bg-gray-50 dark:hover:bg-white/5 transition-colors duration-200 disabled:opacity-50"
              >
                <Upload className="h-4 w-4 mr-2" />
                Nahrát na SharePoint
              </button>
            )}
            {canWrite && !isClosed && (
              <button
                onClick={() => setCloseDialogOpen(true)}
                className="inline-flex items-center px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white text-sm font-medium rounded-md transition-colors duration-200"
              >
                <Lock className="h-4 w-4 mr-2" />
                Uzavřít měsíc
              </button>
            )}
          </div>
        </div>

        {/* Table */}
        <div className="overflow-auto flex-1">
          <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
            <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0">
              <tr>
                <th className="px-3 py-3 text-center text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Zkontrolováno</th>
                <th className="px-3 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Zaměstnanec</th>
                <th className="px-3 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Převod</th>
                <th className="px-3 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Úvazek</th>
                <th className="px-3 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Odpracováno</th>
                <th className="px-3 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Dovolená</th>
                <th className="px-3 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Nemoc</th>
                <th className="px-3 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Lékař</th>
                <th className="px-3 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">NV</th>
                <th className="px-3 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Rozdíl</th>
                <th className="px-3 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Korekce</th>
                <th className="px-3 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Zůstatek</th>
                <th className="px-3 py-3 text-center text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"></th>
                <th className="px-3 py-3 text-center text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"></th>
              </tr>
            </thead>
            <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
              {statements.length === 0 && (
                <tr>
                  <td colSpan={14} className="px-6 py-12 text-center text-sm text-gray-500 dark:text-graphite-muted">
                    Žádné výkazy za tento měsíc
                  </td>
                </tr>
              )}
              {statements.map((statement) => {
                const isExpanded = expandedPerson === statement.personId;
                return (
                  <React.Fragment key={statement.personId}>
                    <tr className="hover:bg-gray-50 dark:hover:bg-white/5">
                      <td className="px-3 py-3 text-center">
                        <input
                          type="checkbox"
                          checked={statement.isReviewed ?? false}
                          onChange={() => handleToggleReviewed(statement.personId, statement.isReviewed ?? false)}
                          disabled={isClosed || !canWrite || setReviewed.isPending}
                          aria-label={`Zkontrolováno ${statement.displayName}`}
                        />
                      </td>
                      <td className="px-3 py-3 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-graphite-text">
                        {statement.displayName}
                      </td>
                      <td className="px-3 py-3 text-right text-sm text-gray-700 dark:text-graphite-muted">
                        {formatNumber(statement.previousBalance ?? null)}
                      </td>
                      <td className="px-3 py-3 text-right text-sm text-gray-700 dark:text-graphite-muted">
                        {formatNumber(statement.requiredHours ?? null)}
                      </td>
                      <td className="px-3 py-3 text-right text-sm text-gray-700 dark:text-graphite-muted">
                        {formatNumber(statement.workedHours ?? null)}
                      </td>
                      <td className="px-3 py-3 text-right text-sm text-gray-700 dark:text-graphite-muted">
                        {formatNumber(statement.vacationHours ?? null)}
                      </td>
                      <td className="px-3 py-3 text-right text-sm text-gray-700 dark:text-graphite-muted">
                        {formatNumber(statement.sickHours ?? null)}
                      </td>
                      <td className="px-3 py-3 text-right text-sm text-gray-700 dark:text-graphite-muted">
                        {formatNumber(statement.doctorHours ?? null)}
                      </td>
                      <td className="px-3 py-3 text-right text-sm text-gray-700 dark:text-graphite-muted">
                        {formatNumber(statement.compTimeHours ?? null)}
                      </td>
                      <td className={`px-3 py-3 text-right text-sm font-medium ${deltaClass(statement.deltaHours)}`}>
                        {formatNumber(statement.deltaHours ?? null)}
                      </td>
                      <td className={`px-3 py-3 text-right text-sm font-medium ${deltaClass(statement.adjustmentsTotal)}`}>
                        {formatNumber(statement.adjustmentsTotal ?? null)}
                      </td>
                      <td className="px-3 py-3 text-right text-sm font-bold text-gray-900 dark:text-graphite-text">
                        {formatNumber(statement.projectedBalance ?? null)}
                      </td>
                      <td className="px-3 py-3 text-center">
                        {(statement.warnings?.length ?? 0) > 0 && (
                          <span title={(statement.warnings ?? []).join('\n')}>
                            <AlertTriangle className="h-4 w-4 text-amber-500 inline" />
                          </span>
                        )}
                      </td>
                      <td className="px-3 py-3 text-center">
                        <button
                          onClick={() => setExpandedPerson(isExpanded ? null : (statement.personId ?? null))}
                          aria-label={`Korekce ${statement.displayName}`}
                          className="inline-flex items-center justify-center h-6 w-6 rounded text-gray-500 dark:text-graphite-muted hover:bg-gray-100 dark:hover:bg-white/10"
                        >
                          {isExpanded ? <ChevronUp className="h-4 w-4" /> : <Plus className="h-4 w-4" />}
                        </button>
                      </td>
                    </tr>
                    {isExpanded && statement.personId && (
                      <tr>
                        <td colSpan={14} className="p-0">
                          <StatementAdjustmentsPanel
                            personId={statement.personId}
                            year={year}
                            month={month}
                            adjustments={statement.adjustments ?? []}
                            canWrite={canWrite}
                            isClosed={isClosed}
                          />
                        </td>
                      </tr>
                    )}
                  </React.Fragment>
                );
              })}
            </tbody>
          </table>
        </div>

        {/* Employee settings */}
        <div className="flex-shrink-0 border-t border-gray-200 dark:border-graphite-border">
          <button
            onClick={() => setSettingsOpen((open) => !open)}
            className="w-full flex items-center justify-between px-6 py-3 text-sm font-medium text-gray-700 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5"
          >
            Nastavení zaměstnanců
            {settingsOpen ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
          </button>
          {settingsOpen && (
            <div className="px-6 pb-4">
              <EmployeeSettingsSection
                employees={employees}
                availablePeople={availablePeople}
                canWrite={canWrite}
              />
            </div>
          )}
        </div>
      </div>

      <CloseOvertimeMonthDialog
        isOpen={closeDialogOpen}
        monthLabel={monthLabel}
        unreviewedNames={unreviewedNames}
        onConfirm={handleClose}
        onCancel={() => setCloseDialogOpen(false)}
      />
    </div>
  );
};

export default OvertimePage;
