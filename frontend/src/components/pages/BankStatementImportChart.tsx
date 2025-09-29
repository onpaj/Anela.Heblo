import React from "react";
import {
  AlertCircle,
  RefreshCw,
  CreditCard,
} from "lucide-react";
import {
  useBankStatementImportStatistics,
  BankStatementImportStatisticsDto,
} from "../../api/hooks/useBankStatements";
import { format, parseISO } from 'date-fns';
import { cs } from 'date-fns/locale';

interface BankStatementSimpleChartProps {
  data: BankStatementImportStatisticsDto[];
}

const BankStatementSimpleChart: React.FC<BankStatementSimpleChartProps> = ({ data }) => {
  const maxImports = Math.max(...data.map(d => d.importCount), 1);
  const maxItems = Math.max(...data.map(d => d.totalItemCount), 1);
  
  return (
    <div>
      <h3 className="text-lg font-medium text-gray-900 mb-4">
        Import banky - přehled posledních dní
      </h3>
      
      <div className="space-y-3">
        {data.slice(-30).reverse().map((stat, index) => {
          const date = parseISO(stat.date);
          const formattedDate = format(date, 'dd.MM.yyyy', { locale: cs });
          const dayName = format(date, 'EEE', { locale: cs });
          
          return (
            <div key={index} className="flex items-center gap-4">
              <div className="w-20 text-sm text-gray-600 flex-shrink-0">
                <div className="font-medium">{formattedDate}</div>
                <div className="text-xs text-gray-400 capitalize">{dayName}</div>
              </div>
              
              <div className="flex-1 space-y-2">
                {/* Import Count Bar */}
                <div className="flex items-center gap-2">
                  <div className="w-16 text-xs text-gray-500">Importy:</div>
                  <div className="flex-1 bg-gray-200 rounded-full h-4 relative">
                    <div
                      className="bg-indigo-600 h-4 rounded-full transition-all duration-300"
                      style={{
                        width: maxImports > 0 ? `${(stat.importCount / maxImports) * 100}%` : '0%'
                      }}
                    />
                    <span className="absolute inset-0 flex items-center justify-center text-xs font-medium text-white">
                      {stat.importCount}
                    </span>
                  </div>
                </div>
                
                {/* Total Item Count Bar */}
                <div className="flex items-center gap-2">
                  <div className="w-16 text-xs text-gray-500">Položky:</div>
                  <div className="flex-1 bg-gray-200 rounded-full h-4 relative">
                    <div
                      className="bg-green-600 h-4 rounded-full transition-all duration-300"
                      style={{
                        width: maxItems > 0 ? `${(stat.totalItemCount / maxItems) * 100}%` : '0%'
                      }}
                    />
                    <span className="absolute inset-0 flex items-center justify-center text-xs font-medium text-white">
                      {stat.totalItemCount}
                    </span>
                  </div>
                </div>
              </div>
            </div>
          );
        })}
      </div>
      
      {/* Legend */}
      <div className="mt-4 flex gap-4 text-sm">
        <div className="flex items-center gap-2">
          <div className="w-4 h-3 bg-indigo-600 rounded"></div>
          <span className="text-gray-600">Počet importů</span>
        </div>
        <div className="flex items-center gap-2">
          <div className="w-4 h-3 bg-green-600 rounded"></div>
          <span className="text-gray-600">Počet položek</span>
        </div>
      </div>
    </div>
  );
};

const BankStatementImportChart: React.FC = () => {
  // API query - no filters, just get last 30 days
  const { 
    data, 
    isLoading, 
    error, 
    refetch,
    isFetching
  } = useBankStatementImportStatistics({});

  // Calculate summary statistics
  const summaryStats = React.useMemo(() => {
    if (!data?.statistics) return null;

    const totalImports = data.statistics.reduce((sum, day) => sum + day.importCount, 0);
    const totalItems = data.statistics.reduce((sum, day) => sum + day.totalItemCount, 0);
    const avgDaily = Math.round(totalImports / data.statistics.length || 0);
    const maxDaily = Math.max(...data.statistics.map(day => day.importCount));
    const activeDays = data.statistics.filter(day => day.importCount > 0).length;

    return {
      totalImports,
      totalItems,
      avgDaily,
      maxDaily,
      activeDays,
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
            <CreditCard className="h-6 w-6 text-indigo-600" />
            Import banky
          </h1>
          <p className="text-gray-600 mt-1">
            Monitoring importu bankovních výpisů za posledních 30 dní
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

      {/* Summary statistics */}
      {summaryStats && (
        <div className="grid grid-cols-1 md:grid-cols-5 gap-4">
          <div className="bg-white rounded-lg border border-gray-200 p-4">
            <h3 className="text-sm font-medium text-gray-600">Celkem importů</h3>
            <p className="text-2xl font-bold text-gray-900 mt-1">
              {summaryStats.totalImports}
            </p>
          </div>
          
          <div className="bg-white rounded-lg border border-gray-200 p-4">
            <h3 className="text-sm font-medium text-gray-600">Celkem položek</h3>
            <p className="text-2xl font-bold text-gray-900 mt-1">
              {summaryStats.totalItems}
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
            <h3 className="text-sm font-medium text-gray-600">Aktivní dny</h3>
            <p className="text-2xl font-bold text-green-600 mt-1">
              {summaryStats.activeDays}
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
          <BankStatementSimpleChart
            data={data.statistics}
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
                • <strong>Import banky:</strong> Přehled množství naimportovaných výpisů za posledních 30 dní
              </p>
              <p>
                • <strong>Počet položek:</strong> Celkový počet transakcí ve všech importovaných výpisech
              </p>
              <p>
                • <strong>Aktivní dny:</strong> Dny, kdy proběhl alespoň jeden import bankovního výpisu
              </p>
              <p>
                • Graf zobrazuje posledních 30 dní včetně dnů s nulovým počtem importů
              </p>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default BankStatementImportChart;