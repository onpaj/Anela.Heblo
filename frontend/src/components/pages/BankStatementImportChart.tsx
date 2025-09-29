import React, { useState } from "react";
import {
  Calendar,
  TrendingUp,
  Database,
  AlertCircle,
  Loader2,
} from "lucide-react";
import {
  useBankStatementImportStatistics,
  GetBankStatementImportStatisticsRequest,
} from "../../api/hooks/useBankStatements";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";

const BankStatementImportChart: React.FC = () => {
  // Date filter states
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");

  // Applied filters for the query
  const [appliedFilters, setAppliedFilters] = useState<GetBankStatementImportStatisticsRequest>({});

  // API query
  const { data, isLoading, error } = useBankStatementImportStatistics(appliedFilters);

  const handleApplyFilters = () => {
    setAppliedFilters({
      startDate: startDate || undefined,
      endDate: endDate || undefined,
    });
  };

  const handleClearFilters = () => {
    setStartDate("");
    setEndDate("");
    setAppliedFilters({});
  };

  const formatDate = (dateString: string) => {
    const date = new Date(dateString);
    return date.toLocaleDateString('cs-CZ');
  };

  const getMaxValue = () => {
    if (!data?.statistics || data.statistics.length === 0) return 0;
    return Math.max(...data.statistics.map(stat => Math.max(stat.importCount, stat.totalItemCount)));
  };

  const maxValue = getMaxValue();

  return (
    <div className="h-full flex flex-col bg-white">
      {/* Header */}
      <div className="flex-none border-b border-gray-200 px-6 py-4">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-2xl font-semibold text-gray-900">
              Statistiky importů bank statements
            </h1>
            <p className="mt-1 text-sm text-gray-500">
              Přehled importů bank statements podle dnů
            </p>
          </div>
        </div>
      </div>

      {/* Filter Bar */}
      <div className="flex-none border-b border-gray-200 px-6 py-4">
        <div className="flex flex-wrap items-center gap-4">
          <div className="flex items-center gap-2">
            <Calendar className="w-4 h-4 text-gray-400" />
            <label className="text-sm font-medium text-gray-700">Od:</label>
            <input
              type="date"
              value={startDate}
              onChange={(e) => setStartDate(e.target.value)}
              className="px-3 py-1.5 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
            />
          </div>

          <div className="flex items-center gap-2">
            <label className="text-sm font-medium text-gray-700">Do:</label>
            <input
              type="date"
              value={endDate}
              onChange={(e) => setEndDate(e.target.value)}
              className="px-3 py-1.5 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
            />
          </div>

          <button
            onClick={handleApplyFilters}
            className="px-4 py-1.5 bg-indigo-600 text-white text-sm font-medium rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
          >
            Aplikovat filtry
          </button>

          <button
            onClick={handleClearFilters}
            className="px-4 py-1.5 bg-gray-100 text-gray-700 text-sm font-medium rounded-md hover:bg-gray-200 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-gray-500"
          >
            Vymazat filtry
          </button>
        </div>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-hidden">
        <div className={PAGE_CONTAINER_HEIGHT}>
          <div className="h-full overflow-auto p-6">
            {isLoading && (
              <div className="flex items-center justify-center h-64">
                <div className="text-center">
                  <Loader2 className="w-8 h-8 text-indigo-600 animate-spin mx-auto" />
                  <p className="mt-2 text-sm text-gray-500">Načítání statistik...</p>
                </div>
              </div>
            )}

            {error && (
              <div className="flex items-center justify-center h-64">
                <div className="text-center">
                  <AlertCircle className="w-8 h-8 text-red-500 mx-auto" />
                  <p className="mt-2 text-sm text-red-600">
                    Chyba při načítání statistik: {error.message}
                  </p>
                </div>
              </div>
            )}

            {data && data.statistics && (
              <div className="space-y-6">
                {/* Summary Cards */}
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                  <div className="bg-white p-6 rounded-lg shadow border">
                    <div className="flex items-center">
                      <div className="flex-shrink-0">
                        <TrendingUp className="h-6 w-6 text-indigo-600" />
                      </div>
                      <div className="ml-3">
                        <p className="text-sm font-medium text-gray-500">Celkem importů</p>
                        <p className="text-lg font-semibold text-gray-900">
                          {data.statistics.reduce((sum, stat) => sum + stat.importCount, 0)}
                        </p>
                      </div>
                    </div>
                  </div>

                  <div className="bg-white p-6 rounded-lg shadow border">
                    <div className="flex items-center">
                      <div className="flex-shrink-0">
                        <Database className="h-6 w-6 text-green-600" />
                      </div>
                      <div className="ml-3">
                        <p className="text-sm font-medium text-gray-500">Celkem položek</p>
                        <p className="text-lg font-semibold text-gray-900">
                          {data.statistics.reduce((sum, stat) => sum + stat.totalItemCount, 0)}
                        </p>
                      </div>
                    </div>
                  </div>

                  <div className="bg-white p-6 rounded-lg shadow border">
                    <div className="flex items-center">
                      <div className="flex-shrink-0">
                        <Calendar className="h-6 w-6 text-blue-600" />
                      </div>
                      <div className="ml-3">
                        <p className="text-sm font-medium text-gray-500">Dnů s aktivitou</p>
                        <p className="text-lg font-semibold text-gray-900">
                          {data.statistics.length}
                        </p>
                      </div>
                    </div>
                  </div>
                </div>

                {/* Simple Bar Chart */}
                {data.statistics.length > 0 && (
                  <div className="bg-white p-6 rounded-lg shadow border">
                    <h3 className="text-lg font-medium text-gray-900 mb-4">
                      Graf importů podle dnů
                    </h3>
                    <div className="space-y-3">
                      {data.statistics.map((stat, index) => (
                        <div key={index} className="flex items-center space-x-4">
                          <div className="w-20 text-sm text-gray-600 flex-shrink-0">
                            {formatDate(stat.date)}
                          </div>
                          
                          <div className="flex-1 space-y-1">
                            {/* Import Count Bar */}
                            <div className="flex items-center">
                              <div className="w-16 text-xs text-gray-500 mr-2">Importy:</div>
                              <div className="flex-1 bg-gray-200 rounded-full h-4 relative">
                                <div
                                  className="bg-indigo-600 h-4 rounded-full transition-all duration-300"
                                  style={{
                                    width: maxValue > 0 ? `${(stat.importCount / maxValue) * 100}%` : '0%'
                                  }}
                                />
                                <span className="absolute inset-0 flex items-center justify-center text-xs font-medium text-white">
                                  {stat.importCount}
                                </span>
                              </div>
                            </div>
                            
                            {/* Total Item Count Bar */}
                            <div className="flex items-center">
                              <div className="w-16 text-xs text-gray-500 mr-2">Položky:</div>
                              <div className="flex-1 bg-gray-200 rounded-full h-4 relative">
                                <div
                                  className="bg-green-600 h-4 rounded-full transition-all duration-300"
                                  style={{
                                    width: maxValue > 0 ? `${(stat.totalItemCount / maxValue) * 100}%` : '0%'
                                  }}
                                />
                                <span className="absolute inset-0 flex items-center justify-center text-xs font-medium text-white">
                                  {stat.totalItemCount}
                                </span>
                              </div>
                            </div>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {data.statistics.length === 0 && (
                  <div className="text-center py-12">
                    <Database className="w-12 h-12 text-gray-400 mx-auto" />
                    <h3 className="mt-2 text-sm font-medium text-gray-900">Žádné statistiky</h3>
                    <p className="mt-1 text-sm text-gray-500">
                      Pro zadané období nejsou k dispozici žádné statistiky importů.
                    </p>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

export default BankStatementImportChart;