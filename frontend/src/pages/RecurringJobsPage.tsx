import React, { useState, useCallback } from 'react';
import { Clock, RefreshCw, AlertCircle, ToggleLeft, ToggleRight } from 'lucide-react';
import { useRecurringJobsQuery, useUpdateRecurringJobStatusMutation, RecurringJobDto } from '../api/hooks/useRecurringJobs';
import { LoadingIndicator } from '../components/ui/LoadingIndicator';

const RecurringJobsPage: React.FC = () => {
  const { data: jobs, isLoading, error, refetch } = useRecurringJobsQuery();
  const updateJobStatus = useUpdateRecurringJobStatusMutation();
  const [updatingJobName, setUpdatingJobName] = useState<string | null>(null);

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
          <h1 className="text-lg font-semibold text-gray-900">Správa Recurring Jobs</h1>
        </div>

        {/* Main Content - Scrollable */}
        <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0">
          <div className="p-6">
            <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded flex items-center justify-between">
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

  const jobsList = jobs || [];

  if (jobsList.length === 0) {
    return (
      <div className="flex flex-col h-full w-full">
        {/* Header - Fixed, title only */}
        <div className="flex-shrink-0 mb-3">
          <h1 className="text-lg font-semibold text-gray-900">Správa Recurring Jobs</h1>
        </div>

        {/* Main Content - Scrollable */}
        <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0">
          <div className="p-12 text-center">
            <Clock className="h-12 w-12 mx-auto text-gray-300 mb-3" />
            <p className="text-gray-500">Žádné recurring jobs nenalezeny</p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full w-full">
      {/* Header - Fixed, title only */}
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900">Správa Recurring Jobs</h1>
      </div>

      {/* Main Content - Scrollable */}
      <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0">
        {/* Action bar inside content */}
        <div className="px-6 py-4 border-b border-gray-200 flex items-center justify-between">
          <div className="flex items-center">
            <Clock className="h-5 w-5 text-gray-400 mr-2" />
            <p className="text-sm text-gray-500">Zapínání/vypínání Hangfire úloh</p>
          </div>
          <button
            onClick={() => refetch()}
            className="inline-flex items-center px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white text-sm font-medium rounded-md transition-colors duration-200"
          >
            <RefreshCw className="h-4 w-4 mr-2" />
            Obnovit
          </button>
        </div>

        {/* Table */}
        <div className="overflow-auto flex-1">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50 sticky top-0">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Display Name
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Description
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Cron Expression
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Last Modified
                </th>
                <th className="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Status
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {jobsList.map((job) => (
                <tr key={job.jobName} className="hover:bg-gray-50">
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                    {job.displayName || job.jobName}
                  </td>
                  <td className="px-6 py-4 text-sm text-gray-700 max-w-xs">
                    {job.description || '-'}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-mono text-gray-600">
                    {job.cronExpression || '-'}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap">
                    <div className="text-sm text-gray-900">
                      {formatDate(job.lastModifiedAt)}
                    </div>
                    {job.lastModifiedBy && (
                      <div className="text-xs text-gray-500 mt-0.5">
                        {job.lastModifiedBy}
                      </div>
                    )}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-center">
                    <button
                      onClick={() => handleToggle(job)}
                      disabled={updatingJobName === job.jobName}
                      aria-label={`${job.isEnabled ? 'Vypnout' : 'Zapnout'} úlohu ${job.displayName || job.jobName}`}
                      role="switch"
                      aria-checked={job.isEnabled}
                      className={`
                        inline-flex items-center px-3 py-1.5 rounded-full text-xs font-medium transition-all duration-200
                        ${job.isEnabled
                          ? 'bg-emerald-100 text-emerald-800 hover:bg-emerald-200'
                          : 'bg-gray-100 text-gray-800 hover:bg-gray-200'
                        }
                        ${updatingJobName === job.jobName ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}
                      `}
                      title={job.isEnabled ? 'Klikněte pro vypnutí' : 'Klikněte pro zapnutí'}
                    >
                      {updatingJobName === job.jobName ? (
                        <RefreshCw className="h-3 w-3 mr-1 animate-spin" />
                      ) : job.isEnabled ? (
                        <ToggleRight className="h-3.5 w-3.5 mr-1" />
                      ) : (
                        <ToggleLeft className="h-3.5 w-3.5 mr-1" />
                      )}
                      {job.isEnabled ? 'Zapnuto' : 'Vypnuto'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
};

export default RecurringJobsPage;
