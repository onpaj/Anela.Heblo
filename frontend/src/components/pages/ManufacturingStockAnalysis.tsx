import React, { useState, useMemo } from 'react';
import { Search, RefreshCw, Download, AlertTriangle, AlertCircle, CheckCircle, Package, Settings, ChevronLeft, ChevronRight, ChevronUp, ChevronDown, HelpCircle } from 'lucide-react';
import { 
  useManufacturingStockAnalysisQuery, 
  GetManufacturingStockAnalysisRequest, 
  TimePeriodFilter, 
  ManufacturingStockSortBy,
  ManufacturingStockSeverity,
  formatNumber,
  formatPercentage,
  calculateTimePeriodRange,
  getTimePeriodDisplayText
} from '../../api/hooks/useManufacturingStockAnalysis';
import { getAuthenticatedApiClient } from '../../api/client';
import CatalogDetail from './CatalogDetail';

const ManufacturingStockAnalysis: React.FC = () => {
  // State for filters
  const [filters, setFilters] = useState<GetManufacturingStockAnalysisRequest>({
    timePeriod: TimePeriodFilter.PreviousQuarter,
    productFamily: undefined,
    criticalItemsOnly: true,
    majorItemsOnly: true,
    adequateItemsOnly: false,
    unconfiguredOnly: false,
    searchTerm: '',
    pageNumber: 1,
    pageSize: 20,
    sortBy: ManufacturingStockSortBy.OverstockPercentage,
    sortDescending: false
  });

  // State for product detail modal
  const [selectedProductCode, setSelectedProductCode] = useState<string | null>(null);
  const [isDetailModalOpen, setIsDetailModalOpen] = useState(false);

  // State for collapsible sections
  const [isControlsCollapsed, setIsControlsCollapsed] = useState(false);

  // State for expandable rows (ProductFamily subgrids)
  const [expandedRows, setExpandedRows] = useState<Set<string>>(new Set());
  
  // State for loaded ProductFamily data
  const [subgridData, setSubgridData] = useState<Record<string, any[]>>({});
  const [loadingSubgrids, setLoadingSubgrids] = useState<Set<string>>(new Set());

  // Query for stock analysis data
  const { data, isLoading, error, isRefetching, refetch } = useManufacturingStockAnalysisQuery(filters);

  // Memoized data for performance
  const tableData = useMemo(() => data?.items || [], [data?.items]);
  const summary = useMemo(() => data?.summary, [data?.summary]);
  
  // Pagination calculations
  const totalCount = data?.totalCount || 0;
  const totalPages = Math.ceil(totalCount / filters.pageSize!);

  // Handler for filter changes
  const handleFilterChange = (newFilters: Partial<GetManufacturingStockAnalysisRequest>) => {
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
  const handleSort = (column: ManufacturingStockSortBy) => {
    setFilters(prev => ({
      ...prev,
      sortBy: column,
      sortDescending: prev.sortBy === column ? !prev.sortDescending : true,
      pageNumber: 1
    }));
  };

  // Handler for time period change
  const handleTimePeriodChange = (timePeriod: TimePeriodFilter) => {
    if (timePeriod === TimePeriodFilter.CustomPeriod) {
      const range = calculateTimePeriodRange(TimePeriodFilter.PreviousQuarter);
      handleFilterChange({ 
        timePeriod, 
        customFromDate: range.fromDate || undefined, 
        customToDate: range.toDate || undefined 
      });
    } else {
      handleFilterChange({ 
        timePeriod, 
        customFromDate: undefined, 
        customToDate: undefined 
      });
    }
  };

  // Export functionality (placeholder)
  const handleExport = () => {
    // TODO: Implement export functionality
    console.log('Export to CSV');
  };


  // Sortable header component
  const SortableHeader: React.FC<{ 
    column: ManufacturingStockSortBy; 
    children: React.ReactNode; 
    className?: string;
    style?: React.CSSProperties;
  }> = ({ column, children, className = "", style }) => {
    const isActive = filters.sortBy === column;
    const isAscending = isActive && !filters.sortDescending;
    const isDescending = isActive && filters.sortDescending;

    return (
      <th
        scope="col"
        className={`px-4 py-3 text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100 select-none ${className}`}
        style={style}
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
  const getRowColorClass = (severity: ManufacturingStockSeverity, isSubgridRow: boolean = false) => {
    // Subgrid rows have slightly darker background for better visual separation
    if (isSubgridRow) {
      switch (severity) {
        case ManufacturingStockSeverity.Critical:
          return 'bg-red-100 hover:bg-red-200';
        case ManufacturingStockSeverity.Major:
          return 'bg-orange-100 hover:bg-orange-200';
        case ManufacturingStockSeverity.Adequate:
          return 'bg-emerald-100 hover:bg-emerald-200';
        case ManufacturingStockSeverity.Unconfigured:
          return 'bg-gray-100 hover:bg-gray-200';
        default:
          return 'bg-gray-100 hover:bg-gray-200';
      }
    } else {
      switch (severity) {
        case ManufacturingStockSeverity.Critical:
          return 'bg-red-50 hover:bg-red-100';
        case ManufacturingStockSeverity.Major:
          return 'bg-orange-50 hover:bg-orange-100';
        case ManufacturingStockSeverity.Adequate:
          return 'bg-emerald-50 hover:bg-emerald-100';
        case ManufacturingStockSeverity.Unconfigured:
          return 'bg-gray-50 hover:bg-gray-100';
        default:
          return 'hover:bg-gray-50';
      }
    }
  };

  // Handle severity filter click from summary cards - checkbox behavior
  const handleSeverityFilterClick = (severity: ManufacturingStockSeverity) => {
    switch (severity) {
      case ManufacturingStockSeverity.Critical:
        handleFilterChange({ criticalItemsOnly: !filters.criticalItemsOnly });
        break;
      case ManufacturingStockSeverity.Major:
        handleFilterChange({ majorItemsOnly: !filters.majorItemsOnly });
        break;
      case ManufacturingStockSeverity.Adequate:
        handleFilterChange({ adequateItemsOnly: !filters.adequateItemsOnly });
        break;
      case ManufacturingStockSeverity.Unconfigured:
        handleFilterChange({ unconfiguredOnly: !filters.unconfiguredOnly });
        break;
    }
  };

  // Modal handlers for product detail
  const handleRowClick = (item: any, event?: React.MouseEvent) => {
    // Don't open detail if clicking expand button
    if (event?.target && (event.target as HTMLElement).closest('.expand-button')) {
      return;
    }
    setSelectedProductCode(item.code);
    setIsDetailModalOpen(true);
  };

  const handleCloseDetail = () => {
    setIsDetailModalOpen(false);
    setSelectedProductCode(null);
  };

  // Handler for expanding/collapsing rows
  const handleRowExpand = async (productFamily: string, currentProductCode: string) => {
    const newExpandedRows = new Set(expandedRows);
    
    if (expandedRows.has(productFamily)) {
      // Collapse row
      newExpandedRows.delete(productFamily);
      setExpandedRows(newExpandedRows);
    } else {
      // Expand row and load data if not already loaded
      newExpandedRows.add(productFamily);
      setExpandedRows(newExpandedRows);
      
      // Check if data is already loaded
      if (!subgridData[productFamily] && !loadingSubgrids.has(productFamily)) {
        // Start loading
        const newLoadingSubgrids = new Set(loadingSubgrids);
        newLoadingSubgrids.add(productFamily);
        setLoadingSubgrids(newLoadingSubgrids);
        
        try {
          // Fetch products for this ProductFamily
          const apiClient = getAuthenticatedApiClient();
          const relativeUrl = `/api/manufacturing-stock-analysis`;
          const params = new URLSearchParams();
          params.append('productFamily', productFamily);
          params.append('pageSize', '100'); // Get all products in family
          
          const queryString = params.toString();
          const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}?${queryString}`;
          
          const response = await (apiClient as any).http.fetch(fullUrl, {
            method: 'GET',
            headers: {
              'Accept': 'application/json'
            }
          });
          
          if (response.ok) {
            const data = await response.json();
            // Filter out the current product from subgrid
            const filteredItems = data.items.filter((item: any) => item.code !== currentProductCode);
            
            setSubgridData(prev => ({
              ...prev,
              [productFamily]: filteredItems
            }));
          }
        } catch (error) {
          console.error('Error loading product family data:', error);
        } finally {
          // Remove from loading set
          const newLoadingSubgrids = new Set(loadingSubgrids);
          newLoadingSubgrids.delete(productFamily);
          setLoadingSubgrids(newLoadingSubgrids);
        }
      }
    }
  };

  // Check if row should show expand button (all products with ProductFamily)
  const shouldShowExpandButton = (item: any) => {
    return !!item.productFamily;
  };

  // Subgrid component for ProductFamily products
  const ProductFamilySubgrid: React.FC<{ productFamily: string; isLoading: boolean }> = ({ productFamily, isLoading }) => {
    const items = subgridData[productFamily] || [];
    
    if (isLoading) {
      return (
        <tr className="bg-gray-50">
          <td colSpan={9} className="px-4 py-4">
            <div className="flex items-center justify-center">
              <RefreshCw className="h-4 w-4 animate-spin text-gray-400 mr-2" />
              <span className="text-sm text-gray-600">Načítání produktů stejné řady...</span>
            </div>
          </td>
        </tr>
      );
    }
    
    if (items.length === 0) {
      return (
        <tr className="bg-gray-50">
          <td colSpan={9} className="px-4 py-3">
            <div className="text-sm text-gray-500 text-center">
              Žádné další produkty v této řadě
            </div>
          </td>
        </tr>
      );
    }

    return (
      <>
        {items.map((subItem) => (
          <tr 
            key={`sub-${subItem.code}`}
            className={`${getRowColorClass(subItem.severity, true)} cursor-pointer transition-colors duration-150 border-l-8 border-indigo-300`}
            onClick={(e) => handleRowClick(subItem, e)}
            title="Klikněte pro zobrazení detailu produktu"
          >
            {/* Product Info - matching main table column width */}
            <td className="px-4 py-3 whitespace-nowrap" style={{ minWidth: '200px', width: '25%' }}>
              <div className="flex items-center">
                <div className="w-10 mr-2"></div>
                {/* Color strip based on severity */}
                <div className={`w-1 h-6 mr-2 rounded-sm ${getSeverityStripColor(subItem.severity)}`}></div>
                <div className="flex-1 min-w-0 pl-6">
                  <div className="text-xs text-gray-700 truncate font-medium">
                    {subItem.name}
                  </div>
                  <div className="text-xs text-gray-400">
                    {subItem.code}
                  </div>
                </div>
              </div>
            </td>

            {/* Current Stock */}
            <td className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-700" style={{ minWidth: '90px', width: '10%' }}>
              <div className="font-medium">{formatNumber(subItem.currentStock, 0)}</div>
            </td>

            {/* Sales in Period */}
            <td className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-700" style={{ minWidth: '100px', width: '12%' }}>
              {formatNumber(subItem.salesInPeriod, 0)}
            </td>

            {/* Daily Sales */}
            <td className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-700" style={{ minWidth: '100px', width: '12%' }}>
              {formatNumber(subItem.dailySalesRate, 2)}
            </td>

            {/* Optimal Days Setup */}
            <td className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-700" style={{ minWidth: '90px', width: '10%' }}>
              {subItem.optimalDaysSetup > 0 ? `${subItem.optimalDaysSetup} dní` : '—'}
            </td>

            {/* Stock Days Available */}
            <td className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-700" style={{ minWidth: '90px', width: '10%' }}>
              <div className="font-medium">
                {subItem.stockDaysAvailable > 999 ? '∞' : formatNumber(subItem.stockDaysAvailable, 0)}
              </div>
            </td>

            {/* Minimum Stock */}
            <td className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-700" style={{ minWidth: '90px', width: '10%' }}>
              {formatNumber(subItem.minimumStock, 0)}
            </td>

            {/* Overstock Percentage */}
            <td className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-700" style={{ minWidth: '90px', width: '10%' }}>
              {formatPercentage(subItem.overstockPercentage)}
            </td>

            {/* Batch Size */}
            <td className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-700" style={{ minWidth: '80px', width: '8%' }}>
              {subItem.batchSize || '—'}
            </td>
          </tr>
        ))}
      </>
    );
  };

  // Get color strip for product based on severity
  const getSeverityStripColor = (severity: ManufacturingStockSeverity) => {
    switch (severity) {
      case ManufacturingStockSeverity.Critical:
        // Red - Overstock < 100%
        return 'bg-red-500';
      case ManufacturingStockSeverity.Major:
        // Orange - Below minimum stock
        return 'bg-orange-500';
      case ManufacturingStockSeverity.Adequate:
        // Green - All conditions OK
        return 'bg-emerald-500';
      case ManufacturingStockSeverity.Unconfigured:
        // Gray - Missing configuration
        return 'bg-gray-400';
      default:
        return '';
    }
  };

  // Get time period tooltip
  const getTimePeriodTooltip = (timePeriod: TimePeriodFilter) => {
    if (timePeriod === TimePeriodFilter.CustomPeriod && filters.customFromDate && filters.customToDate) {
      return `${filters.customFromDate.toLocaleDateString('cs-CZ')} - ${filters.customToDate.toLocaleDateString('cs-CZ')}`;
    }
    
    const range = calculateTimePeriodRange(timePeriod);
    if (!range.fromDate || !range.toDate) {
      return getTimePeriodDisplayText(timePeriod);
    }
    return `${range.fromDate.toLocaleDateString('cs-CZ')} - ${range.toDate.toLocaleDateString('cs-CZ')}`;
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
        <h1 className="text-lg font-semibold text-gray-900">Řízení zásob ve výrobě</h1>
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
                          onClick={() => handleSeverityFilterClick(ManufacturingStockSeverity.Critical)}
                          className={`px-1 py-0.5 rounded transition-colors hover:bg-red-50 ${
                            filters.criticalItemsOnly ? 'bg-red-50 ring-1 ring-red-300' : ''
                          }`}
                          title="Nadsklad &lt; 100%"
                        >
                          <span className="text-red-600 font-medium">{summary.criticalCount}</span>
                        </button>
                        <button
                          onClick={() => handleSeverityFilterClick(ManufacturingStockSeverity.Major)}
                          className={`px-1 py-0.5 rounded transition-colors hover:bg-orange-50 ${
                            filters.majorItemsOnly ? 'bg-orange-50 ring-1 ring-orange-300' : ''
                          }`}
                          title="Pod minimální zásobou"
                        >
                          <span className="text-orange-600 font-medium">{summary.majorCount}</span>
                        </button>
                        <button
                          onClick={() => handleSeverityFilterClick(ManufacturingStockSeverity.Adequate)}
                          className={`px-1 py-0.5 rounded transition-colors hover:bg-emerald-50 ${
                            filters.adequateItemsOnly ? 'bg-emerald-50 ring-1 ring-emerald-300' : ''
                          }`}
                          title="OK"
                        >
                          <span className="text-green-600 font-medium">{summary.adequateCount}</span>
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
                          <span className="font-medium text-red-200">Červené:</span> Nadsklad &lt; 100%
                        </div>
                      </div>
                      <div className="flex items-start">
                        <div className="w-3 h-3 bg-orange-200 rounded-sm mr-2 mt-0.5 flex-shrink-0"></div>
                        <div>
                          <span className="font-medium text-orange-200">Oranžové:</span> Skladem &lt; minimální zásoba
                        </div>
                      </div>
                      <div className="flex items-start">
                        <div className="w-3 h-3 bg-gray-200 rounded-sm mr-2 mt-0.5 flex-shrink-0"></div>
                        <div>
                          <span className="font-medium text-gray-200">Šedé:</span> Nezkonfigurováno (chybí OptimalStockDaysSetup)
                        </div>
                      </div>
                      <div className="flex items-start">
                        <div className="w-3 h-3 bg-emerald-200 rounded-sm mr-2 mt-0.5 flex-shrink-0"></div>
                        <div>
                          <span className="font-medium text-emerald-200">Zelené:</span> Všechny podmínky OK
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
                    <div className="flex items-center px-2 py-1 rounded-md bg-gray-50">
                      <Package className="h-3 w-3 text-blue-500 mr-1" />
                      <span className="text-gray-600">Celkem:</span>
                      <span className="font-semibold text-gray-900 ml-1">{summary.totalProducts}</span>
                    </div>
                    
                    <button
                      onClick={() => handleSeverityFilterClick(ManufacturingStockSeverity.Critical)}
                      className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-red-50 ${
                        filters.criticalItemsOnly ? 'bg-red-50 ring-1 ring-red-300' : ''
                      }`}
                    >
                      <AlertTriangle className="h-3 w-3 text-red-500 mr-1" />
                      <span className="text-gray-600">Nadsklad &lt; 100%:</span>
                      <span className="font-semibold text-red-600 ml-1">{summary.criticalCount}</span>
                    </button>
                    
                    <button
                      onClick={() => handleSeverityFilterClick(ManufacturingStockSeverity.Major)}
                      className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-orange-50 ${
                        filters.majorItemsOnly ? 'bg-orange-50 ring-1 ring-orange-300' : ''
                      }`}
                    >
                      <AlertCircle className="h-3 w-3 text-orange-500 mr-1" />
                      <span className="text-gray-600">Pod min. zásobou:</span>
                      <span className="font-semibold text-orange-600 ml-1">{summary.majorCount}</span>
                    </button>
                    
                    <button
                      onClick={() => handleSeverityFilterClick(ManufacturingStockSeverity.Adequate)}
                      className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-emerald-50 ${
                        filters.adequateItemsOnly ? 'bg-emerald-50 ring-1 ring-emerald-300' : ''
                      }`}
                    >
                      <CheckCircle className="h-3 w-3 text-green-500 mr-1" />
                      <span className="text-gray-600">OK:</span>
                      <span className="font-semibold text-green-600 ml-1">{summary.adequateCount}</span>
                    </button>
                    
                    <button
                      onClick={() => handleSeverityFilterClick(ManufacturingStockSeverity.Unconfigured)}
                      className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-gray-50 ${
                        filters.unconfiguredOnly ? 'bg-gray-50 ring-1 ring-gray-300' : ''
                      }`}
                    >
                      <Settings className="h-3 w-3 text-gray-500 mr-1" />
                      <span className="text-gray-600">Nezkonfigurováno:</span>
                      <span className="font-semibold text-gray-600 ml-1">{summary.unconfiguredCount}</span>
                    </button>
                  </div>
                </div>
              )}
              
              {/* Filters */}
              <div>
                <h3 className="text-xs font-medium text-gray-700 mb-2">Filtry</h3>
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-3">
                  {/* Time Period Selection */}
                  <div>
                    <label className="block text-xs font-medium text-gray-700 mb-1">
                      Časové období
                    </label>
                    <select
                      value={filters.timePeriod || TimePeriodFilter.PreviousQuarter}
                      onChange={(e) => handleTimePeriodChange(e.target.value as TimePeriodFilter)}
                      className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                      title={getTimePeriodTooltip(filters.timePeriod || TimePeriodFilter.PreviousQuarter)}
                    >
                      <option value={TimePeriodFilter.PreviousQuarter}>Minulý kvartal</option>
                      <option value={TimePeriodFilter.FutureQuarter}>Budoucí kvartal</option>
                      <option value={TimePeriodFilter.Y2Y}>Y2Y (12 měsíců)</option>
                      <option value={TimePeriodFilter.PreviousSeason}>Předchozí sezona</option>
                      <option value={TimePeriodFilter.CustomPeriod}>Vlastní období</option>
                    </select>
                  </div>

                  {/* Custom Date Range (only show if CustomPeriod is selected) */}
                  {filters.timePeriod === TimePeriodFilter.CustomPeriod && (
                    <>
                      <div>
                        <label className="block text-xs font-medium text-gray-700 mb-1">
                          Od data
                        </label>
                        <input
                          type="date"
                          value={filters.customFromDate?.toISOString().split('T')[0] || ''}
                          onChange={(e) => handleFilterChange({ customFromDate: new Date(e.target.value) })}
                          className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                        />
                      </div>

                      <div>
                        <label className="block text-xs font-medium text-gray-700 mb-1">
                          Do data
                        </label>
                        <input
                          type="date"
                          value={filters.customToDate?.toISOString().split('T')[0] || ''}
                          onChange={(e) => handleFilterChange({ customToDate: new Date(e.target.value) })}
                          className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                        />
                      </div>
                    </>
                  )}

                  {/* Product Family */}
                  <div>
                    <label className="block text-xs font-medium text-gray-700 mb-1">
                      Produktová řada
                    </label>
                    <select
                      value={filters.productFamily || ''}
                      onChange={(e) => handleFilterChange({ productFamily: e.target.value || undefined })}
                      className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                    >
                      <option value="">Všechny</option>
                      {summary?.productFamilies.map((family) => (
                        <option key={family} value={family}>{family}</option>
                      ))}
                    </select>
                  </div>

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
                        placeholder="Kód, název, řada..."
                        className="pl-8 w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                      />
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
            <div className="flex-1 overflow-x-auto overflow-y-auto">
              <table className="w-full divide-y divide-gray-200" style={{ minWidth: '1200px' }}>
                <thead className="bg-gray-50 sticky top-0 z-10">
                  <tr>
                    <SortableHeader column={ManufacturingStockSortBy.ProductCode} className="text-left" style={{ minWidth: '200px', width: '25%' }}>
                      Produkt
                    </SortableHeader>
                    <SortableHeader column={ManufacturingStockSortBy.CurrentStock} className="text-right" style={{ minWidth: '90px', width: '10%' }}>
                      Skladem
                    </SortableHeader>
                    <SortableHeader column={ManufacturingStockSortBy.SalesInPeriod} className="text-right" style={{ minWidth: '100px', width: '12%' }}>
                      Prodeje období
                    </SortableHeader>
                    <SortableHeader column={ManufacturingStockSortBy.DailySales} className="text-right" style={{ minWidth: '100px', width: '12%' }}>
                      Prodeje/den
                    </SortableHeader>
                    <SortableHeader column={ManufacturingStockSortBy.OptimalDaysSetup} className="text-right" style={{ minWidth: '90px', width: '10%' }}>
                      Nadsklad
                    </SortableHeader>
                    <SortableHeader column={ManufacturingStockSortBy.StockDaysAvailable} className="text-right" style={{ minWidth: '90px', width: '10%' }}>
                      Zásoba dni
                    </SortableHeader>
                    <SortableHeader column={ManufacturingStockSortBy.MinimumStock} className="text-right" style={{ minWidth: '90px', width: '10%' }}>
                      Min zásoba
                    </SortableHeader>
                    <SortableHeader column={ManufacturingStockSortBy.OverstockPercentage} className="text-right" style={{ minWidth: '90px', width: '10%' }}>
                      Nadsklad %
                    </SortableHeader>
                    <SortableHeader column={ManufacturingStockSortBy.BatchSize} className="text-right" style={{ minWidth: '80px', width: '8%' }}>
                      ks/šarže
                    </SortableHeader>
                  </tr>
                </thead>
                <tbody className="bg-white divide-y divide-gray-200">
                  {tableData.map((item) => {
                    const hasSubItems = shouldShowExpandButton(item);
                    const isExpanded = item.productFamily && expandedRows.has(item.productFamily);
                    const isLoading = !!(item.productFamily && loadingSubgrids.has(item.productFamily));
                    
                    return (
                      <React.Fragment key={item.code}>
                        <tr 
                          className={`${getRowColorClass(item.severity)} cursor-pointer transition-colors duration-150`}
                          onClick={(e) => handleRowClick(item, e)}
                          title="Klikněte pro zobrazení detailu produktu"
                        >
                          {/* Product Info */}
                          <td className="px-4 py-3 whitespace-nowrap" style={{ minWidth: '200px', width: '25%' }}>
                            <div className="flex items-center">
                              {/* Expand/Collapse button */}
                              {hasSubItems ? (
                                <button
                                  className="expand-button flex-shrink-0 p-1 mr-2 text-gray-400 hover:text-gray-600 focus:outline-none"
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    handleRowExpand(item.productFamily!, item.code);
                                  }}
                                  title={isExpanded ? 'Skrýt ostatní produkty řady' : 'Zobrazit ostatní produkty řady'}
                                >
                                  {isExpanded ? (
                                    <ChevronDown className="h-4 w-4" />
                                  ) : (
                                    <ChevronRight className="h-4 w-4" />
                                  )}
                                </button>
                              ) : (
                                <div className="w-6 mr-2"></div>
                              )}
                              
                              {/* Color strip based on severity */}
                              <div className={`w-1 h-8 mr-2 rounded-sm ${getSeverityStripColor(item.severity)}`}></div>
                              <div className="flex-1 min-w-0">
                                {/* Product name first - main info */}
                                <div className="text-xs text-gray-900 truncate font-medium">
                                  {item.name}
                                </div>
                                {/* Product code second - smaller */}
                                <div className="text-xs text-gray-500">
                                  {item.code}
                                </div>
                                {item.productFamily && (
                                  <div className="text-xs text-gray-400">
                                    {item.productFamily}
                                  </div>
                                )}
                              </div>
                            </div>
                          </td>

                      {/* Current Stock */}
                      <td className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-900" style={{ minWidth: '90px', width: '10%' }}>
                        <div className="font-bold">{formatNumber(item.currentStock, 0)}</div>
                      </td>

                      {/* Sales in Period */}
                      <td className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-900" style={{ minWidth: '100px', width: '12%' }}>
                        {formatNumber(item.salesInPeriod, 0)}
                      </td>

                      {/* Daily Sales */}
                      <td className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-900" style={{ minWidth: '100px', width: '12%' }}>
                        <div>{formatNumber(item.dailySalesRate, 2)}</div>
                      </td>

                      {/* Optimal Days Setup */}
                      <td className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-900" style={{ minWidth: '90px', width: '10%' }}>
                        {item.optimalDaysSetup > 0 ? `${item.optimalDaysSetup} dní` : '—'}
                      </td>

                      {/* Stock Days Available */}
                      <td className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-900" style={{ minWidth: '90px', width: '10%' }}>
                        <div className="font-bold">
                          {item.stockDaysAvailable > 999 ? '∞' : formatNumber(item.stockDaysAvailable, 0)}
                        </div>
                      </td>

                      {/* Minimum Stock */}
                      <td className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-900" style={{ minWidth: '90px', width: '10%' }}>
                        {formatNumber(item.minimumStock, 0)}
                      </td>

                      {/* Overstock Percentage */}
                      <td className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-900" style={{ minWidth: '90px', width: '10%' }}>
                        {formatPercentage(item.overstockPercentage)}
                      </td>

                          {/* Batch Size */}
                          <td className="px-3 py-3 whitespace-nowrap text-right text-xs text-gray-900" style={{ minWidth: '80px', width: '8%' }}>
                            {item.batchSize || '—'}
                          </td>
                        </tr>
                        
                        {/* Subgrid for ProductFamily - only show if expanded */}
                        {hasSubItems && isExpanded && (
                          <ProductFamilySubgrid productFamily={item.productFamily!} isLoading={isLoading} />
                        )}
                      </React.Fragment>
                    );
                  })}
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

export default ManufacturingStockAnalysis;