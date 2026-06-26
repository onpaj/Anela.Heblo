import React from 'react';
import { useTranslation } from 'react-i18next';
import { BarChart3, CheckCircle, AlertCircle, XCircle } from 'lucide-react';

const ClassificationStats: React.FC = () => {
  const { t } = useTranslation();

  // TODO: Replace with actual API call
  const mockStats = {
    totalInvoicesProcessed: 150,
    successfulClassifications: 120,
    manualReviewRequired: 25,
    errors: 5,
    successRate: 80,
  };

  const stats = [
    {
      name: 'Celkem zpracováno',
      value: mockStats.totalInvoicesProcessed,
      icon: BarChart3,
      color: 'text-blue-600 dark:text-blue-400',
      bgColor: 'bg-blue-100 dark:bg-blue-900/30',
    },
    {
      name: 'Úspěšné',
      value: mockStats.successfulClassifications,
      icon: CheckCircle,
      color: 'text-green-600 dark:text-emerald-400',
      bgColor: 'bg-green-100 dark:bg-emerald-900/30',
    },
    {
      name: 'Ruční kontrola',
      value: mockStats.manualReviewRequired,
      icon: AlertCircle,
      color: 'text-yellow-600 dark:text-amber-400',
      bgColor: 'bg-yellow-100 dark:bg-amber-900/30',
    },
    {
      name: 'Chyby',
      value: mockStats.errors,
      icon: XCircle,
      color: 'text-red-600 dark:text-red-400',
      bgColor: 'bg-red-100 dark:bg-red-900/30',
    },
  ];

  return (
    <div className="bg-white dark:bg-graphite-surface border border-gray-200 dark:border-graphite-border rounded-lg p-6 shadow-sm dark:shadow-soft-dark">
      <h3 className="text-lg font-medium text-gray-900 dark:text-graphite-text mb-6">
        Statistiky klasifikace
      </h3>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6 mb-6">
        {stats.map((stat) => {
          const Icon = stat.icon;
          return (
            <div key={stat.name} className="text-center">
              <div className={`mx-auto w-12 h-12 ${stat.bgColor} rounded-lg flex items-center justify-center mb-3`}>
                <Icon className={`w-6 h-6 ${stat.color}`} />
              </div>
              <div className="text-2xl font-bold text-gray-900 dark:text-graphite-text mb-1">
                {stat.value.toLocaleString()}
              </div>
              <div className="text-sm text-gray-600 dark:text-graphite-muted">
                {stat.name}
              </div>
            </div>
          );
        })}
      </div>

      <div className="border-t border-gray-200 dark:border-graphite-border pt-6">
        <div className="flex items-center justify-between mb-2">
          <span className="text-sm font-medium text-gray-700 dark:text-graphite-muted">
            Úspěšnost
          </span>
          <span className="text-sm font-bold text-gray-900 dark:text-graphite-text">
            {mockStats.successRate}%
          </span>
        </div>
        <div className="w-full bg-gray-200 dark:bg-graphite-hover rounded-full h-2">
          <div 
            className="bg-green-600 h-2 rounded-full transition-all duration-300"
            style={{ width: `${mockStats.successRate}%` }}
          />
        </div>
      </div>

      <div className="mt-4 text-xs text-gray-500 dark:text-graphite-muted text-center">
        {t('invoiceClassification.stats.disclaimer', 'Statistics are updated after each classification run')}
      </div>
    </div>
  );
};

export default ClassificationStats;