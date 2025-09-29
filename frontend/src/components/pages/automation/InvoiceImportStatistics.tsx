import React, { useState } from 'react';
import { AlertCircle, BarChart3, Calendar, RefreshCw } from 'lucide-react';
import { useInvoiceImportStatistics } from '../../../api/hooks/useInvoiceImportStatistics';
import { InvoiceImportChart } from '../../charts/InvoiceImportChart';

type DateTypeOption = 'InvoiceDate' | 'LastSyncTime';

/**
 * Page component for monitoring invoice import statistics
 * Shows daily invoice counts with visual indicators for problematic days
 */
const InvoiceImportStatistics: React.FC = () => {
  const [dateType, setDateType] = useState<DateTypeOption>('InvoiceDate');
  
  const { 
    data, 
    isLoading, 
    error, 
    refetch,
    isFetching
  } = useInvoiceImportStatistics({ 
    dateType
    // Note: daysBack not specified - let backend use configured default
  });

  const handleDateTypeChange = (newDateType: DateTypeOption) => {
    setDateType(newDateType);
  };

  // Calculate summary statistics
  const summaryStats = React.useMemo(() => {
    if (!data?.data) return null;

    const totalInvoices = data.data.reduce((sum, day) => sum + day.count, 0);
    const avgDaily = Math.round(totalInvoices / data.data.length);
    const problematicDays = data.data.filter(day => day.isBelowThreshold).length;
    const maxDaily = Math.max(...data.data.map(day => day.count));

    return {
      totalInvoices,
      avgDaily,
      problematicDays,
      maxDaily,
    };
  }, [data]);

  if (error) {
    return (
      <div className="p-6">
        <div className="bg-red-50 border border-red-200 rounded-lg p-4">
          <div className="flex items-center gap-3">
            <AlertCircle className="h-5 w-5 text-red-600" />
            <div>
              <h3 className="text-sm font-medium text-red-800">
                Chyba při načítání dat
              </h3>
              <p className="text-sm text-red-700 mt-1">
                {error instanceof Error ? error.message : 'Neočekávaná chyba'}
              </p>
            </div>
          </div>
          <button
            onClick={() => refetch()}
            className="mt-3 px-3 py-1 text-sm bg-red-100 text-red-800 rounded hover:bg-red-200 transition-colors"
          >
            Zkusit znovu
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="p-6 space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center gap-3">
            <BarChart3 className="h-6 w-6 text-indigo-600" />
            Import vydaných faktur
          </h1>
          <p className="text-gray-600 mt-1">
            Monitoring kvality importu faktur ze Shoptet
          </p>
        </div>
        
        <button
          onClick={() => refetch()}
          disabled={isFetching}
          className="flex items-center gap-2 px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors disabled:opacity-50"
        >
          <RefreshCw className={`h-4 w-4 ${isFetching ? 'animate-spin' : ''}`} />
          Obnovit
        </button>
      </div>

      {/* Date type selector */}
      <div className="bg-white rounded-lg border border-gray-200 p-4">
        <div className="flex items-center gap-3 mb-3">
          <Calendar className="h-5 w-5 text-gray-600" />
          <h3 className="font-medium text-gray-900">Zobrazení podle</h3>
        </div>
        
        <div className="flex gap-3">
          <button
            onClick={() => handleDateTypeChange('InvoiceDate')}
            className={`px-4 py-2 rounded-lg border transition-colors ${
              dateType === 'InvoiceDate'
                ? 'bg-indigo-50 border-indigo-200 text-indigo-700'
                : 'bg-white border-gray-200 text-gray-700 hover:bg-gray-50'
            }`}
          >
            Datum vystavení faktury
          </button>
          <button
            onClick={() => handleDateTypeChange('LastSyncTime')}
            className={`px-4 py-2 rounded-lg border transition-colors ${
              dateType === 'LastSyncTime'
                ? 'bg-indigo-50 border-indigo-200 text-indigo-700'
                : 'bg-white border-gray-200 text-gray-700 hover:bg-gray-50'
            }`}
          >
            Datum importu faktury
          </button>
        </div>
      </div>

      {/* Summary statistics */}
      {summaryStats && (
        <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
          <div className="bg-white rounded-lg border border-gray-200 p-4">
            <h3 className="text-sm font-medium text-gray-600">Celkem faktur</h3>
            <p className="text-2xl font-bold text-gray-900 mt-1">
              {summaryStats.totalInvoices}
            </p>
          </div>
          
          <div className="bg-white rounded-lg border border-gray-200 p-4">
            <h3 className="text-sm font-medium text-gray-600">Průměr/den</h3>
            <p className="text-2xl font-bold text-gray-900 mt-1">
              {summaryStats.avgDaily}
            </p>
          </div>
          
          <div className="bg-white rounded-lg border border-gray-200 p-4">
            <h3 className="text-sm font-medium text-gray-600">Maximum/den</h3>
            <p className="text-2xl font-bold text-gray-900 mt-1">
              {summaryStats.maxDaily}
            </p>
          </div>
          
          <div className="bg-white rounded-lg border border-gray-200 p-4">
            <h3 className="text-sm font-medium text-gray-600">Problémové dny</h3>
            <p className={`text-2xl font-bold mt-1 ${
              summaryStats.problematicDays > 0 ? 'text-red-600' : 'text-green-600'
            }`}>
              {summaryStats.problematicDays}
            </p>
          </div>
        </div>
      )}

      {/* Chart */}
      <div className="bg-white rounded-lg border border-gray-200 p-6">
        {isLoading ? (
          <div className="flex items-center justify-center h-80">
            <div className="text-center">
              <RefreshCw className="h-8 w-8 text-indigo-600 animate-spin mx-auto mb-4" />
              <p className="text-gray-600">Načítání dat...</p>
            </div>
          </div>
        ) : data ? (
          <InvoiceImportChart
            data={data.data}
            minimumThreshold={data.minimumThreshold}
            dateType={dateType}
          />
        ) : (
          <div className="flex items-center justify-center h-80">
            <div className="text-center">
              <AlertCircle className="h-8 w-8 text-gray-400 mx-auto mb-4" />
              <p className="text-gray-600">Žádná data k zobrazení</p>
            </div>
          </div>
        )}
      </div>

      {/* Info panel */}
      <div className="bg-blue-50 border border-blue-200 rounded-lg p-4">
        <div className="flex items-start gap-3">
          <div className="text-blue-600 mt-0.5">
            <svg className="h-5 w-5" fill="currentColor" viewBox="0 0 20 20">
              <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z" clipRule="evenodd" />
            </svg>
          </div>
          <div>
            <h3 className="text-sm font-medium text-blue-800 mb-1">
              O této statistice
            </h3>
            <div className="text-sm text-blue-700 space-y-1">
              <p>
                • <strong>Datum vystavení faktury:</strong> Seskupuje faktury podle data, kdy byly vystaveny ve Shoptet
              </p>
              <p>
                • <strong>Datum importu faktury:</strong> Seskupuje faktury podle data, kdy byly importovány do našeho systému
              </p>
              <p>
                • <strong>Problémové dny:</strong> Dny s počtem faktur pod nastaveným minimálním prahem
              </p>
              <p>
                • Graf zobrazuje konfigurovatelný počet posledních dní včetně dnů s nulovým počtem faktur
              </p>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default InvoiceImportStatistics;