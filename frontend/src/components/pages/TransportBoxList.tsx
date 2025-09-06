import React, { useState } from 'react';
import { Search, AlertCircle, ChevronUp, ChevronDown, ChevronLeft, ChevronRight, Package, Calendar, MapPin, Truck, Box, RefreshCw, Plus, X } from 'lucide-react';
import { 
  useTransportBoxesQuery,
  useTransportBoxSummaryQuery,
  GetTransportBoxesRequest
} from '../../api/hooks/useTransportBoxes';
import { CatalogAutocomplete } from '../common/CatalogAutocomplete';
import { catalogItemToCodeAndName, PRODUCT_TYPE_FILTERS } from '../common/CatalogAutocompleteAdapters';
import TransportBoxDetail from './TransportBoxDetail';
import { PAGE_CONTAINER_HEIGHT } from '../../constants/layout';

// State labels mapping - using string keys since DTO returns strings
const stateLabels: Record<string, string> = {
  'New': 'Nový',
  'Opened': 'Otevřený',
  'InTransit': 'V přepravě',
  'Received': 'Přijatý',
  'Stocked': 'Naskladněný',
  'Reserve': 'V rezervě',
  'Closed': 'Uzavřený',
  'Error': 'Chyba',
};

const stateColors: Record<string, string> = {
  'New': 'bg-gray-100 text-gray-800',
  'Opened': 'bg-blue-100 text-blue-800',
  'InTransit': 'bg-yellow-100 text-yellow-800',
  'Received': 'bg-purple-100 text-purple-800',
  'Stocked': 'bg-green-100 text-green-800',
  'Reserve': 'bg-indigo-100 text-indigo-800',
  'Closed': 'bg-gray-100 text-gray-800',
  'Error': 'bg-red-100 text-red-800',
};

const TransportBoxList: React.FC = () => {
  
  // Filter states - separate input values from applied filters
  const [codeInput, setCodeInput] = useState('');
  const [selectedProduct, setSelectedProduct] = useState<string | null>(null);
  
  const [codeFilter, setCodeFilter] = useState('');
  const [stateFilter, setStateFilter] = useState('ACTIVE');
  const [productFilter, setProductFilter] = useState('');
  
  // Pagination states
  const [skip, setSkip] = useState(0);
  const take = 20;
  
  // Sorting states
  const [sortBy, setSortBy] = useState('id');
  const [sortDescending, setSortDescending] = useState(true);

  // Detail modal states
  const [selectedBoxId, setSelectedBoxId] = useState<number | null>(null);
  const [isDetailModalOpen, setIsDetailModalOpen] = useState(false);

  // State for collapsible sections
  const [isControlsCollapsed, setIsControlsCollapsed] = useState(false);

  // Prepare query request
  const queryRequest: GetTransportBoxesRequest = {
    skip,
    take,
    code: codeFilter || undefined,
    state: stateFilter || undefined,
    productCode: productFilter || undefined,
    sortBy,
    sortDescending,
  };

  // Fetch data
  const { data, isLoading, error, refetch } = useTransportBoxesQuery(queryRequest);
  
  // Fetch summary data (using same filters but no pagination)
  const summaryRequest = {
    code: codeFilter || undefined,
    productCode: productFilter || undefined,
  };
  const { data: summaryData } = useTransportBoxSummaryQuery(summaryRequest);

  // Product filter handling
  const handleProductSelect = (productCodeAndName: string | null) => {
    setSelectedProduct(productCodeAndName);
    if (productCodeAndName) {
      // Extract product code from "CODE - NAME" format
      const productCode = productCodeAndName.split(' - ')[0];
      setProductFilter(productCode);
    } else {
      setProductFilter('');
    }
    setSkip(0); // Reset to first page
  };


  // Handle clear filters
  const handleClearFilters = () => {
    setCodeInput('');
    setSelectedProduct(null);
    setCodeFilter('');
    setStateFilter('ACTIVE');
    setProductFilter('');
    setSkip(0);
  };

  // Handle state filter from summary cards
  const handleStateFilterClick = (state: string) => {
    setStateFilter(state);
    setSkip(0); // Reset to first page
  };

  // Handle sorting
  const handleSort = (column: string) => {
    if (sortBy === column) {
      setSortDescending(!sortDescending);
    } else {
      setSortBy(column);
      setSortDescending(true);
    }
    setSkip(0); // Reset to first page
  };

  // Handle box detail modal
  const handleRowClick = (boxId: number) => {
    setSelectedBoxId(boxId);
    setIsDetailModalOpen(true);
  };

  const handleCloseDetail = () => {
    setIsDetailModalOpen(false);
    setSelectedBoxId(null);
    // Refresh data in case anything was changed in the detail modal
    refetch();
  };

  // Handle "Open New Box" button click - create box directly and show detail
  const handleOpenNewBox = async () => {
    try {
      const { getAuthenticatedApiClient } = await import('../../api/client');
      const { CreateNewTransportBoxRequest } = await import('../../api/generated/api-client');
      
      const apiClient = await getAuthenticatedApiClient();
      const request = new CreateNewTransportBoxRequest({
        description: undefined // Empty box, no description initially
      });
      
      const response = await apiClient.transportBox_CreateNewTransportBox(request);
      
      if (response.success && response.transportBox && response.transportBox.id) {
        // Open the detail modal for the new box immediately
        setSelectedBoxId(response.transportBox.id);
        setIsDetailModalOpen(true);
        // Refresh the data to show the new box in the list
        refetch();
      } else {
        // Handle API error response
        console.error('Error creating transport box:', response.errorCode || 'Unknown error');
      }
    } catch (error) {
      console.error('Error creating transport box:', error);
    }
  };

  // Pagination helpers
  const currentPage = Math.floor(skip / take) + 1;
  const totalItems = data?.totalCount || 0;
  const totalPages = Math.ceil(totalItems / take);

  const handlePageChange = (newPage: number) => {
    setSkip((newPage - 1) * take);
  };

  // Format date for display
  const formatDate = (dateString: string | undefined) => {
    if (!dateString) return '-';
    return new Date(dateString).toLocaleDateString('cs-CZ', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  };

  if (error) {
    return (
      <div className="p-6 bg-red-50 border border-red-200 rounded-lg">
        <div className="flex items-center gap-2">
          <AlertCircle className="h-5 w-5 text-red-600" />
          <div>
            <h3 className="text-red-800 font-semibold">Chyba při načítání transportních boxů</h3>
            <p className="text-red-600 text-sm mt-1">
              {error instanceof Error ? error.message : 'Neznámá chyba'}
            </p>
            <button
              onClick={() => refetch()}
              className="mt-2 px-3 py-1 bg-red-600 text-white rounded hover:bg-red-700 transition-colors text-sm"
            >
              Zkusit znovu
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col w-full" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      {/* Header - Fixed */}
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900">Transportní boxy</h1>
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
                {summaryData && (
                  <span className="text-xs text-gray-500">({summaryData.totalBoxes} boxů)</span>
                )}
              </button>
              
              <div className="flex items-center space-x-3">
                {/* Always visible controls when collapsed */}
                {isControlsCollapsed && (
                  <>
                    {/* Quick summary when collapsed - clickable */}
                    {summaryData && (
                      <div className="flex items-center space-x-2 text-xs">
                        <button
                          onClick={() => handleStateFilterClick('')}
                          className={`px-1 py-0.5 rounded transition-colors hover:bg-gray-100 ${
                            stateFilter === '' ? 'bg-gray-100 ring-1 ring-gray-300' : ''
                          }`}
                          title="Všechny boxy"
                        >
                          <span className="text-gray-700 font-medium">{summaryData.totalBoxes}</span>
                        </button>
                        <span className="text-gray-400">|</span>
                        <button
                          onClick={() => handleStateFilterClick('ACTIVE')}
                          className={`px-1 py-0.5 rounded transition-colors hover:bg-green-50 ${
                            stateFilter === 'ACTIVE' ? 'bg-green-50 ring-1 ring-green-300' : ''
                          }`}
                          title="Aktivní boxy"
                        >
                          <span className="text-green-600 font-medium">{summaryData.activeBoxes}</span>
                        </button>
                        {Object.entries(stateLabels).map(([state, label]) => {
                          const count = summaryData.statesCounts?.[state] || 0;
                          let colorClass = 'text-gray-600';
                          let hoverClass = 'hover:bg-gray-50';
                          let activeClass = 'bg-gray-50 ring-1 ring-gray-300';
                          
                          // Apply special colors for specific states
                          switch (state) {
                            case 'Error':
                              colorClass = count === 0 ? 'text-gray-400' : 'text-red-600';
                              hoverClass = 'hover:bg-red-50';
                              activeClass = 'bg-red-50 ring-1 ring-red-300';
                              break;
                            case 'InTransit':
                              colorClass = count === 0 ? 'text-gray-400' : 'text-yellow-600';
                              hoverClass = 'hover:bg-yellow-50';
                              activeClass = 'bg-yellow-50 ring-1 ring-yellow-300';
                              break;
                            case 'New':
                              colorClass = count === 0 ? 'text-gray-400' : 'text-blue-600';
                              hoverClass = 'hover:bg-blue-50';
                              activeClass = 'bg-blue-50 ring-1 ring-blue-300';
                              break;
                            case 'Opened':
                              colorClass = count === 0 ? 'text-gray-400' : 'text-indigo-600';
                              hoverClass = 'hover:bg-indigo-50';
                              activeClass = 'bg-indigo-50 ring-1 ring-indigo-300';
                              break;
                            case 'Stocked':
                              colorClass = count === 0 ? 'text-gray-400' : 'text-emerald-600';
                              hoverClass = 'hover:bg-emerald-50';
                              activeClass = 'bg-emerald-50 ring-1 ring-emerald-300';
                              break;
                            default:
                              colorClass = count === 0 ? 'text-gray-400' : 'text-gray-600';
                              break;
                          }

                          return (
                            <React.Fragment key={state}>
                              <span className="text-gray-400">|</span>
                              <button
                                onClick={() => handleStateFilterClick(state)}
                                className={`px-1 py-0.5 rounded transition-colors ${hoverClass} ${
                                  stateFilter === state ? activeClass : ''
                                }`}
                                title={label}
                              >
                                <span className={`font-medium ${colorClass}`}>{count}</span>
                              </button>
                            </React.Fragment>
                          );
                        })}
                      </div>
                    )}
                    {/* Search field when collapsed */}
                    <div className="flex-1 max-w-xs">
                      <div className="relative">
                        <Search className="absolute left-2 top-1/2 transform -translate-y-1/2 h-3 w-3 text-gray-400" />
                        <input
                          type="text"
                          value={codeInput}
                          onChange={(e) => setCodeInput(e.target.value)}
                          onKeyPress={(e) => {
                            if (e.key === 'Enter') {
                              setCodeFilter(codeInput);
                              setSkip(0);
                            }
                          }}
                          placeholder="Vyhledat..."
                          className="pl-7 w-full border border-gray-300 rounded-md px-2 py-1 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                        />
                      </div>
                    </div>
                  </>
                )}
                
                {/* Action buttons - always visible */}
                <button
                  onClick={handleOpenNewBox}
                  className="flex items-center px-3 py-1 border border-transparent rounded-md shadow-sm text-xs font-medium text-white bg-indigo-600 hover:bg-indigo-700"
                >
                  <Plus className="h-3 w-3 mr-1" />
                  {isControlsCollapsed ? '' : 'Otevřít nový box'}
                </button>
                <button
                  onClick={() => refetch()}
                  disabled={isLoading}
                  className="flex items-center px-2 py-1 border border-gray-300 rounded-md shadow-sm text-xs font-medium text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50"
                >
                  <RefreshCw className={`h-3 w-3 mr-1 ${isLoading ? 'animate-spin' : ''}`} />
                  {isControlsCollapsed ? '' : 'Obnovit'}
                </button>
              </div>
            </div>
          </div>
          
          {!isControlsCollapsed && (
            <div className="p-3 space-y-4">
              {/* Summary Cards */}
              {summaryData && (
                <div>
                  <h3 className="text-xs font-medium text-gray-700 mb-2">Přehled stavů</h3>
                  <div className="flex flex-wrap items-center gap-2 text-xs">
                    <button
                      onClick={() => handleStateFilterClick('')}
                      className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-gray-100 ${
                        stateFilter === '' ? 'bg-gray-100 ring-1 ring-gray-300' : ''
                      }`}
                    >
                      <Package className="h-3 w-3 text-blue-500 mr-1" />
                      <span className="text-gray-600">Celkem:</span>
                      <span className="font-semibold text-gray-900 ml-1">{summaryData.totalBoxes}</span>
                    </button>
                    
                    <button
                      onClick={() => handleStateFilterClick('ACTIVE')}
                      className={`flex items-center px-2 py-1 rounded-md transition-colors hover:bg-green-50 ${
                        stateFilter === 'ACTIVE' ? 'bg-green-50 ring-1 ring-green-300' : ''
                      }`}
                    >
                      <Truck className="h-3 w-3 text-green-500 mr-1" />
                      <span className="text-gray-600">Aktivní:</span>
                      <span className="font-semibold text-green-600 ml-1">{summaryData.activeBoxes}</span>
                    </button>

                    {/* Individual states with counts - show all states */}
                    {Object.entries(stateLabels).map(([state, label]) => {
                      const count = summaryData.statesCounts?.[state] || 0;
                      const isActive = stateFilter === state;
                      let iconColor = 'text-gray-500';
                      let hoverColor = 'hover:bg-gray-50';
                      let activeColor = 'bg-gray-50 ring-1 ring-gray-300';
                      let IconComponent = Box;
                      
                      // Set colors based on state
                      switch (state) {
                        case 'New':
                          iconColor = 'text-blue-500';
                          hoverColor = 'hover:bg-blue-50';
                          activeColor = 'bg-blue-50 ring-1 ring-blue-300';
                          break;
                        case 'Opened':
                          iconColor = 'text-indigo-500';
                          hoverColor = 'hover:bg-indigo-50';
                          activeColor = 'bg-indigo-50 ring-1 ring-indigo-300';
                          break;
                        case 'InTransit':
                          iconColor = 'text-yellow-500';
                          hoverColor = 'hover:bg-yellow-50';
                          activeColor = 'bg-yellow-50 ring-1 ring-yellow-300';
                          IconComponent = Truck;
                          break;
                        case 'Received':
                          iconColor = 'text-purple-500';
                          hoverColor = 'hover:bg-purple-50';
                          activeColor = 'bg-purple-50 ring-1 ring-purple-300';
                          break;
                        case 'Stocked':
                          iconColor = 'text-emerald-500';
                          hoverColor = 'hover:bg-emerald-50';
                          activeColor = 'bg-emerald-50 ring-1 ring-emerald-300';
                          break;
                        case 'Reserve':
                          iconColor = 'text-cyan-500';
                          hoverColor = 'hover:bg-cyan-50';
                          activeColor = 'bg-cyan-50 ring-1 ring-cyan-300';
                          break;
                        case 'Closed':
                          iconColor = 'text-gray-500';
                          hoverColor = 'hover:bg-gray-50';
                          activeColor = 'bg-gray-50 ring-1 ring-gray-300';
                          break;
                        case 'Error':
                          iconColor = 'text-red-500';
                          hoverColor = 'hover:bg-red-50';
                          activeColor = 'bg-red-50 ring-1 ring-red-300';
                          IconComponent = AlertCircle;
                          break;
                      }

                      return (
                        <button
                          key={state}
                          onClick={() => handleStateFilterClick(state)}
                          className={`flex items-center px-2 py-1 rounded-md transition-colors ${hoverColor} ${
                            isActive ? activeColor : ''
                          }`}
                        >
                          <IconComponent className={`h-3 w-3 mr-1 ${iconColor}`} />
                          <span className="text-gray-600">{label}:</span>
                          <span className={`font-semibold ml-1 ${count === 0 ? 'text-gray-400' : iconColor.replace('text-', 'text-')}`}>
                            {count}
                          </span>
                        </button>
                      );
                    })}
                  </div>
                </div>
              )}
              
              {/* Filters */}
              <div>
                <h3 className="text-xs font-medium text-gray-700 mb-2">Filtry</h3>
                <div className="flex gap-3 items-end">
                  {/* Search - Now much smaller */}
                  <div className="w-40">
                    <label className="block text-xs font-medium text-gray-700 mb-1">
                      Kód boxu
                    </label>
                    <div className="relative">
                      <Search className="absolute left-2 top-1/2 transform -translate-y-1/2 h-3 w-3 text-gray-400" />
                      <input
                        type="text"
                        value={codeInput}
                        onChange={(e) => {
                          setCodeInput(e.target.value);
                          // Don't apply filter immediately - wait for Enter key
                        }}
                        onKeyPress={(e) => {
                          if (e.key === 'Enter') {
                            setCodeFilter(codeInput);
                            setSkip(0);
                          }
                        }}
                        placeholder="Kód boxu..."
                        className="pl-8 pr-8 w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                      />
                      {codeInput && (
                        <button
                          onClick={() => {
                            setCodeInput('');
                            setCodeFilter('');
                            setSkip(0);
                          }}
                          className="absolute right-2 top-1/2 transform -translate-y-1/2 text-gray-400 hover:text-gray-600"
                        >
                          <X className="h-3 w-3" />
                        </button>
                      )}
                    </div>
                  </div>

                  {/* Product Filter - Takes remaining space */}
                  <div className="flex-1">
                    <label className="block text-xs font-medium text-gray-700 mb-1">
                      Produkt v boxu
                    </label>
                    <CatalogAutocomplete<string>
                      value={selectedProduct}
                      onSelect={handleProductSelect}
                      placeholder="Vyhledat produkt..."
                      productTypes={PRODUCT_TYPE_FILTERS.FINISHED_PRODUCTS}
                      itemAdapter={catalogItemToCodeAndName}
                      displayValue={(value) => value}
                      size="sm"
                      clearable
                    />
                  </div>

                </div>

                {/* Clear All Filters Button */}
                {(codeFilter || productFilter) && (
                  <div className="mt-3 flex justify-end">
                    <button
                      onClick={handleClearFilters}
                      className="flex items-center gap-1 px-2 py-1 text-xs text-gray-600 hover:text-gray-800 hover:bg-gray-100 rounded-md transition-colors"
                    >
                      <X className="h-3 w-3" />
                      Vymazat všechny filtry
                    </button>
                  </div>
                )}
              </div>
            </div>
          )}
        </div>

      {/* Results Table */}
      <div className="flex-1 bg-white rounded-lg shadow overflow-hidden flex flex-col min-h-0">
        {isLoading ? (
          <div className="flex-1 flex items-center justify-center">
            <RefreshCw className="h-8 w-8 animate-spin text-gray-400" />
            <span className="ml-2 text-gray-600">Načítání dat...</span>
          </div>
        ) : data?.items?.length === 0 ? (
          <div className="flex-1 flex items-center justify-center">
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
                  <th
                    onClick={() => handleSort('code')}
                    className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
                  >
                    <div className="flex items-center">
                      Kód
                      {sortBy === 'code' && (
                        sortDescending ? <ChevronDown className="ml-1 h-4 w-4" /> : <ChevronUp className="ml-1 h-4 w-4" />
                      )}
                    </div>
                  </th>
                  <th
                    onClick={() => handleSort('state')}
                    className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
                  >
                    <div className="flex items-center">
                      Stav
                      {sortBy === 'state' && (
                        sortDescending ? <ChevronDown className="ml-1 h-4 w-4" /> : <ChevronUp className="ml-1 h-4 w-4" />
                      )}
                    </div>
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Počet položek
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Lokace
                  </th>
                  <th
                    onClick={() => handleSort('laststatechanged')}
                    className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
                  >
                    <div className="flex items-center">
                      Poslední změna
                      {sortBy === 'laststatechanged' && (
                        sortDescending ? <ChevronDown className="ml-1 h-4 w-4" /> : <ChevronUp className="ml-1 h-4 w-4" />
                      )}
                    </div>
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Popis
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {data?.items?.map((box) => (
                  <tr 
                    key={box.id} 
                    className="hover:bg-gray-50 cursor-pointer transition-colors"
                    onClick={() => box.id && handleRowClick(box.id)}
                    title="Klikněte pro zobrazení detailu"
                  >
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                      {box.code || '-'}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                        stateColors[box.state || ''] || 'bg-gray-100 text-gray-800'
                      }`}>
                        {stateLabels[box.state || ''] || box.state}
                      </span>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                      {box.itemCount}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                      <div className="flex items-center">
                        <MapPin className="h-4 w-4 text-gray-400 mr-1" />
                        {box.location || '-'}
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                      <div className="flex items-center">
                        <Calendar className="h-4 w-4 text-gray-400 mr-1" />
                        {formatDate(box.lastStateChanged?.toString())}
                      </div>
                    </td>
                    <td className="px-6 py-4 text-sm text-gray-900 max-w-xs truncate">
                      {box.description || '-'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        
        {/* Pagination - Always visible at bottom */}
        {totalItems > 0 && (
          <div className="flex-shrink-0 bg-white px-3 py-2 flex items-center justify-between border-t border-gray-200 text-xs">
            <div className="flex-1 flex justify-between sm:hidden">
              <button
                onClick={() => handlePageChange(currentPage - 1)}
                disabled={currentPage === 1}
                className="relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                Předchozí
              </button>
              <button
                onClick={() => handlePageChange(currentPage + 1)}
                disabled={currentPage === totalPages}
                className="ml-2 relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                Další
              </button>
            </div>
            <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
              <div>
                <p className="text-xs text-gray-600">
                  {skip + 1}-{Math.min(skip + take, totalItems)} z {totalItems}
                </p>
              </div>
              <div>
                <nav className="relative z-0 inline-flex rounded shadow-sm -space-x-px" aria-label="Pagination">
                  <button
                    onClick={() => handlePageChange(currentPage - 1)}
                    disabled={currentPage === 1}
                    className="relative inline-flex items-center px-1 py-1 rounded-l border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    <ChevronLeft className="h-3 w-3" />
                  </button>
                  
                  {/* Page numbers */}
                  {Array.from({ length: Math.min(totalPages, 5) }, (_, i) => {
                    let pageNum: number;
                    if (totalPages <= 5) {
                      pageNum = i + 1;
                    } else if (currentPage <= 3) {
                      pageNum = i + 1;
                    } else if (currentPage >= totalPages - 2) {
                      pageNum = totalPages - 4 + i;
                    } else {
                      pageNum = currentPage - 2 + i;
                    }
                    
                    return (
                      <button
                        key={pageNum}
                        onClick={() => handlePageChange(pageNum)}
                        className={`relative inline-flex items-center px-2 py-1 border text-xs font-medium ${
                          pageNum === currentPage
                            ? 'z-10 bg-indigo-50 border-indigo-500 text-indigo-600'
                            : 'bg-white border-gray-300 text-gray-500 hover:bg-gray-50'
                        }`}
                      >
                        {pageNum}
                      </button>
                    );
                  })}
                  
                  <button
                    onClick={() => handlePageChange(currentPage + 1)}
                    disabled={currentPage === totalPages}
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

      {/* Transport Box Detail Modal */}
      <TransportBoxDetail 
        boxId={selectedBoxId}
        isOpen={isDetailModalOpen}
        onClose={handleCloseDetail}
      />
    </div>
  );
};

export default TransportBoxList;