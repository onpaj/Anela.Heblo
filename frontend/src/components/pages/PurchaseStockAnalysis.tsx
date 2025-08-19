import React, { useState, useMemo } from 'react';
import { Search, RefreshCw, Download, AlertTriangle, TrendingDown, CheckCircle, Package, Settings, ChevronLeft, ChevronRight, ChevronUp, ChevronDown, HelpCircle } from 'lucide-react';
import { 
  usePurchaseStockAnalysisQuery, 
  GetPurchaseStockAnalysisRequest, 
  StockStatusFilter, 
  StockAnalysisSortBy,
  formatNumber,
  formatCurrency
} from '../../api/hooks/usePurchaseStockAnalysis';
import { StockSeverity } from '../../api/generated/api-client';
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
  const [selectedProductCode, setSelectedProductCode] = useState<string | null>(null);
  const [isDetailModalOpen, setIsDetailModalOpen] = useState(false);

  // State for collapsible sections
  const [isControlsCollapsed, setIsControlsCollapsed] = useState(false);

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
      case StockSeverity.Critical:
        return 'bg-red-50/30 hover:bg-red-50/50';
      case StockSeverity.Low:
        return 'bg-amber-50/30 hover:bg-amber-50/50';
      case StockSeverity.Optimal:
        return 'bg-emerald-50/30 hover:bg-emerald-50/50';
      case StockSeverity.Overstocked:
        return 'bg-blue-50/30 hover:bg-blue-50/50';
      case StockSeverity.NotConfigured:
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
    setSelectedProductCode(item.productCode);
    setIsDetailModalOpen(true);
  };

  const handleCloseDetail = () => {
    setIsDetailModalOpen(false);
    setSelectedProductCode(null);
  };

  // Get color strip for product based on severity (only when not filtering by status)
  const getSeverityStripColor = (severity: StockSeverity) => {
    // Don't show strip when filtering by specific status
    if (filters.stockStatus !== StockStatusFilter.All) {
      return '';
    }
    
    switch (severity) {
      case StockSeverity.Critical:
        return 'bg-red-500';
      case StockSeverity.Low:
        return 'bg-amber-500';
      case StockSeverity.Optimal:
        return 'bg-emerald-500';
      case StockSeverity.Overstocked:
        return 'bg-blue-500';
      case StockSeverity.NotConfigured:
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
    <div className="flex flex-col h-full w-full">
      {/* Header - Fixed */}
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900">Analýza skladových zásob</h1>
      </div>

      {/* Controls - Single Collapsible Block */}
      <div className="flex-shrink-0 bg-white rounded-lg shadow mb-4">
          <div className="p-3 border-b border-gray-200">
            <div className="flex items-center justify-between">
              <button
                onClick={() => setIsControlsCollapsed(!isControlsCollapsed)}
                className="flex items-center space-x-2 text-sm font-medium text-gray-900 hover:text-gray-700"
              >
                {isControlsCollapsed ? <ChevronRight className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
                <span>Filtry a nastavení</span>
                {summary && (
                  <span className="text-xs text-gray-500">({summary.totalProducts} produktů)</span>
                )}
              </button>
              
              <div className="flex items-center space-x-3">
                {/* Always visible controls when collapsed */}
                {isControlsCollapsed && (
                  <>
                    {/* Quick summary when collapsed - clickable */}
                    {summary && (
                      <div className="flex items-center space-x-2 text-xs">
                        <button
                          onClick={() => handleStatusFilterClick(StockStatusFilter.All)}
                          className={`px-1 py-0.5 rounded transition-colors hover:bg-gray-100 ${
                            filters.stockStatus === StockStatusFilter.All ? 'bg-gray-100 ring-1 ring-gray-300' : ''
                          }`}
                          title="Všechny produkty"
                        >
                          <span className="text-gray-700 font-medium">{summary.totalProducts}</span>
                        </button>
                        <span className="text-gray-400">|</span>
                        <button
                          onClick={() => handleStatusFilterClick(StockStatusFilter.Critical)}
                          className={`px-1 py-0.5 rounded transition-colors hover:bg-red-50 ${
                            filters.stockStatus === StockStatusFilter.Critical ? 'bg-red-50 ring-1 ring-red-300' : ''
                          }`}
                          title="Kritické zásoby"
                        >
                          <span className="text-red-600 font-medium">{summary.criticalCount}</span>
                        </button>
                        <button
                          onClick={() => handleStatusFilterClick(StockStatusFilter.Low)}
                          className={`px-1 py-0.5 rounded transition-colors hover:bg-amber-50 ${
                            filters.stockStatus === StockStatusFilter.Low ? 'bg-amber-50 ring-1 ring-amber-300' : ''
                          }`}
                          title="Nízké zásoby"
                        >
                          <span className="text-orange-600 font-medium">{summary.lowStockCount}</span>
                        </button>
                        <button
                          onClick={() => handleStatusFilterClick(StockStatusFilter.Optimal)}
                          className={`px-1 py-0.5 rounded transition-colors hover:bg-emerald-50 ${
                            filters.stockStatus === StockStatusFilter.Optimal ? 'bg-emerald-50 ring-1 ring-emerald-300' : ''
                          }`}
                          title="Optimální zásoby"
                        >
                          <span className="text-green-600 font-medium">{summary.optimalCount}</span>
                        </button>
                      </div>
                    )}
                    {/* Search field when collapsed */}
                    <div className="flex-1 max-w-xs">
                      <div className="relative">
                        <Search className="absolute left-2 top-1/2 transform -translate-y-1/2 h-3 w-3 text-gray-400" />
                        <input
                          type="text"
                          value={filters.searchTerm || ''}
                          onChange={(e) => handleFilterChange({ searchTerm: e.target.value })}
                          placeholder="Vyhledat..."
                          className="pl-7 w-full border border-gray-300 rounded-md px-2 py-1 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                        />
                      </div>
                    </div>
                  </>
                )}
                
                {/* Action buttons - always visible */}
                <button
                  onClick={() => refetch()}
                  disabled={isRefetching}
                  className="flex items-center px-2 py-1 border border-gray-300 rounded-md shadow-sm text-xs font-medium text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50"
                >
                  <RefreshCw className={`h-3 w-3 mr-1 ${isRefetching ? 'animate-spin' : ''}`} />
                  {isControlsCollapsed ? '' : 'Obnovit'}
                </button>
                <button
                  onClick={handleExport}
                  className="flex items-center px-2 py-1 border border-gray-300 rounded-md shadow-sm text-xs font-medium text-gray-700 bg-white hover:bg-gray-50"
                >
                  <Download className="h-3 w-3 mr-1" />
                  {isControlsCollapsed ? '' : 'Export'}
                </button>
                
                {/* Help */}
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
            </div>
          </div>
          
          {!isControlsCollapsed && (
            <div className="p-3 space-y-4">
              {/* Summary Cards */}
              {summary && (
                <div>
                  <h3 className="text-xs font-medium text-gray-700 mb-2">Přehled stavů zásob</h3>
                  <div className="flex flex-wrap items-center gap-2 text-xs">
                    <button
                      onClick={() => handleStatusFilterClick(StockStatusFilter.All)}
                      className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-gray-100 ${
                        filters.stockStatus === StockStatusFilter.All ? 'bg-gray-100 ring-1 ring-gray-300' : ''
                      }`}
                    >
                      <Package className="h-3 w-3 text-blue-500 mr-1" />
                      <span className="text-gray-600">Celkem:</span>
                      <span className="font-semibold text-gray-900 ml-1">{summary.totalProducts}</span>
                    </button>
                    
                    <button
                      onClick={() => handleStatusFilterClick(StockStatusFilter.Critical)}
                      className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-red-50 ${
                        filters.stockStatus === StockStatusFilter.Critical ? 'bg-red-50 ring-1 ring-red-300' : ''
                      }`}
                    >
                      <AlertTriangle className="h-3 w-3 text-red-500 mr-1" />
                      <span className="text-gray-600">Kritické:</span>
                      <span className="font-semibold text-red-600 ml-1">{summary.criticalCount}</span>
                    </button>
                    
                    <button
                      onClick={() => handleStatusFilterClick(StockStatusFilter.Low)}
                      className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-amber-50 ${
                        filters.stockStatus === StockStatusFilter.Low ? 'bg-amber-50 ring-1 ring-amber-300' : ''
                      }`}
                    >
                      <TrendingDown className="h-3 w-3 text-orange-500 mr-1" />
                      <span className="text-gray-600">Nízké:</span>
                      <span className="font-semibold text-orange-600 ml-1">{summary.lowStockCount}</span>
                    </button>
                    
                    <button
                      onClick={() => handleStatusFilterClick(StockStatusFilter.Optimal)}
                      className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-emerald-50 ${
                        filters.stockStatus === StockStatusFilter.Optimal ? 'bg-emerald-50 ring-1 ring-emerald-300' : ''
                      }`}
                    >
                      <CheckCircle className="h-3 w-3 text-green-500 mr-1" />
                      <span className="text-gray-600">Optimální:</span>
                      <span className="font-semibold text-green-600 ml-1">{summary.optimalCount}</span>
                    </button>
                    
                    <button
                      onClick={() => handleStatusFilterClick(StockStatusFilter.Overstocked)}
                      className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-blue-50 ${
                        filters.stockStatus === StockStatusFilter.Overstocked ? 'bg-blue-50 ring-1 ring-blue-300' : ''
                      }`}
                    >
                      <Package className="h-3 w-3 text-blue-500 mr-1" />
                      <span className="text-gray-600">Přeskladněno:</span>
                      <span className="font-semibold text-blue-600 ml-1">{summary.overstockedCount}</span>
                    </button>
                    
                    <button
                      onClick={() => handleStatusFilterClick(StockStatusFilter.NotConfigured)}
                      className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-gray-50 ${
                        filters.stockStatus === StockStatusFilter.NotConfigured ? 'bg-gray-50 ring-1 ring-gray-300' : ''
                      }`}
                    >
                      <Settings className="h-3 w-3 text-gray-500 mr-1" />
                      <span className="text-gray-600">Nezkonfigurováno:</span>
                      <span className="font-semibold text-gray-600 ml-1">{summary.notConfiguredCount}</span>
                    </button>
                  </div>
                </div>
              )}
              
              {/* Filters */}
              <div>
                <h3 className="text-xs font-medium text-gray-700 mb-2">Filtry</h3>
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-3">
                  {/* Search */}
                  <div>
                    <label className="block text-xs font-medium text-gray-700 mb-1">
                      Vyhledat
                    </label>
                    <div className="relative">
                      <Search className="absolute left-2 top-1/2 transform -translate-y-1/2 h-3 w-3 text-gray-400" />
                      <input
                        type="text"
                        value={filters.searchTerm || ''}
                        onChange={(e) => handleFilterChange({ searchTerm: e.target.value })}
                        placeholder="Kód, název, dodavatel..."
                        className="pl-8 w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                      />
                    </div>
                  </div>

                  {/* Date From */}
                  <div>
                    <label className="block text-xs font-medium text-gray-700 mb-1">
                      Od data
                    </label>
                    <input
                      type="date"
                      value={filters.fromDate?.toISOString().split('T')[0] || ''}
                      onChange={(e) => handleFilterChange({ fromDate: new Date(e.target.value) })}
                      className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                    />
                  </div>

                  {/* Date To */}
                  <div>
                    <label className="block text-xs font-medium text-gray-700 mb-1">
                      Do data
                    </label>
                    <input
                      type="date"
                      value={filters.toDate?.toISOString().split('T')[0] || ''}
                      onChange={(e) => handleFilterChange({ toDate: new Date(e.target.value) })}
                      className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                    />
                  </div>

                  {/* Quick Date Range Selectors */}
                  <div>
                    <label className="block text-xs font-medium text-gray-700 mb-1">
                      Rychlé volby
                    </label>
                    <div className="space-y-1.5">
                      <div className="flex gap-1">
                        <button
                          onClick={() => handleQuickDateRange('last12months')}
                          className="px-1.5 py-0.5 text-xs bg-gray-100 hover:bg-gray-200 text-gray-700 rounded border border-gray-300 transition-colors whitespace-nowrap"
                          title={getDateRangeTooltip('last12months')}
                        >
                          Y2Y
                        </button>
                        <button
                          onClick={() => handleQuickDateRange('previousQuarter')}
                          className="px-1.5 py-0.5 text-xs bg-gray-100 hover:bg-gray-200 text-gray-700 rounded border border-gray-300 transition-colors whitespace-nowrap"
                          title={getDateRangeTooltip('previousQuarter')}
                        >
                          PrevQ
                        </button>
                        <button
                          onClick={() => handleQuickDateRange('nextQuarter')}
                          className="px-1.5 py-0.5 text-xs bg-gray-100 hover:bg-gray-200 text-gray-700 rounded border border-gray-300 transition-colors whitespace-nowrap"
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
                        <span className="ml-1.5 text-xs text-gray-700">Pouze konfigurované</span>
                      </label>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>

      {/* Results Table */}
      <div className="flex-1 bg-white rounded-lg shadow overflow-hidden flex flex-col min-h-0">

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
                <thead className="bg-gray-50 sticky top-0 z-10">
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

          {/* Pagination - Compact */}
          {totalCount > 0 && (
            <div className="flex-shrink-0 bg-white px-3 py-2 flex items-center justify-between border-t border-gray-200 text-xs">
              <div className="flex-1 flex justify-between sm:hidden">
                <button
                  onClick={() => handlePageChange(filters.pageNumber! - 1)}
                  disabled={filters.pageNumber! <= 1}
                  className="relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  Předchozí
                </button>
                <button
                  onClick={() => handlePageChange(filters.pageNumber! + 1)}
                  disabled={filters.pageNumber! >= totalPages}
                  className="ml-2 relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  Další
                </button>
              </div>
              <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
                <div className="flex items-center space-x-3">
                  <p className="text-xs text-gray-600">
                    {((filters.pageNumber! - 1) * filters.pageSize!) + 1}-{Math.min(filters.pageNumber! * filters.pageSize!, totalCount)} z {totalCount}
                  </p>
                  <div className="flex items-center space-x-1">
                    <span className="text-xs text-gray-600">Zobrazit:</span>
                    <select
                      value={filters.pageSize}
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
                      onClick={() => handlePageChange(filters.pageNumber! - 1)}
                      disabled={filters.pageNumber! <= 1}
                      className="relative inline-flex items-center px-1 py-1 rounded-l border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                      <ChevronLeft className="h-3 w-3" />
                    </button>
                    
                    {/* Page numbers */}
                    {Array.from({ length: Math.min(totalPages, 5) }, (_, i) => {
                      let pageNum: number;
                      if (totalPages <= 5) {
                        pageNum = i + 1;
                      } else if (filters.pageNumber! <= 3) {
                        pageNum = i + 1;
                      } else if (filters.pageNumber! >= totalPages - 2) {
                        pageNum = totalPages - 4 + i;
                      } else {
                        pageNum = filters.pageNumber! - 2 + i;
                      }
                      
                      return (
                        <button
                          key={pageNum}
                          onClick={() => handlePageChange(pageNum)}
                          className={`relative inline-flex items-center px-2 py-1 border text-xs font-medium ${
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

      {/* Product Detail Modal */}
      <CatalogDetail 
        productCode={selectedProductCode}
        isOpen={isDetailModalOpen}
        onClose={handleCloseDetail}
        defaultTab="history"
      />
    </div>
  );
};

export default PurchaseStockAnalysis;