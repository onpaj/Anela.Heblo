import React, { useState } from 'react';
import { AlertCircle, RefreshCw, CheckCircle, Clock, XCircle } from 'lucide-react';
import { useStockUpOperationsQuery, useRetryStockUpOperationMutation } from '../api/hooks/useStockUpOperations';
import { StockUpOperationState } from '../api/generated/api-client';
import { LoadingIndicator } from '../components/ui/LoadingIndicator';

const StockOperationsPage: React.FC = () => {
  const [selectedState, setSelectedState] = useState<StockUpOperationState | undefined>(StockUpOperationState.Failed);
  const [currentPage, setCurrentPage] = useState(1);
  const pageSize = 50;

  const { data, isLoading, error, refetch } = useStockUpOperationsQuery({
    state: selectedState,
    page: currentPage,
    pageSize: pageSize,
  });

  const retryMutation = useRetryStockUpOperationMutation();

  const getStateColor = (state: StockUpOperationState) => {
    switch (state) {
      case StockUpOperationState.Completed:
        return 'bg-green-100 text-green-800';
      case StockUpOperationState.Failed:
        return 'bg-red-100 text-red-800';
      case StockUpOperationState.Pending:
        return 'bg-yellow-100 text-yellow-800';
      case StockUpOperationState.Submitted:
        return 'bg-blue-100 text-blue-800';
      case StockUpOperationState.Verified:
        return 'bg-indigo-100 text-indigo-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  };

  const getStateIcon = (state: StockUpOperationState) => {
    switch (state) {
      case StockUpOperationState.Completed:
        return <CheckCircle className="h-4 w-4" />;
      case StockUpOperationState.Failed:
        return <XCircle className="h-4 w-4" />;
      case StockUpOperationState.Pending:
        return <Clock className="h-4 w-4" />;
      case StockUpOperationState.Submitted:
      case StockUpOperationState.Verified:
        return <RefreshCw className="h-4 w-4" />;
      default:
        return null;
    }
  };

  const handleRetry = async (operationId: number) => {
    if (window.confirm('Opravdu chcete znovu spustit tuto operaci?')) {
      try {
        await retryMutation.mutateAsync(operationId);
      } catch (error) {
        console.error('Chyba při opakování operace:', error);
      }
    }
  };

  const formatDate = (date?: string | Date | null) => {
    if (!date) return 'N/A';
    const dateObj = date instanceof Date ? date : new Date(date);
    return dateObj.toLocaleString('cs-CZ');
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <LoadingIndicator isVisible={true} />
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-4">
        <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded">
          Chyba při načítání operací: {(error as Error).message}
        </div>
      </div>
    );
  }

  const operations = data?.operations || [];
  const totalCount = data?.totalCount || 0;

  return (
    <div className="p-6">
      {/* Header */}
      <div className="flex items-center justify-between mb-6">
        <div className="flex items-center space-x-3">
          <AlertCircle className="h-8 w-8 text-gray-700" />
          <div>
            <h1 className="text-2xl font-bold text-gray-900">Stock-up operace</h1>
            <p className="text-sm text-gray-500">Přehled operací naskladnění do Shoptet</p>
          </div>
        </div>

        <button
          onClick={() => refetch()}
          className="inline-flex items-center px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white text-sm font-medium rounded-md transition-colors duration-200"
        >
          <RefreshCw className="h-4 w-4 mr-2" />
          Obnovit
        </button>
      </div>

      {/* Filters */}
      <div className="mb-4 flex space-x-4">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">
            Filtrovat podle stavu:
          </label>
          <select
            value={selectedState || ''}
            onChange={(e) => {
              const value = e.target.value;
              if (value) {
                setSelectedState(value as StockUpOperationState);
              } else {
                setSelectedState(undefined);
              }
              setCurrentPage(1);
            }}
            className="px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500"
          >
            <option value="">Všechny</option>
            <option value={StockUpOperationState.Failed}>Failed</option>
            <option value={StockUpOperationState.Pending}>Pending</option>
            <option value={StockUpOperationState.Submitted}>Submitted</option>
            <option value={StockUpOperationState.Verified}>Verified</option>
            <option value={StockUpOperationState.Completed}>Completed</option>
          </select>
        </div>
      </div>

      {/* Results count */}
      <div className="mb-4 text-sm text-gray-600">
        Celkem operací: {totalCount}
      </div>

      {/* Table */}
      {operations.length === 0 ? (
        <div className="bg-gray-50 border border-gray-200 rounded-lg p-8 text-center">
          <p className="text-gray-500">Žádné operace nenalezeny</p>
        </div>
      ) : (
        <div className="bg-white shadow-md rounded-lg overflow-hidden">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  ID
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Číslo dokladu
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Kód produktu
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Množství
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Stav
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Vytvořeno
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Chybová zpráva
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Akce
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {operations.map((operation) => (
                <tr key={operation.id} className="hover:bg-gray-50">
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                    {operation.id}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                    {operation.documentNumber}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                    {operation.productCode}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                    {operation.amount}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap">
                    <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${getStateColor(operation.state ?? StockUpOperationState.Pending)}`}>
                      {getStateIcon(operation.state ?? StockUpOperationState.Pending)}
                      <span className="ml-1">{StockUpOperationState[operation.state ?? StockUpOperationState.Pending]}</span>
                    </span>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {formatDate(operation.createdAt)}
                  </td>
                  <td className="px-6 py-4 text-sm text-red-600 max-w-xs truncate">
                    {operation.errorMessage || '-'}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm">
                    {(operation.state ?? StockUpOperationState.Pending) === StockUpOperationState.Failed && operation.id && (
                      <button
                        onClick={() => handleRetry(operation.id!)}
                        disabled={retryMutation.isPending}
                        className="inline-flex items-center px-3 py-1 bg-orange-600 hover:bg-orange-700 disabled:bg-gray-400 text-white text-xs font-medium rounded transition-colors duration-200"
                      >
                        <RefreshCw className="h-3 w-3 mr-1" />
                        Opakovat
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Pagination */}
      {totalCount > pageSize && (
        <div className="mt-4 flex justify-between items-center">
          <button
            onClick={() => setCurrentPage(Math.max(1, currentPage - 1))}
            disabled={currentPage === 1}
            className="px-4 py-2 bg-gray-200 hover:bg-gray-300 disabled:bg-gray-100 disabled:text-gray-400 text-gray-700 text-sm font-medium rounded-md transition-colors duration-200"
          >
            Předchozí
          </button>
          <span className="text-sm text-gray-600">
            Stránka {currentPage} z {Math.ceil(totalCount / pageSize)}
          </span>
          <button
            onClick={() => setCurrentPage(currentPage + 1)}
            disabled={currentPage >= Math.ceil(totalCount / pageSize)}
            className="px-4 py-2 bg-gray-200 hover:bg-gray-300 disabled:bg-gray-100 disabled:text-gray-400 text-gray-700 text-sm font-medium rounded-md transition-colors duration-200"
          >
            Další
          </button>
        </div>
      )}
    </div>
  );
};

export default StockOperationsPage;
