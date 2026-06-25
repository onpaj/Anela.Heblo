import React, { useState } from "react";
import {
  Calendar,
  BarChart,
  TrendingUp,
  AlertCircle,
} from "lucide-react";
import { useBankStatementImportStatistics } from "../../../api/hooks/useBankStatements";
import { BankStatementImportChart } from "../../charts/BankStatementImportChart";

const StatisticsTab: React.FC = () => {
  // Date filters - default to last 30 days
  const getDefaultEndDate = () => {
    return new Date().toISOString().split('T')[0];
  };
  
  const getDefaultStartDate = () => {
    const date = new Date();
    date.setDate(date.getDate() - 30);
    return date.toISOString().split('T')[0];
  };

  const [startDate, setStartDate] = useState(getDefaultStartDate());
  const [endDate, setEndDate] = useState(getDefaultEndDate());
  const [dateType, setDateType] = useState<"ImportDate" | "StatementDate">("ImportDate");
  const [viewType, setViewType] = useState<"ImportCount" | "TotalItemCount">("ImportCount");
  const [minimumThreshold, setMinimumThreshold] = useState(1);

  // Fetch statistics data
  const { data, isLoading, error } = useBankStatementImportStatistics({
    startDate,
    endDate,
    dateType,
  });

  // Memoize statistics to avoid dependency issues
  const statistics = React.useMemo(() => data?.statistics || [], [data?.statistics]);

  // Calculate summary statistics
  const summaryStats = React.useMemo(() => {
    if (!statistics.length) {
      return {
        totalImports: 0,
        totalItems: 0,
        avgImportsPerDay: 0,
        avgItemsPerDay: 0,
        daysWithIssues: 0,
      };
    }

    const totalImports = statistics.reduce((sum, stat) => sum + stat.importCount, 0);
    const totalItems = statistics.reduce((sum, stat) => sum + stat.totalItemCount, 0);
    const daysWithData = statistics.filter(stat => 
      (viewType === 'ImportCount' ? stat.importCount : stat.totalItemCount) > 0
    ).length;
    const daysWithIssues = statistics.filter(stat => 
      (viewType === 'ImportCount' ? stat.importCount : stat.totalItemCount) < minimumThreshold
    ).length;

    return {
      totalImports,
      totalItems,
      avgImportsPerDay: daysWithData > 0 ? totalImports / daysWithData : 0,
      avgItemsPerDay: daysWithData > 0 ? totalItems / daysWithData : 0,
      daysWithIssues,
    };
  }, [statistics, viewType, minimumThreshold]);

  const handleApplyFilters = () => {
    // Data will refetch automatically due to React Query dependencies
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <BarChart className="h-5 w-5 animate-pulse text-indigo-500" />
          <div className="text-gray-500 dark:text-graphite-muted">Načítání statistik...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600 dark:text-red-400">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání statistik: {(error as Error).message}</div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex-1 overflow-auto space-y-6">
      {/* Filter Controls */}
      <div className="bg-white shadow rounded-lg p-4 dark:bg-graphite-surface dark:shadow-soft-dark">
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-5 gap-4">
          {/* Date Range */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1 dark:text-graphite-muted">
              Od
            </label>
            <input
              type="date"
              value={startDate}
              onChange={(e) => setStartDate(e.target.value)}
              className="block w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:ring-indigo-500 focus:border-indigo-500 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1 dark:text-graphite-muted">
              Do
            </label>
            <input
              type="date"
              value={endDate}
              onChange={(e) => setEndDate(e.target.value)}
              className="block w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:ring-indigo-500 focus:border-indigo-500 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
            />
          </div>

          {/* Date Type Selection */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1 dark:text-graphite-muted">
              Datum podle
            </label>
            <select
              value={dateType}
              onChange={(e) => setDateType(e.target.value as "ImportDate" | "StatementDate")}
              className="block w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:ring-indigo-500 focus:border-indigo-500 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
            >
              <option value="ImportDate">Datum importu</option>
              <option value="StatementDate">Datum výpisu</option>
            </select>
          </div>

          {/* View Type Selection */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1 dark:text-graphite-muted">
              Metrika
            </label>
            <select
              value={viewType}
              onChange={(e) => setViewType(e.target.value as "ImportCount" | "TotalItemCount")}
              className="block w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:ring-indigo-500 focus:border-indigo-500 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
            >
              <option value="ImportCount">Počet importů</option>
              <option value="TotalItemCount">Počet položek</option>
            </select>
          </div>

          {/* Minimum Threshold */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1 dark:text-graphite-muted">
              Min. prah
            </label>
            <input
              type="number"
              min="0"
              value={minimumThreshold}
              onChange={(e) => setMinimumThreshold(Number(e.target.value))}
              className="block w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:ring-indigo-500 focus:border-indigo-500 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
            />
          </div>
        </div>

        <div className="mt-4">
          <button
            onClick={handleApplyFilters}
            className="flex items-center gap-2 px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors text-sm"
          >
            <Calendar className="h-4 w-4" />
            Použít filtry
          </button>
        </div>
      </div>

      {/* Summary Statistics Cards */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="bg-white shadow rounded-lg p-4 dark:bg-graphite-surface dark:shadow-soft-dark">
          <div className="flex items-center">
            <div className="flex-shrink-0">
              <TrendingUp className="h-8 w-8 text-blue-600" />
            </div>
            <div className="ml-4">
              <p className="text-sm font-medium text-gray-500 dark:text-graphite-muted">Celkem importů</p>
              <p className="text-2xl font-semibold text-gray-900 dark:text-graphite-text">
                {summaryStats.totalImports}
              </p>
            </div>
          </div>
        </div>

        <div className="bg-white shadow rounded-lg p-4 dark:bg-graphite-surface dark:shadow-soft-dark">
          <div className="flex items-center">
            <div className="flex-shrink-0">
              <BarChart className="h-8 w-8 text-green-600" />
            </div>
            <div className="ml-4">
              <p className="text-sm font-medium text-gray-500 dark:text-graphite-muted">Celkem položek</p>
              <p className="text-2xl font-semibold text-gray-900 dark:text-graphite-text">
                {summaryStats.totalItems}
              </p>
            </div>
          </div>
        </div>

        <div className="bg-white shadow rounded-lg p-4 dark:bg-graphite-surface dark:shadow-soft-dark">
          <div className="flex items-center">
            <div className="flex-shrink-0">
              <Calendar className="h-8 w-8 text-indigo-600" />
            </div>
            <div className="ml-4">
              <p className="text-sm font-medium text-gray-500 dark:text-graphite-muted">
                Průměr/den ({viewType === 'ImportCount' ? 'importy' : 'položky'})
              </p>
              <p className="text-2xl font-semibold text-gray-900 dark:text-graphite-text">
                {viewType === 'ImportCount' 
                  ? summaryStats.avgImportsPerDay.toFixed(1)
                  : summaryStats.avgItemsPerDay.toFixed(1)
                }
              </p>
            </div>
          </div>
        </div>

        <div className="bg-white shadow rounded-lg p-4 dark:bg-graphite-surface dark:shadow-soft-dark">
          <div className="flex items-center">
            <div className="flex-shrink-0">
              <AlertCircle className={`h-8 w-8 ${summaryStats.daysWithIssues > 0 ? 'text-red-600 dark:text-red-400' : 'text-gray-400 dark:text-graphite-faint'}`} />
            </div>
            <div className="ml-4">
              <p className="text-sm font-medium text-gray-500 dark:text-graphite-muted">Problémové dny</p>
              <p className={`text-2xl font-semibold ${summaryStats.daysWithIssues > 0 ? 'text-red-600 dark:text-red-400' : 'text-gray-900 dark:text-graphite-text'}`}>
                {summaryStats.daysWithIssues}
              </p>
            </div>
          </div>
        </div>
      </div>

      {/* Chart */}
      <div className="bg-white shadow rounded-lg p-6 dark:bg-graphite-surface dark:shadow-soft-dark">
        {statistics.length > 0 ? (
          <BankStatementImportChart
            data={statistics}
            viewType={viewType}
            dateType={dateType}
            minimumThreshold={minimumThreshold}
          />
        ) : (
          <div className="text-center py-8">
            <BarChart className="mx-auto h-12 w-12 text-gray-400 dark:text-graphite-faint" />
            <h3 className="mt-2 text-sm font-medium text-gray-900 dark:text-graphite-text">Žádná data</h3>
            <p className="mt-1 text-sm text-gray-500 dark:text-graphite-muted">
              Pro vybraný časový rozsah nejsou k dispozici žádná statistická data.
            </p>
          </div>
        )}
      </div>
    </div>
  );
};

export default StatisticsTab;