import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Card, CardContent, CardHeader, CardTitle } from '../ui/card';
import { Loader2, RefreshCw, Clock, CheckCircle, XCircle, AlertCircle } from 'lucide-react';
import { useQueuedJobs, useScheduledJobs, useFailedJobs } from '../../api/hooks/useBackgroundJobs';

interface JobGridProps {
  jobs: any[];
  isLoading: boolean;
  onRefresh: () => void;
  title: string;
}

const JobGrid: React.FC<JobGridProps> = ({ jobs, isLoading, onRefresh, title }) => {
  const { t } = useTranslation();

  const getStateIcon = (state: string) => {
    switch (state.toLowerCase()) {
      case 'succeeded':
      case 'completed':
        return <CheckCircle className="h-4 w-4 text-emerald-500" />;
      case 'failed':
        return <XCircle className="h-4 w-4 text-red-500" />;
      case 'scheduled':
        return <Clock className="h-4 w-4 text-blue-500" />;
      case 'processing':
      case 'enqueued':
        return <AlertCircle className="h-4 w-4 text-yellow-500" />;
      default:
        return <AlertCircle className="h-4 w-4 text-gray-500" />;
    }
  };

  const formatDate = (date: string | null) => {
    if (!date) return '-';
    return new Date(date).toLocaleString('cs-CZ');
  };

  const formatMethod = (method: string) => {
    // Extract just the method name from the full method signature
    const match = method.match(/(\w+)\(/);
    return match ? match[1] : method;
  };

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between">
        <CardTitle className="text-lg font-semibold">{title}</CardTitle>
        <button
          onClick={onRefresh}
          disabled={isLoading}
          className="flex items-center gap-2 px-3 py-1.5 text-sm border border-gray-300 rounded-md hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {isLoading ? (
            <Loader2 className="h-4 w-4 animate-spin" />
          ) : (
            <RefreshCw className="h-4 w-4" />
          )}
          {t('common.refresh')}
        </button>
      </CardHeader>
      <CardContent>
        {isLoading ? (
          <div className="flex items-center justify-center py-8">
            <Loader2 className="h-6 w-6 animate-spin" />
            <span className="ml-2">{t('common.loading')}</span>
          </div>
        ) : jobs.length === 0 ? (
          <div className="text-center py-8 text-gray-500">
            {t('automation.noJobs')}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full border-collapse">
              <thead>
                <tr className="border-b border-gray-200">
                  <th className="text-left py-3 px-4 font-medium text-gray-700">
                    {t('automation.status')}
                  </th>
                  <th className="text-left py-3 px-4 font-medium text-gray-700">
                    {t('automation.jobId')}
                  </th>
                  <th className="text-left py-3 px-4 font-medium text-gray-700">
                    {t('automation.method')}
                  </th>
                  <th className="text-left py-3 px-4 font-medium text-gray-700">
                    {t('automation.enqueuedAt')}
                  </th>
                  <th className="text-left py-3 px-4 font-medium text-gray-700">
                    {t('automation.scheduledAt')}
                  </th>
                  <th className="text-left py-3 px-4 font-medium text-gray-700">
                    {t('automation.arguments')}
                  </th>
                </tr>
              </thead>
              <tbody>
                {jobs.map((job) => (
                  <tr key={job.id} className="border-b border-gray-100 hover:bg-gray-50">
                    <td className="py-3 px-4">
                      <div className="flex items-center gap-2">
                        {getStateIcon(job.state)}
                        <span className="text-sm">{job.state}</span>
                      </div>
                    </td>
                    <td className="py-3 px-4">
                      <span className="text-sm font-mono text-gray-600">
                        {job.id.substring(0, 8)}...
                      </span>
                    </td>
                    <td className="py-3 px-4">
                      <span className="text-sm font-medium">
                        {formatMethod(job.method)}
                      </span>
                    </td>
                    <td className="py-3 px-4">
                      <span className="text-sm text-gray-600">
                        {formatDate(job.enqueuedAt)}
                      </span>
                    </td>
                    <td className="py-3 px-4">
                      <span className="text-sm text-gray-600">
                        {formatDate(job.scheduledAt)}
                      </span>
                    </td>
                    <td className="py-3 px-4">
                      <span className="text-sm text-gray-500 truncate max-w-xs block">
                        {job.arguments || '-'}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </CardContent>
    </Card>
  );
};

const Automatizace: React.FC = () => {
  const { t } = useTranslation();
  const [activeTab, setActiveTab] = useState('queued');

  const {
    data: queuedJobs,
    isLoading: isLoadingQueued,
    refetch: refetchQueued,
  } = useQueuedJobs({
    offset: 0,
    count: 50,
  });

  const {
    data: scheduledJobs,
    isLoading: isLoadingScheduled,
    refetch: refetchScheduled,
  } = useScheduledJobs({
    offset: 0,
    count: 50,
  });

  const {
    data: failedJobs,
    isLoading: isLoadingFailed,
    refetch: refetchFailed,
  } = useFailedJobs({
    offset: 0,
    count: 50,
  });

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">{t('automation.title')}</h1>
        <p className="text-gray-600 mt-1">{t('automation.description')}</p>
      </div>

      <div className="space-y-6">
        <div className="flex space-x-1 bg-gray-100 p-1 rounded-lg max-w-lg">
          <button
            onClick={() => setActiveTab('queued')}
            className={`flex-1 flex items-center justify-center gap-2 px-3 py-2 rounded-md text-sm font-medium transition-colors ${
              activeTab === 'queued'
                ? 'bg-white text-gray-900 shadow-sm'
                : 'text-gray-600 hover:text-gray-900'
            }`}
          >
            <AlertCircle className="h-4 w-4" />
            {t('automation.queuedJobs')}
          </button>
          <button
            onClick={() => setActiveTab('scheduled')}
            className={`flex-1 flex items-center justify-center gap-2 px-3 py-2 rounded-md text-sm font-medium transition-colors ${
              activeTab === 'scheduled'
                ? 'bg-white text-gray-900 shadow-sm'
                : 'text-gray-600 hover:text-gray-900'
            }`}
          >
            <Clock className="h-4 w-4" />
            {t('automation.scheduledJobs')}
          </button>
          <button
            onClick={() => setActiveTab('failed')}
            className={`flex-1 flex items-center justify-center gap-2 px-3 py-2 rounded-md text-sm font-medium transition-colors ${
              activeTab === 'failed'
                ? 'bg-white text-gray-900 shadow-sm'
                : 'text-gray-600 hover:text-gray-900'
            }`}
          >
            <XCircle className="h-4 w-4" />
            {t('automation.failedJobs')}
          </button>
        </div>

        {activeTab === 'queued' && (
          <JobGrid
            jobs={queuedJobs?.jobs || []}
            isLoading={isLoadingQueued}
            onRefresh={() => refetchQueued()}
            title={t('automation.queuedJobsTitle')}
          />
        )}

        {activeTab === 'scheduled' && (
          <JobGrid
            jobs={scheduledJobs?.jobs || []}
            isLoading={isLoadingScheduled}
            onRefresh={() => refetchScheduled()}
            title={t('automation.scheduledJobsTitle')}
          />
        )}

        {activeTab === 'failed' && (
          <JobGrid
            jobs={failedJobs?.jobs || []}
            isLoading={isLoadingFailed}
            onRefresh={() => refetchFailed()}
            title={t('automation.failedJobsTitle')}
          />
        )}
      </div>
    </div>
  );
};

export default Automatizace;