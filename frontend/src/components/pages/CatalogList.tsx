import React, { useState } from 'react';
import { Search, Filter, AlertCircle, Loader2, ChevronUp, ChevronDown, ChevronLeft, ChevronRight } from 'lucide-react';
import { 
  useCatalogQuery, 
  ProductType,
  CatalogItemDto
} from '../../api/hooks/useCatalog';
import CatalogDetail from './CatalogDetail';

const productTypeLabels: Record<ProductType, string> = {
  [ProductType.Product]: 'Produkt',
  [ProductType.Goods]: 'Zboží',
  [ProductType.Material]: 'Materiál',
  [ProductType.SemiProduct]: 'Polotovar',
  [ProductType.UNDEFINED]: 'Nedefinováno',
};

const CatalogList: React.FC = () => {
  
  // Filter states - separate input values from applied filters
  const [productNameInput, setProductNameInput] = useState('');
  const [productCodeInput, setProductCodeInput] = useState('');
  const [productNameFilter, setProductNameFilter] = useState('');
  const [productCodeFilter, setProductCodeFilter] = useState('');
  const [productTypeFilter, setProductTypeFilter] = useState<ProductType | ''>('');
  
  // Pagination states
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  
  // Sorting states
  const [sortBy, setSortBy] = useState<string>('');
  const [sortDescending, setSortDescending] = useState(false);

  // Modal states
  const [selectedItem, setSelectedItem] = useState<CatalogItemDto | null>(null);
  const [isDetailModalOpen, setIsDetailModalOpen] = useState(false);

  // Use the actual API call
  const { data, isLoading: loading, error, refetch } = useCatalogQuery(
    productNameFilter,
    productCodeFilter,
    productTypeFilter,
    pageNumber,
    pageSize,
    sortBy,
    sortDescending
  );

  const filteredItems = data?.items || [];
  const totalCount = data?.totalCount || 0; // Total count from API
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
    setProductTypeFilter('');
    setPageNumber(1); // Reset to first page when clearing filters
    
    // Force data reload by refetching
    await refetch();
  };

  // Sorting handler
  const handleSort = (column: string) => {
    if (sortBy === column) {
      setSortDescending(!sortDescending);
    } else {
      setSortBy(column);
      setSortDescending(false);
    }
    // React Query will automatically refetch when sortBy or sortDescending changes
  };

  // Pagination handlers
  const handlePageChange = (newPage: number) => {
    if (newPage >= 1 && newPage <= totalPages) {
      setPageNumber(newPage);
      // React Query will automatically refetch when pageNumber changes
    }
  };

  const handlePageSizeChange = (newPageSize: number) => {
    setPageSize(newPageSize);
    setPageNumber(1); // Reset to first page when changing page size
    // React Query will automatically refetch when pageSize changes
  };

  // Reset pagination when type filter changes
  React.useEffect(() => {
    setPageNumber(1);
    // Refetch will be triggered automatically by the query dependency change
  }, [productTypeFilter]);

  // Modal handlers
  const handleItemClick = (item: CatalogItemDto) => {
    setSelectedItem(item);
    setIsDetailModalOpen(true);
  };

  const handleCloseDetail = () => {
    setIsDetailModalOpen(false);
    setSelectedItem(null);
  };

  // Sortable header component
  const SortableHeader: React.FC<{ column: string; children: React.ReactNode }> = ({ column, children }) => {
    const isActive = sortBy === column;
    const isAscending = isActive && !sortDescending;
    const isDescending = isActive && sortDescending;

    return (
      <th
        scope="col"
        className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100 select-none"
        onClick={() => handleSort(column)}
      >
        <div className="flex items-center space-x-1">
          <span>{children}</span>
          <div className="flex flex-col">
            <ChevronUp
              className={`h-3 w-3 ${isAscending ? 'text-indigo-600' : 'text-gray-300'}`}
            />
            <ChevronDown
              className={`h-3 w-3 -mt-1 ${isDescending ? 'text-indigo-600' : 'text-gray-300'}`}
            />
          </div>
        </div>
      </th>
    );
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500">Načítání katalogu...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání katalogu: {error.message}</div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full">
      {/* Header - Fixed */}
      <div className="flex-shrink-0 mb-6">
        <h1 className="text-2xl font-semibold text-gray-900">Seznam produktů</h1>
        <p className="mt-1 text-sm text-gray-500">
          Přehled všech produktů v katalogu
        </p>
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
                  id="productName"
                  value={productNameInput}
                  onChange={(e) => setProductNameInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"
                  placeholder="Název produktu..."
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
              <select
                id="productType"
                value={productTypeFilter}
                onChange={(e) => setProductTypeFilter(e.target.value === '' ? '' : Number(e.target.value) as ProductType)}
                className="block w-full pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
              >
                <option value="">Všechny typy</option>
                {Object.entries(productTypeLabels).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
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
                <SortableHeader column="type">Typ</SortableHeader>
                <SortableHeader column="available">Dostupné</SortableHeader>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Umístění
                </th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  MOQ
                </th>
                <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  MMQ
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {filteredItems.map((item) => (
                <tr 
                  key={item.productCode} 
                  className="hover:bg-gray-50 cursor-pointer transition-colors duration-150"
                  onClick={() => handleItemClick(item)}
                  title="Klikněte pro zobrazení detailu"
                >
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                    {item.productCode}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                    {item.productName}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800">
                      {productTypeLabels[item.type]}
                    </span>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-center">
                    <span className="inline-flex items-center px-3 py-1 rounded-full text-sm font-semibold bg-green-100 text-green-800">
                      {Math.round(item.stock.available * 100) / 100}
                    </span>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {item.location}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {item.minimalOrderQuantity}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {item.minimalManufactureQuantity}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          
          {filteredItems.length === 0 && (
            <div className="text-center py-8">
              <p className="text-gray-500">Žádné produkty nebyly nalezeny.</p>
            </div>
          )}
        </div>
      </div>
      
      {/* Pagination - Fixed at bottom */}
      {totalCount > 0 && (
        <div className="flex-shrink-0 bg-white px-4 py-3 flex items-center justify-between border-t border-gray-200 sm:px-6 shadow-lg">
          <div className="flex-1 flex justify-between sm:hidden">
            <button
              onClick={() => handlePageChange(pageNumber - 1)}
              disabled={pageNumber <= 1}
              className="relative inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Předchozí
            </button>
            <button
              onClick={() => handlePageChange(pageNumber + 1)}
              disabled={pageNumber >= totalPages}
              className="ml-3 relative inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Další
            </button>
          </div>
          <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
            <div className="flex items-center space-x-2">
              <p className="text-sm text-gray-700">
                Zobrazeno{' '}
                <span className="font-medium">{Math.min((pageNumber - 1) * pageSize + 1, totalCount)}</span>
                {' '}až{' '}
                <span className="font-medium">{Math.min(pageNumber * pageSize, totalCount)}</span>
                {' '}z{' '}
                <span className="font-medium">{totalCount}</span>
                {' '}výsledků
                {productNameFilter || productCodeFilter ? (
                  <span className="text-gray-500"> (s aplikovanými filtry)</span>
                ) : ''}
              </p>
              <div className="flex items-center space-x-2">
                <label htmlFor="pageSize" className="text-sm text-gray-700">
                  Počet na stránku:
                </label>
                <select
                  id="pageSize"
                  value={pageSize}
                  onChange={(e) => handlePageSizeChange(Number(e.target.value))}
                  className="border-gray-300 rounded-md text-sm"
                >
                  <option value={10}>10</option>
                  <option value={20}>20</option>
                  <option value={50}>50</option>
                  <option value={100}>100</option>
                </select>
              </div>
            </div>
            <div>
              <nav className="relative z-0 inline-flex rounded-md shadow-sm -space-x-px" aria-label="Pagination">
                <button
                  onClick={() => handlePageChange(pageNumber - 1)}
                  disabled={pageNumber <= 1}
                  className="relative inline-flex items-center px-2 py-2 rounded-l-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <span className="sr-only">Předchozí</span>
                  <ChevronLeft className="h-5 w-5" />
                </button>
                
                {/* Page numbers */}
                {Array.from({ length: Math.min(totalPages, 7) }, (_, i) => {
                  let pageNum: number;
                  if (totalPages <= 7) {
                    pageNum = i + 1;
                  } else if (pageNumber <= 4) {
                    pageNum = i + 1;
                  } else if (pageNumber >= totalPages - 3) {
                    pageNum = totalPages - 6 + i;
                  } else {
                    pageNum = pageNumber - 3 + i;
                  }
                  
                  return (
                    <button
                      key={pageNum}
                      onClick={() => handlePageChange(pageNum)}
                      className={`relative inline-flex items-center px-4 py-2 border text-sm font-medium ${
                        pageNum === pageNumber
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
                  className="relative inline-flex items-center px-2 py-2 rounded-r-md border border-gray-300 bg-white text-sm font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <span className="sr-only">Další</span>
                  <ChevronRight className="h-5 w-5" />
                </button>
              </nav>
            </div>
          </div>
        </div>
      )}

      {/* Modal for Product Detail */}
      <CatalogDetail 
        item={selectedItem}
        isOpen={isDetailModalOpen}
        onClose={handleCloseDetail}
      />
    </div>
  );
};

export default CatalogList;