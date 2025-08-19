import React, { useState } from 'react';
import { Search, Filter, AlertCircle, Loader2, ChevronUp, ChevronDown, ChevronLeft, ChevronRight } from 'lucide-react';
import { useProductMarginsQuery } from '../../api/hooks/useProductMargins';

const ProductMarginsList: React.FC = () => {
  
  // Filter states - separate input values from applied filters
  const [productNameInput, setProductNameInput] = useState('');
  const [productCodeInput, setProductCodeInput] = useState('');
  const [productNameFilter, setProductNameFilter] = useState('');
  const [productCodeFilter, setProductCodeFilter] = useState('');
  
  // Pagination states
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  
  // Sorting states
  const [sortBy, setSortBy] = useState<string>('');
  const [sortDescending, setSortDescending] = useState(false);

  // Use the API call
  const { data, isLoading: loading, error, refetch } = useProductMarginsQuery(
    productCodeFilter,
    productNameFilter,
    pageNumber,
    pageSize,
    sortBy,
    sortDescending
  );

  const filteredItems = data?.items || [];
  const totalCount = data?.totalCount || 0;
  const totalPages = Math.ceil(totalCount / pageSize);

  // Handler for applying filters on Enter
  const handleApplyFilters = async () => {
    setProductNameFilter(productNameInput);
    setProductCodeFilter(productCodeInput);
    setPageNumber(1); // Reset to first page when applying filters
    
    // Force data reload by refetching
    await refetch();
  };

  // Handler for Enter key press
  const handleKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === 'Enter') {
      handleApplyFilters();
    }
  };

  // Handler for clearing all filters
  const handleClearFilters = async () => {
    setProductNameInput('');
    setProductCodeInput('');
    setProductNameFilter('');
    setProductCodeFilter('');
    setPageNumber(1); // Reset to first page when clearing filters
    
    // Force data reload by refetching
    await refetch();
  };

  // Pagination handlers
  const handlePageChange = (newPage: number) => {
    if (newPage >= 1 && newPage <= totalPages) {
      setPageNumber(newPage);
    }
  };

  const handlePageSizeChange = (newPageSize: number) => {
    setPageSize(newPageSize);
    setPageNumber(1); // Reset to first page when changing page size
  };
  
  // Sorting handler
  const handleSort = (column: string) => {
    if (sortBy === column) {
      setSortDescending(!sortDescending);
    } else {
      setSortBy(column);
      setSortDescending(false);
    }
    setPageNumber(1); // Reset to first page when sorting
  };
  
  // Sortable header component
  const SortableHeader: React.FC<{ column: string; children: React.ReactNode; align?: 'left' | 'right' }> = ({ column, children, align = 'left' }) => {
    const isActive = sortBy === column;
    const isAscending = isActive && !sortDescending;
    const isDescending = isActive && sortDescending;

    return (
      <th
        scope="col"
        className={`px-6 py-3 text-${align} text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100 select-none`}
        onClick={() => handleSort(column)}
      >
        <div className={`flex items-center ${align === 'right' ? 'justify-end' : ''} space-x-1`}>
          {align === 'right' && (
            <div className="flex flex-col">
              <ChevronUp
                className={`h-3 w-3 ${isAscending ? 'text-indigo-600' : 'text-gray-300'}`}
              />
              <ChevronDown
                className={`h-3 w-3 -mt-1 ${isDescending ? 'text-indigo-600' : 'text-gray-300'}`}
              />
            </div>
          )}
          <span>{children}</span>
          {align === 'left' && (
            <div className="flex flex-col">
              <ChevronUp
                className={`h-3 w-3 ${isAscending ? 'text-indigo-600' : 'text-gray-300'}`}
              />
              <ChevronDown
                className={`h-3 w-3 -mt-1 ${isDescending ? 'text-indigo-600' : 'text-gray-300'}`}
              />
            </div>
          )}
        </div>
      </th>
    );
  };

  // Format currency
  const formatCurrency = (value?: number | null) => {
    if (value === null || value === undefined) return '-';
    if (!isFinite(value)) return '-'; // Handle Infinity, -Infinity, NaN
    return new Intl.NumberFormat('cs-CZ', {
      style: 'currency',
      currency: 'CZK',
      minimumFractionDigits: 0,
      maximumFractionDigits: 0,
    }).format(value);
  };

  // Format percentage
  const formatPercentage = (value?: number | null) => {
    if (value === null || value === undefined) return '-';
    if (!isFinite(value)) return '-'; // Handle Infinity, -Infinity, NaN
    return `${value.toFixed(2)}%`;
  };

  // Get margin color
  const getMarginColor = (margin?: number | null) => {
    if (margin === null || margin === undefined) return 'text-gray-500';
    if (!isFinite(margin)) return 'text-gray-500'; // Handle Infinity, -Infinity, NaN
    if (margin < 30) return 'text-red-600';
    if (margin < 50) return 'text-orange-600';
    if (margin < 80) return 'text-yellow-600';
    return 'text-green-600';
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500">Načítání marží produktů...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání marží: {error.message}</div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full">
      {/* Header - Fixed */}
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900">Marže produktů</h1>
      </div>

      {/* Filters - Fixed */}
      <div className="flex-shrink-0 bg-white shadow rounded-lg p-4 mb-4">
        <div className="flex items-center justify-between flex-wrap gap-3">
          <div className="flex items-center gap-3 flex-1 min-w-0">
            <div className="flex items-center">
              <Filter className="h-4 w-4 text-gray-400 mr-2" />
              <span className="text-sm font-medium text-gray-900">Filtry:</span>
            </div>
            
            <div className="flex-1 max-w-xs">
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Search className="h-4 w-4 text-gray-400" />
                </div>
                <input
                  type="text"
                  id="productCode"
                  value={productCodeInput}
                  onChange={(e) => setProductCodeInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"
                  placeholder="Kód produktu..."
                />
              </div>
            </div>

            <div className="flex-1 max-w-xs">
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Search className="h-4 w-4 text-gray-400" />
                </div>
                <input
                  type="text"
                  id="productName"
                  value={productNameInput}
                  onChange={(e) => setProductNameInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"
                  placeholder="Název produktu..."
                />
              </div>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={handleApplyFilters}
              className="bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200 text-sm"
            >
              Filtrovat
            </button>
            <button
              onClick={handleClearFilters}
              className="bg-gray-500 hover:bg-gray-600 text-white font-medium py-2 px-3 rounded-md transition-colors duration-200 text-sm"
            >
              Vymazat
            </button>
          </div>
        </div>
      </div>

      {/* Data Grid - Scrollable */}
      <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0">
        <div className="flex-1 overflow-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50 sticky top-0 z-10">
              <tr>
                <SortableHeader column="productCode">Kód produktu</SortableHeader>
                <SortableHeader column="productName">Název produktu</SortableHeader>
                <SortableHeader column="priceWithoutVat" align="right">Cena bez DPH</SortableHeader>
                <SortableHeader column="purchasePrice" align="right">Nákupní cena</SortableHeader>
                <th scope="col" className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Náklad průměr
                </th>
                <th scope="col" className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Marže průměr
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {filteredItems.map((item) => (
                <tr 
                  key={item.productCode} 
                  className="hover:bg-gray-50 transition-colors duration-150"
                >
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                    {item.productCode}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                    {item.productName}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-gray-900">
                    {formatCurrency(item.priceWithoutVat)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-gray-900">
                    {formatCurrency(item.purchasePrice)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-gray-600">
                    {formatCurrency((item.materialCost || 0) + (item.manufactureCost || 0))}
                  </td>
                  <td className={`px-6 py-4 whitespace-nowrap text-sm text-right font-semibold ${getMarginColor(item.averageMargin)}`}>
                    {formatPercentage(item.averageMargin)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

      </div>
      
      {/* Pagination - Compact */}
      {totalCount > 0 && (
        <div className="flex-shrink-0 bg-white px-3 py-2 flex items-center justify-between border-t border-gray-200 text-xs">
          <div className="flex-1 flex justify-between sm:hidden">
            <button
              onClick={() => handlePageChange(pageNumber - 1)}
              disabled={pageNumber <= 1}
              className="relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Předchozí
            </button>
            <button
              onClick={() => handlePageChange(pageNumber + 1)}
              disabled={pageNumber >= totalPages}
              className="ml-2 relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Další
            </button>
          </div>
          <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
            <div className="flex items-center space-x-3">
              <p className="text-xs text-gray-600">
                {Math.min((pageNumber - 1) * pageSize + 1, totalCount)}-{Math.min(pageNumber * pageSize, totalCount)} z {totalCount}
                {productNameFilter || productCodeFilter ? (
                  <span className="text-gray-500"> (filtrováno)</span>
                ) : ''}
              </p>
              <div className="flex items-center space-x-1">
                <span className="text-xs text-gray-600">Zobrazit:</span>
                <select
                  value={pageSize}
                  onChange={(e) => handlePageSizeChange(Number(e.target.value))}
                  className="border border-gray-300 rounded px-1 py-0.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                >
                  <option value={10}>10</option>
                  <option value={20}>20</option>
                  <option value={50}>50</option>
                  <option value={100}>100</option>
                </select>
              </div>
            </div>
            <div>
              <nav className="relative z-0 inline-flex rounded shadow-sm -space-x-px" aria-label="Pagination">
                <button
                  onClick={() => handlePageChange(pageNumber - 1)}
                  disabled={pageNumber <= 1}
                  className="relative inline-flex items-center px-1 py-1 rounded-l border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <ChevronLeft className="h-3 w-3" />
                </button>
                
                {/* Page numbers */}
                {Array.from({ length: Math.min(totalPages, 5) }, (_, i) => {
                  let pageNum: number;
                  if (totalPages <= 5) {
                    pageNum = i + 1;
                  } else if (pageNumber <= 3) {
                    pageNum = i + 1;
                  } else if (pageNumber >= totalPages - 2) {
                    pageNum = totalPages - 4 + i;
                  } else {
                    pageNum = pageNumber - 2 + i;
                  }
                  
                  return (
                    <button
                      key={pageNum}
                      onClick={() => handlePageChange(pageNum)}
                      className={`relative inline-flex items-center px-2 py-1 border text-xs font-medium ${
                        pageNumber === pageNum
                          ? 'z-10 bg-indigo-50 border-indigo-500 text-indigo-600'
                          : 'bg-white border-gray-300 text-gray-500 hover:bg-gray-50'
                      }`}
                    >
                      {pageNum}
                    </button>
                  );
                })}
                
                <button
                  onClick={() => handlePageChange(pageNumber + 1)}
                  disabled={pageNumber >= totalPages}
                  className="relative inline-flex items-center px-1 py-1 rounded-r border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <ChevronRight className="h-3 w-3" />
                </button>
              </nav>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default ProductMarginsList;