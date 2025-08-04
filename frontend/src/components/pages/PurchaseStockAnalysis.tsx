import React, { useState, useMemo } from 'react';
import { Search, Filter, RefreshCw, Download, AlertTriangle, TrendingDown, CheckCircle, Package, Settings, ChevronLeft, ChevronRight, ChevronUp, ChevronDown, HelpCircle } from 'lucide-react';
import { 
  usePurchaseStockAnalysisQuery, 
  GetPurchaseStockAnalysisRequest, 
  StockStatusFilter, 
  StockAnalysisSortBy,
  getSeverityColorClass,
  getSeverityDisplayText,
  formatNumber,
  formatCurrency,
  StockSeverity
} from '../../api/hooks/usePurchaseStockAnalysis';
import { CatalogItemDto } from '../../api/hooks/useCatalog';
import CatalogDetail from './CatalogDetail';

const PurchaseStockAnalysis: React.FC = () => {
  // State for filters
  const [filters, setFilters] = useState<GetPurchaseStockAnalysisRequest>({
    fromDate: new Date(new Date().getFullYear() - 1, new Date().getMonth(), new Date().getDate()),
    toDate: new Date(),
    stockStatus: StockStatusFilter.All,
    onlyConfigured: false,
    searchTerm: '',
    pageNumber: 1,
    pageSize: 20,
    sortBy: StockAnalysisSortBy.StockEfficiency,
    sortDescending: false
  });

  // State for product detail modal
  const [selectedProduct, setSelectedProduct] = useState<CatalogItemDto | null>(null);
  const [isDetailModalOpen, setIsDetailModalOpen] = useState(false);

  // Query for stock analysis data
  const { data, isLoading, error, isRefetching, refetch } = usePurchaseStockAnalysisQuery(filters);

  // Memoized data for performance
  const tableData = useMemo(() => data?.items || [], [data?.items]);
  const summary = useMemo(() => data?.summary, [data?.summary]);
  
  // Pagination calculations
  const totalCount = data?.totalCount || 0;
  const totalPages = Math.ceil(totalCount / filters.pageSize!);

  // Handler for filter changes
  const handleFilterChange = (newFilters: Partial<GetPurchaseStockAnalysisRequest>) => {
    setFilters(prev => ({ ...prev, ...newFilters, pageNumber: 1 }));
  };

  // Handler for pagination
  const handlePageChange = (newPage: number) => {
    if (newPage >= 1 && newPage <= totalPages) {
      setFilters(prev => ({ ...prev, pageNumber: newPage }));
    }
  };

  // Handler for page size change
  const handlePageSizeChange = (newPageSize: number) => {
    setFilters(prev => ({ ...prev, pageSize: newPageSize, pageNumber: 1 }));
  };

  // Handler for sorting
  const handleSort = (column: StockAnalysisSortBy) => {
    setFilters(prev => ({
      ...prev,
      sortBy: column,
      sortDescending: prev.sortBy === column ? !prev.sortDescending : true,
      pageNumber: 1
    }));
  };

  // Export functionality (placeholder)
  const handleExport = () => {
    // TODO: Implement export functionality
    console.log('Export to CSV');
  };

  // Quick date range selectors
  const handleQuickDateRange = (type: 'last12months' | 'previousQuarter' | 'nextQuarter') => {
    const now = new Date();
    let fromDate: Date;
    let toDate: Date;

    switch (type) {
      case 'last12months':
        fromDate = new Date(now.getFullYear() - 1, now.getMonth(), now.getDate());
        toDate = new Date();
        break;
      
      case 'previousQuarter':
        // Previous quarter (3 months back)
        fromDate = new Date(now.getFullYear(), now.getMonth() - 3, 1);
        toDate = new Date(now.getFullYear(), now.getMonth(), 0); // Last day of previous month
        break;
      
      case 'nextQuarter':
        // Next quarter from previous year (3 months forward from same period last year)
        const lastYear = now.getFullYear() - 1;
        fromDate = new Date(lastYear, now.getMonth(), 1);
        toDate = new Date(lastYear, now.getMonth() + 3, 0); // Last day of the quarter
        break;
      
      default:
        return;
    }

    handleFilterChange({ fromDate, toDate });
  };

  // Get tooltip text for date range buttons
  const getDateRangeTooltip = (type: 'last12months' | 'previousQuarter' | 'nextQuarter') => {
    const now = new Date();
    let fromDate: Date;
    let toDate: Date;

    switch (type) {
      case 'last12months':
        fromDate = new Date(now.getFullYear() - 1, now.getMonth(), now.getDate());
        toDate = new Date();
        break;
      
      case 'previousQuarter':
        fromDate = new Date(now.getFullYear(), now.getMonth() - 3, 1);
        toDate = new Date(now.getFullYear(), now.getMonth(), 0);
        break;
      
      case 'nextQuarter':
        const lastYear = now.getFullYear() - 1;
        fromDate = new Date(lastYear, now.getMonth(), 1);
        toDate = new Date(lastYear, now.getMonth() + 3, 0);
        break;
      
      default:
        return '';
    }

    return `${fromDate.toLocaleDateString('cs-CZ')} - ${toDate.toLocaleDateString('cs-CZ')}`;
  };

  // Sortable header component
  const SortableHeader: React.FC<{ column: StockAnalysisSortBy; children: React.ReactNode; className?: string }> = ({ column, children, className = "" }) => {
    const isActive = filters.sortBy === column;
    const isAscending = isActive && !filters.sortDescending;
    const isDescending = isActive && filters.sortDescending;

    return (
      <th
        scope="col"
        className={`px-6 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100 select-none ${className}`}
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

  // Get row background color based on severity (subtle coloring)
  const getRowColorClass = (severity: StockSeverity) => {
    switch (severity) {
      case 'Critical':
        return 'bg-red-50/30 hover:bg-red-50/50';
      case 'Low':
        return 'bg-amber-50/30 hover:bg-amber-50/50';
      case 'Optimal':
        return 'bg-emerald-50/30 hover:bg-emerald-50/50';
      case 'Overstocked':
        return 'bg-blue-50/30 hover:bg-blue-50/50';
      case 'NotConfigured':
        return 'bg-gray-50/30 hover:bg-gray-50/50';
      default:
        return 'hover:bg-gray-50';
    }
  };

  // Handle status filter click from summary cards
  const handleStatusFilterClick = (status: StockStatusFilter) => {
    handleFilterChange({ stockStatus: status });
  };

  // Modal handlers for product detail
  const handleRowClick = (item: any) => {
    // Convert stock analysis item to catalog item format for the modal
    const catalogItem: CatalogItemDto = {
      productCode: item.productCode,
      productName: item.productName,
      type: item.productType === 'Material' ? 3 : item.productType === 'Goods' ? 1 : 0, // Use correct ProductType enum values
      stock: {
        available: item.availableStock,
        erp: item.availableStock,
        eshop: 0,
        transport: 0,
        reserve: 0
      },
      price: {
        currentSellingPrice: 0,
        currentPurchasePrice: item.lastPurchase?.unitPrice || 0
      },
      properties: {
        optimalStockDaysSetup: 0,
        stockMinSetup: item.minStockLevel || 0,
        batchSize: 0,
        seasonMonths: []
      },
      location: '', // Not available in stock analysis
      minimalOrderQuantity: item.minimalOrderQuantity || '',
      minimalManufactureQuantity: 0 // Not available in stock analysis - use number
    };
    
    setSelectedProduct(catalogItem);
    setIsDetailModalOpen(true);
  };

  const handleCloseDetail = () => {
    setIsDetailModalOpen(false);
    setSelectedProduct(null);
  };

  // Get color strip for product based on severity (only when not filtering by status)
  const getSeverityStripColor = (severity: StockSeverity) => {
    // Don't show strip when filtering by specific status
    if (filters.stockStatus !== StockStatusFilter.All) {
      return '';
    }
    
    switch (severity) {
      case 'Critical':
        return 'bg-red-500';
      case 'Low':
        return 'bg-amber-500';
      case 'Optimal':
        return 'bg-emerald-500';
      case 'Overstocked':
        return 'bg-blue-500';
      case 'NotConfigured':
        return 'bg-gray-400';
      default:
        return '';
    }
  };

  if (error) {
    return (
      <div className="min-h-screen bg-gray-50 px-4 py-8">
        <div className="max-w-7xl mx-auto">
          <div className="bg-red-50 border border-red-200 rounded-lg p-6">
            <div className="flex items-center">
              <AlertTriangle className="h-5 w-5 text-red-400 mr-2" />
              <h3 className="text-lg font-medium text-red-800">Chyba při načítání dat</h3>
            </div>
            <p className="mt-2 text-sm text-red-700">
              {error instanceof Error ? error.message : 'Neočekávaná chyba'}
            </p>
            <button
              onClick={() => refetch()}
              className="mt-4 bg-red-100 hover:bg-red-200 text-red-800 px-4 py-2 rounded-md text-sm font-medium"
            >
              Zkusit znovu
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex-1 bg-gray-50 px-4 py-6 flex flex-col min-h-0">
      <div className="flex-1 flex flex-col min-h-0">
        {/* Header */}
        <div className="mb-6">
          <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
            <div>
              <h1 className="text-2xl font-bold text-gray-900">Analýza skladových zásob</h1>
              <p className="mt-1 text-sm text-gray-600">
                Přehled skladových hladin a spotřeby materiálů a zboží
              </p>
            </div>
            <div className="flex items-center space-x-3 flex-shrink-0">
              <button
                onClick={() => refetch()}
                disabled={isRefetching}
                className="flex items-center px-3 py-2 border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50"
              >
                <RefreshCw className={`h-4 w-4 mr-2 ${isRefetching ? 'animate-spin' : ''}`} />
                Obnovit
              </button>
              <button
                onClick={handleExport}
                className="flex items-center px-3 py-2 border border-gray-300 rounded-md shadow-sm text-sm font-medium text-gray-700 bg-white hover:bg-gray-50"
              >
                <Download className="h-4 w-4 mr-2" />
                Export
              </button>
            </div>
          </div>
        </div>

        {/* Summary Cards - Clickable */}
        {summary && (
          <div className="bg-white rounded-lg shadow p-4 mb-6">
            <div className="flex items-center justify-between mb-2">
              <h3 className="text-sm font-medium text-gray-900">Přehled stavů zásob</h3>
              <div className="relative group">
                <HelpCircle className="h-4 w-4 text-gray-400 cursor-help" />
                <div className="absolute right-0 top-6 w-80 bg-gray-900 text-white text-xs rounded-lg p-3 opacity-0 group-hover:opacity-100 transition-opacity z-10 pointer-events-none">
                  <div className="space-y-2">
                    <div className="flex items-start">
                      <div className="w-3 h-3 bg-red-200 rounded-sm mr-2 mt-0.5 flex-shrink-0"></div>
                      <div>
                        <span className="font-medium text-red-200">Kritické:</span> Zásoby pod minimální hranicí NEBO pod 20% optimálních zásob
                      </div>
                    </div>
                    <div className="flex items-start">
                      <div className="w-3 h-3 bg-amber-200 rounded-sm mr-2 mt-0.5 flex-shrink-0"></div>
                      <div>
                        <span className="font-medium text-amber-200">Nízké:</span> Zásoby mezi 20-70% optimálních zásob
                      </div>
                    </div>
                    <div className="flex items-start">
                      <div className="w-3 h-3 bg-emerald-200 rounded-sm mr-2 mt-0.5 flex-shrink-0"></div>
                      <div>
                        <span className="font-medium text-emerald-200">Optimální:</span> Zásoby mezi 70-150% optimálních zásob
                      </div>
                    </div>
                    <div className="flex items-start">
                      <div className="w-3 h-3 bg-blue-200 rounded-sm mr-2 mt-0.5 flex-shrink-0"></div>
                      <div>
                        <span className="font-medium text-blue-200">Přeskladněno:</span> Zásoby nad 150% optimálních zásob
                      </div>
                    </div>
                    <div className="flex items-start">
                      <div className="w-3 h-3 bg-gray-200 rounded-sm mr-2 mt-0.5 flex-shrink-0"></div>
                      <div>
                        <span className="font-medium text-gray-200">Nezkonfigurováno:</span> Chybí nastavení minimálních a optimálních zásob
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>
            <div className="flex flex-wrap items-center justify-between gap-4 text-sm">
              <button
                onClick={() => handleStatusFilterClick(StockStatusFilter.All)}
                className={`flex items-center px-3 py-2 rounded-md transition-colors hover:bg-gray-100 ${
                  filters.stockStatus === StockStatusFilter.All ? 'bg-gray-100 ring-2 ring-gray-300' : ''
                }`}
              >
                <Package className="h-4 w-4 text-blue-500 mr-2" />
                <span className="text-gray-600">Celkem:</span>
                <span className="font-semibold text-gray-900 ml-1">{summary.totalProducts}</span>
              </button>
              
              <button
                onClick={() => handleStatusFilterClick(StockStatusFilter.Critical)}
                className={`flex items-center px-3 py-2 rounded-md transition-colors hover:bg-red-50 ${
                  filters.stockStatus === StockStatusFilter.Critical ? 'bg-red-50 ring-2 ring-red-300' : ''
                }`}
              >
                <AlertTriangle className="h-4 w-4 text-red-500 mr-2" />
                <span className="text-gray-600">Kritické:</span>
                <span className="font-semibold text-red-600 ml-1">{summary.criticalCount}</span>
              </button>
              
              <button
                onClick={() => handleStatusFilterClick(StockStatusFilter.Low)}
                className={`flex items-center px-3 py-2 rounded-md transition-colors hover:bg-amber-50 ${
                  filters.stockStatus === StockStatusFilter.Low ? 'bg-amber-50 ring-2 ring-amber-300' : ''
                }`}
              >
                <TrendingDown className="h-4 w-4 text-orange-500 mr-2" />
                <span className="text-gray-600">Nízké:</span>
                <span className="font-semibold text-orange-600 ml-1">{summary.lowStockCount}</span>
              </button>
              
              <button
                onClick={() => handleStatusFilterClick(StockStatusFilter.Optimal)}
                className={`flex items-center px-3 py-2 rounded-md transition-colors hover:bg-emerald-50 ${
                  filters.stockStatus === StockStatusFilter.Optimal ? 'bg-emerald-50 ring-2 ring-emerald-300' : ''
                }`}
              >
                <CheckCircle className="h-4 w-4 text-green-500 mr-2" />
                <span className="text-gray-600">Optimální:</span>
                <span className="font-semibold text-green-600 ml-1">{summary.optimalCount}</span>
              </button>
              
              <button
                onClick={() => handleStatusFilterClick(StockStatusFilter.Overstocked)}
                className={`flex items-center px-3 py-2 rounded-md transition-colors hover:bg-blue-50 ${
                  filters.stockStatus === StockStatusFilter.Overstocked ? 'bg-blue-50 ring-2 ring-blue-300' : ''
                }`}
              >
                <Package className="h-4 w-4 text-blue-500 mr-2" />
                <span className="text-gray-600">Přeskladněno:</span>
                <span className="font-semibold text-blue-600 ml-1">{summary.overstockedCount}</span>
              </button>
              
              <button
                onClick={() => handleStatusFilterClick(StockStatusFilter.NotConfigured)}
                className={`flex items-center px-3 py-2 rounded-md transition-colors hover:bg-gray-50 ${
                  filters.stockStatus === StockStatusFilter.NotConfigured ? 'bg-gray-50 ring-2 ring-gray-300' : ''
                }`}
              >
                <Settings className="h-4 w-4 text-gray-500 mr-2" />
                <span className="text-gray-600">Nezkonfigurováno:</span>
                <span className="font-semibold text-gray-600 ml-1">{summary.notConfiguredCount}</span>
              </button>
            </div>
          </div>
        )}

        {/* Filters */}
        <div className="bg-white rounded-lg shadow mb-6">
          <div className="p-4 border-b border-gray-200">
            <h2 className="text-base font-medium text-gray-900 mb-3">Filtry</h2>
            
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
              {/* Search */}
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Vyhledat
                </label>
                <div className="relative">
                  <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 h-4 w-4 text-gray-400" />
                  <input
                    type="text"
                    value={filters.searchTerm || ''}
                    onChange={(e) => handleFilterChange({ searchTerm: e.target.value })}
                    placeholder="Kód, název, dodavatel..."
                    className="pl-10 w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                  />
                </div>
              </div>

              {/* Date From */}
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Od data
                </label>
                <input
                  type="date"
                  value={filters.fromDate?.toISOString().split('T')[0] || ''}
                  onChange={(e) => handleFilterChange({ fromDate: new Date(e.target.value) })}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                />
              </div>

              {/* Date To */}
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Do data
                </label>
                <input
                  type="date"
                  value={filters.toDate?.toISOString().split('T')[0] || ''}
                  onChange={(e) => handleFilterChange({ toDate: new Date(e.target.value) })}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
                />
              </div>

              {/* Quick Date Range Selectors */}
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Rychlé volby
                </label>
                <div className="space-y-2">
                  <div className="flex gap-1">
                    <button
                      onClick={() => handleQuickDateRange('last12months')}
                      className="px-2 py-1 text-xs bg-gray-100 hover:bg-gray-200 text-gray-700 rounded border border-gray-300 transition-colors whitespace-nowrap"
                      title={getDateRangeTooltip('last12months')}
                    >
                      Y2Y
                    </button>
                    <button
                      onClick={() => handleQuickDateRange('previousQuarter')}
                      className="px-2 py-1 text-xs bg-gray-100 hover:bg-gray-200 text-gray-700 rounded border border-gray-300 transition-colors whitespace-nowrap"
                      title={getDateRangeTooltip('previousQuarter')}
                    >
                      PrevQ
                    </button>
                    <button
                      onClick={() => handleQuickDateRange('nextQuarter')}
                      className="px-2 py-1 text-xs bg-gray-100 hover:bg-gray-200 text-gray-700 rounded border border-gray-300 transition-colors whitespace-nowrap"
                      title={getDateRangeTooltip('nextQuarter')}
                    >
                      NextQ
                    </button>
                  </div>
                  <label className="flex items-center">
                    <input
                      type="checkbox"
                      checked={filters.onlyConfigured || false}
                      onChange={(e) => handleFilterChange({ onlyConfigured: e.target.checked })}
                      className="rounded border-gray-300 text-indigo-600 shadow-sm focus:border-indigo-300 focus:ring focus:ring-indigo-200 focus:ring-opacity-50"
                    />
                    <span className="ml-2 text-xs text-gray-700">Pouze konfigurované</span>
                  </label>
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* Results Table */}
        <div className="bg-white rounded-lg shadow overflow-hidden flex-1 flex flex-col min-h-0">
          <div className="px-4 py-3 border-b border-gray-200 flex-shrink-0">
            <div className="flex items-center justify-between">
              <h2 className="text-base font-medium text-gray-900">
                Výsledky analýzy
                {data && (
                  <span className="ml-2 text-sm text-gray-500">
                    ({data.totalCount} produktů)
                  </span>
                )}
              </h2>
              <div className="text-sm text-gray-500">
                Klikněte na záhlaví sloupce pro řazení
              </div>
            </div>
          </div>

          {isLoading ? (
            <div className="flex items-center justify-center py-12">
              <RefreshCw className="h-8 w-8 animate-spin text-gray-400" />
              <span className="ml-2 text-gray-600">Načítání dat...</span>
            </div>
          ) : tableData.length === 0 ? (
            <div className="flex items-center justify-center py-12">
              <div className="text-center">
                <Package className="h-12 w-12 text-gray-400 mx-auto mb-4" />
                <h3 className="text-lg font-medium text-gray-900 mb-2">Žádné výsledky</h3>
                <p className="text-gray-600">Zkuste upravit filtry nebo vyhledávací kritéria.</p>
              </div>
            </div>
          ) : (
            <div className="flex-1 overflow-auto">
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50">
                  <tr>
                    <SortableHeader column={StockAnalysisSortBy.ProductCode} className="text-left w-40">
                      Produkt
                    </SortableHeader>
                    <SortableHeader column={StockAnalysisSortBy.AvailableStock} className="text-right">
                      Skladem
                    </SortableHeader>
                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider hidden md:table-cell">
                      Min/Opt
                    </th>
                    <SortableHeader column={StockAnalysisSortBy.Consumption} className="text-right hidden lg:table-cell">
                      Spotřeba
                    </SortableHeader>
                    <SortableHeader column={StockAnalysisSortBy.StockEfficiency} className="text-right">
                      NS
                    </SortableHeader>
                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider hidden md:table-cell">
                      MOQ
                    </th>
                    <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider hidden xl:table-cell">
                      Dny
                    </th>
                    <SortableHeader column={StockAnalysisSortBy.LastPurchaseDate} className="text-left hidden lg:table-cell w-56">
                      Poslední nákup
                    </SortableHeader>
                  </tr>
                </thead>
                <tbody className="bg-white divide-y divide-gray-200">
                  {tableData.map((item) => (
                    <tr 
                      key={item.productCode} 
                      className={`${getRowColorClass(item.severity)} hover:bg-gray-50 cursor-pointer transition-colors duration-150`}
                      onClick={() => handleRowClick(item)}
                      title="Klikněte pro zobrazení detailu produktu"
                    >
                      {/* Product Info */}
                      <td className="px-6 py-4 whitespace-nowrap w-40">
                        <div className="flex items-center">
                          {/* Color strip based on severity (only when not filtering) */}
                          {getSeverityStripColor(item.severity) && (
                            <div className={`w-1 h-8 mr-3 rounded-sm ${getSeverityStripColor(item.severity)}`}></div>
                          )}
                          <div className="flex-1 min-w-0">
                            {/* Product name first - main info - wider display */}
                            <div className="text-sm text-gray-900 truncate">
                              {item.productName}
                            </div>
                            {/* Product code second - smaller */}
                            <div className="text-xs text-gray-500">
                              {item.productCode}
                            </div>
                            <div className="text-xs text-gray-500 md:hidden">
                              {item.productType}
                            </div>
                          </div>
                        </div>
                      </td>

                      {/* Available Stock (Skladem) */}
                      <td className="px-6 py-4 whitespace-nowrap text-right text-xs text-gray-900">
                        <div className="font-bold">{formatNumber(item.availableStock, 0)}</div>
                        <div className="text-xs text-gray-500 md:hidden">
                          {formatNumber(item.minStockLevel, 0)}/{formatNumber(item.optimalStockLevel, 0)}
                        </div>
                      </td>

                      {/* Min/Optimal - Hidden on mobile */}
                      <td className="px-6 py-4 whitespace-nowrap text-right text-xs text-gray-500 hidden md:table-cell">
                        <div>{formatNumber(item.minStockLevel, 0)}</div>
                        <div className="text-xs text-gray-400">
                          {formatNumber(item.optimalStockLevel, 0)}
                        </div>
                      </td>

                      {/* Consumption - Hidden on tablet and below */}
                      <td className="px-6 py-4 whitespace-nowrap text-right text-xs text-gray-900 hidden lg:table-cell">
                        <div>{formatNumber(item.consumptionInPeriod, 0)}</div>
                        <div className="text-xs text-gray-500">
                          {formatNumber(item.dailyConsumption, 2)}/den
                        </div>
                      </td>

                      {/* NS (Stock Efficiency) */}
                      <td className="px-6 py-4 whitespace-nowrap text-right text-xs text-gray-900">
                        <div className="font-bold">{formatNumber(item.stockEfficiencyPercentage, 1)}%</div>
                        <div className="text-xs text-gray-500 lg:hidden">
                          {formatNumber(item.consumptionInPeriod, 0)}/měs
                        </div>
                      </td>

                      {/* MOQ - Hidden on mobile */}
                      <td className="px-6 py-4 whitespace-nowrap text-right text-xs text-gray-900 hidden md:table-cell">
                        {item.minimalOrderQuantity || '—'}
                      </td>

                      {/* Days Until Stockout - Hidden on large and below */}
                      <td className="px-6 py-4 whitespace-nowrap text-right text-xs text-gray-900 hidden xl:table-cell">
                        {item.daysUntilStockout ? formatNumber(item.daysUntilStockout, 0) : '∞'}
                      </td>

                      {/* Last Purchase with quantity and price - Hidden on tablet and below */}
                      <td className="px-6 py-4 whitespace-nowrap text-xs text-gray-500 hidden lg:table-cell w-56">
                        {item.lastPurchase ? (
                          <div>
                            <div className="font-medium">{new Date(item.lastPurchase.date).toLocaleDateString('cs-CZ')}</div>
                            <div className="text-xs truncate max-w-20">
                              {item.lastPurchase.supplierName}
                            </div>
                            <div className="text-xs font-medium">
                              {formatNumber(item.lastPurchase.amount, 0)}ks @ {formatCurrency(item.lastPurchase.unitPrice)}
                            </div>
                          </div>
                        ) : (
                          <span className="text-gray-400">—</span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* Pagination - Fixed at bottom */}
          {totalCount > 0 && (
            <div className="flex-shrink-0 bg-white px-4 py-3 flex items-center justify-between border-t border-gray-200 sm:px-6 shadow-lg">
              <div className="flex-1 flex justify-between sm:hidden">
                <button
                  onClick={() => handlePageChange(filters.pageNumber! - 1)}
                  disabled={filters.pageNumber! <= 1}
                  className="relative inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  Předchozí
                </button>
                <button
                  onClick={() => handlePageChange(filters.pageNumber! + 1)}
                  disabled={filters.pageNumber! >= totalPages}
                  className="ml-3 relative inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  Další
                </button>
              </div>
              <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
                <div className="flex items-center space-x-2">
                  <p className="text-sm text-gray-700">
                    Zobrazeno <span className="font-medium">{((filters.pageNumber! - 1) * filters.pageSize!) + 1}</span> až{' '}
                    <span className="font-medium">
                      {Math.min(filters.pageNumber! * filters.pageSize!, totalCount)}
                    </span>{' '}
                    z <span className="font-medium">{totalCount}</span> výsledků
                  </p>
                  <div className="flex items-center space-x-2">
                    <label htmlFor="pageSize" className="text-sm text-gray-700">Zobrazit:</label>
                    <select
                      id="pageSize"
                      value={filters.pageSize}
                      onChange={(e) => handlePageSizeChange(Number(e.target.value))}
                      className="border border-gray-300 rounded-md px-2 py-1 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent"
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
                      onClick={() => handlePageChange(filters.pageNumber! - 1)}
                      disabled={filters.pageNumber! <= 1}
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
                      } else if (filters.pageNumber! <= 4) {
                        pageNum = i + 1;
                      } else if (filters.pageNumber! >= totalPages - 3) {
                        pageNum = totalPages - 6 + i;
                      } else {
                        pageNum = filters.pageNumber! - 3 + i;
                      }
                      
                      return (
                        <button
                          key={pageNum}
                          onClick={() => handlePageChange(pageNum)}
                          className={`relative inline-flex items-center px-4 py-2 border text-sm font-medium ${
                            pageNum === filters.pageNumber!
                              ? 'z-10 bg-indigo-50 border-indigo-500 text-indigo-600'
                              : 'bg-white border-gray-300 text-gray-500 hover:bg-gray-50'
                          }`}
                        >
                          {pageNum}
                        </button>
                      );
                    })}
                    
                    <button
                      onClick={() => handlePageChange(filters.pageNumber! + 1)}
                      disabled={filters.pageNumber! >= totalPages}
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
        </div>
      </div>

      {/* Product Detail Modal */}
      <CatalogDetail 
        item={selectedProduct}
        isOpen={isDetailModalOpen}
        onClose={handleCloseDetail}
        defaultTab="history"
      />
    </div>
  );
};

export default PurchaseStockAnalysis;