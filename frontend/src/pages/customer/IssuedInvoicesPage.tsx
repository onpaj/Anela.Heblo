import React, { useState } from "react";
import {
  Search,
  Filter,
  AlertCircle,
  CheckCircle,
  Clock,
  Loader2,
  ChevronUp,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  BarChart3,
  List,
  Download,
} from "lucide-react";
import { useIssuedInvoicesList } from "../../api/hooks/useIssuedInvoices";
import { useIssuedInvoiceSyncStats } from "../../api/hooks/useIssuedInvoiceSyncStats";
import { formatDate, formatDateTime, formatCurrency } from "../../utils/formatters";
import IssuedInvoiceDetailModal from "../../components/customer/IssuedInvoiceDetailModal";
import InvoiceImportStatistics from "../../components/pages/automation/InvoiceImportStatistics";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";

const IssuedInvoicesPage: React.FC = () => {
  // Tab state
  const [activeTab, setActiveTab] = useState<'statistics' | 'grid'>('statistics');
  
  // Filter states - separate input values from applied filters
  const [invoiceIdInput, setInvoiceIdInput] = useState("");
  const [customerNameInput, setCustomerNameInput] = useState("");
  const [invoiceIdFilter, setInvoiceIdFilter] = useState("");
  const [customerNameFilter, setCustomerNameFilter] = useState("");
  const [invoiceDateFrom, setInvoiceDateFrom] = useState("");
  const [invoiceDateTo, setInvoiceDateTo] = useState("");
  const [showOnlyUnsynced, setShowOnlyUnsynced] = useState(false);
  const [showOnlyWithErrors, setShowOnlyWithErrors] = useState(false);

  // Pagination states
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);

  // Sorting states - default to LastSyncTime descending
  const [sortBy, setSortBy] = useState("LastSyncTime");
  const [sortDescending, setSortDescending] = useState(true);

  // Modal states
  const [selectedInvoiceId, setSelectedInvoiceId] = useState<string | null>(null);
  const [showImportModal, setShowImportModal] = useState(false);

  // Import states
  const [importType, setImportType] = useState<'date' | 'invoice'>('date');
  const [importInvoiceId, setImportInvoiceId] = useState('');
  const [importDateFrom, setImportDateFrom] = useState('');
  const [importDateTo, setImportDateTo] = useState('');
  const [importCurrency, setImportCurrency] = useState<'CZK' | 'EUR'>('CZK');
  const [importLoading, setImportLoading] = useState(false);

  // Use the actual API call for grid data
  const {
    data,
    isLoading: loading,
    error,
    refetch,
  } = useIssuedInvoicesList({
    pageNumber,
    pageSize,
    sortBy,
    sortDescending,
    invoiceId: invoiceIdFilter.trim() || undefined,
    customerName: customerNameFilter.trim() || undefined,
    invoiceDateFrom: invoiceDateFrom ? new Date(invoiceDateFrom).toISOString() : undefined,
    invoiceDateTo: invoiceDateTo ? new Date(invoiceDateTo).toISOString() : undefined,
    showOnlyUnsynced,
    showOnlyWithErrors,
  });

  // Use sync stats API for statistics tab
  const {
    data: syncStats,
    isLoading: syncStatsLoading,
    error: syncStatsError,
    refetch: refetchSyncStats
  } = useIssuedInvoiceSyncStats({
    fromDate: new Date(Date.now() - 30 * 24 * 60 * 60 * 1000), // 30 days ago
    toDate: new Date()
  });

  const filteredItems = data?.items || [];
  const totalCount = data?.totalCount || 0; // Total count from API
  const totalPages = Math.ceil(totalCount / pageSize);

  // Handler for applying filters
  const handleApplyFilters = async () => {
    setInvoiceIdFilter(invoiceIdInput);
    setCustomerNameFilter(customerNameInput);
    setPageNumber(1); // Reset to first page when applying filters

    // Force data reload by refetching
    await refetch();
  };

  // Handler for Enter key press
  const handleKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === "Enter") {
      handleApplyFilters();
    }
  };

  // Handler for clearing all filters
  const handleClearFilters = async () => {
    setInvoiceIdInput("");
    setCustomerNameInput("");
    setInvoiceIdFilter("");
    setCustomerNameFilter("");
    setInvoiceDateFrom("");
    setInvoiceDateTo("");
    setShowOnlyUnsynced(false);
    setShowOnlyWithErrors(false);
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

  // Modal handlers
  const handleItemClick = (invoice: any) => {
    setSelectedInvoiceId(invoice.id);
  };

  const handleCloseDetail = () => {
    setSelectedInvoiceId(null);
  };

  const handleInvoiceUpdated = async () => {
    // Refresh both the invoices list and sync stats after detail modal update
    await Promise.all([
      refetch(),
      refetchSyncStats()
    ]);
  };

  // Import handlers
  const handleImportInvoices = async () => {
    if (importLoading) return;

    try {
      setImportLoading(true);

      // Validate input
      if (importType === 'invoice' && !importInvoiceId.trim()) {
        alert('Zadejte číslo faktury');
        return;
      }
      if (importType === 'date' && (!importDateFrom || !importDateTo)) {
        alert('Zadejte rozsah datumů');
        return;
      }

      // Build request body
      const requestBody = {
        query: {
          requestId: `import-${Date.now()}`,
          currency: importCurrency,
          ...(importType === 'invoice' 
            ? { invoiceId: importInvoiceId.trim() }
            : { 
                dateFrom: new Date(importDateFrom).toISOString(),
                dateTo: new Date(importDateTo).toISOString()
              })
        }
      };

      // Make API call using proper baseUrl
      const response = await fetch(`${process.env.REACT_APP_API_URL || 'http://localhost:5001'}/api/invoices/import`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(requestBody),
      });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }

      const result = await response.json();
      
      // Show results
      const successCount = result.succeeded?.length || 0;
      const failedCount = result.failed?.length || 0;
      const totalCount = successCount + failedCount;

      if (totalCount === 0) {
        alert('Žádné faktury nebyly nalezeny pro import.');
      } else {
        alert(`Import dokončen:\n✓ Úspěšně: ${successCount}\n✗ Chyba: ${failedCount}`);
      }

      // Reset form and close modal
      setShowImportModal(false);
      setImportInvoiceId('');
      setImportDateFrom('');
      setImportDateTo('');
      setImportType('date');
      setImportCurrency('CZK');

      // Refresh both the invoices list and sync stats
      await Promise.all([
        refetch(),
        refetchSyncStats()
      ]);

    } catch (error) {
      console.error('Import error:', error);
      alert(`Chyba při importu: ${error instanceof Error ? error.message : 'Neočekávaná chyba'}`);
    } finally {
      setImportLoading(false);
    }
  };

  // Sortable header component
  const SortableHeader: React.FC<{
    column: string;
    children: React.ReactNode;
  }> = ({ column, children }) => {
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
              className={`h-3 w-3 ${isAscending ? "text-indigo-600" : "text-gray-300"}`}
            />
            <ChevronDown
              className={`h-3 w-3 -mt-1 ${isDescending ? "text-indigo-600" : "text-gray-300"}`}
            />
          </div>
        </div>
      </th>
    );
  };

  const getSyncStatusIcon = (invoice: any) => {
    if (invoice.errorType) {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-800">
          <AlertCircle className="h-3 w-3 mr-1" />
          Chyba
        </span>
      );
    }
    if (invoice.isSynced) {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800">
          <CheckCircle className="h-3 w-3 mr-1" />
          Synced
        </span>
      );
    }
    return (
      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-yellow-100 text-yellow-800">
        <Clock className="h-3 w-3 mr-1" />
        Čeká
      </span>
    );
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500">Načítání faktur...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání faktur: {error.message}</div>
        </div>
      </div>
    );
  }

  // Statistics tab component
  const StatisticsTab: React.FC = () => {
    if (syncStatsLoading) {
      return (
        <div className="flex items-center justify-center h-64">
          <div className="flex items-center space-x-2">
            <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
            <div className="text-gray-500">Načítání statistik...</div>
          </div>
        </div>
      );
    }

    if (syncStatsError) {
      return (
        <div className="flex items-center justify-center h-64">
          <div className="flex items-center space-x-2 text-red-600">
            <AlertCircle className="h-5 w-5" />
            <div>Chyba při načítání statistik: {(syncStatsError as Error)?.message || 'Neočekávaná chyba'}</div>
          </div>
        </div>
      );
    }

    return (
      <div className="flex-1 overflow-auto space-y-6">
        {/* Summary Cards */}
        {syncStats && (
          <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
            <div className="bg-white rounded-lg border border-gray-200 p-4">
              <h3 className="text-sm font-medium text-gray-600">Celkem faktur</h3>
              <p className="text-2xl font-bold text-gray-900 mt-1">
                {syncStats.totalInvoices}
              </p>
            </div>
            
            <div className="bg-white rounded-lg border border-gray-200 p-4">
              <h3 className="text-sm font-medium text-gray-600">Synchronizováno</h3>
              <p className="text-2xl font-bold text-green-600 mt-1">
                {syncStats.syncedInvoices}
              </p>
            </div>
            
            <div className="bg-white rounded-lg border border-gray-200 p-4">
              <h3 className="text-sm font-medium text-gray-600">Nesynchronizováno</h3>
              <p className="text-2xl font-bold text-yellow-600 mt-1">
                {syncStats.unsyncedInvoices}
              </p>
            </div>
            
            <div className="bg-white rounded-lg border border-gray-200 p-4">
              <h3 className="text-sm font-medium text-gray-600">S chybami</h3>
              <p className={`text-2xl font-bold mt-1 ${
                syncStats.invoicesWithErrors > 0 ? 'text-red-600' : 'text-green-600'
              }`}>
                {syncStats.invoicesWithErrors}
              </p>
            </div>
          </div>
        )}

        {/* Invoice Import Statistics Chart */}
        <div className="bg-white rounded-lg border border-gray-200">
          <InvoiceImportStatistics />
        </div>
      </div>
    );
  };

  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      {/* Header with Tabs - Fixed */}
      <div className="flex-shrink-0 mb-3">
        <div className="flex items-center justify-between">
          <h1 className="text-lg font-semibold text-gray-900">Vydané faktury</h1>
        </div>
        
        {/* Tab Navigation */}
        <div className="mt-3 border-b border-gray-200">
          <nav className="-mb-px flex space-x-8">
            <button
              onClick={() => setActiveTab('statistics')}
              className={`flex items-center gap-2 py-2 px-1 border-b-2 font-medium text-sm ${
                activeTab === 'statistics'
                  ? 'border-indigo-500 text-indigo-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
              }`}
            >
              <BarChart3 className="h-4 w-4" />
              Statistiky
            </button>
            <button
              onClick={() => setActiveTab('grid')}
              className={`flex items-center gap-2 py-2 px-1 border-b-2 font-medium text-sm ${
                activeTab === 'grid'
                  ? 'border-indigo-500 text-indigo-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
              }`}
            >
              <List className="h-4 w-4" />
              Seznam
            </button>
          </nav>
        </div>
      </div>

      {/* Tab Content */}
      {activeTab === 'statistics' ? (
        <StatisticsTab />
      ) : (
        <>
          {/* Filters and Import - Fixed (only for grid tab) */}
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
                      id="invoiceId"
                      value={invoiceIdInput}
                      onChange={(e) => setInvoiceIdInput(e.target.value)}
                      onKeyDown={handleKeyDown}
                      className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"
                      placeholder="Číslo faktury..."
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
                      id="customerName"
                      value={customerNameInput}
                      onChange={(e) => setCustomerNameInput(e.target.value)}
                      onKeyDown={handleKeyDown}
                      className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"
                      placeholder="Zákazník..."
                    />
                  </div>
                </div>

                <div className="flex gap-2">
                  <input
                    type="date"
                    value={invoiceDateFrom}
                    onChange={(e) => setInvoiceDateFrom(e.target.value)}
                    className="focus:ring-indigo-500 focus:border-indigo-500 block py-2 px-3 sm:text-sm border-gray-300 rounded-md"
                    placeholder="Od"
                  />
                  <input
                    type="date"
                    value={invoiceDateTo}
                    onChange={(e) => setInvoiceDateTo(e.target.value)}
                    className="focus:ring-indigo-500 focus:border-indigo-500 block py-2 px-3 sm:text-sm border-gray-300 rounded-md"
                    placeholder="Do"
                  />
                </div>

                <div className="flex gap-2">
                  <label className="flex items-center text-sm">
                    <input
                      type="checkbox"
                      checked={showOnlyUnsynced}
                      onChange={(e) => setShowOnlyUnsynced(e.target.checked)}
                      className="h-4 w-4 text-indigo-600 border-gray-300 rounded focus:ring-indigo-500"
                    />
                    <span className="ml-1 text-gray-700">Nesync</span>
                  </label>
                  
                  <label className="flex items-center text-sm">
                    <input
                      type="checkbox"
                      checked={showOnlyWithErrors}
                      onChange={(e) => setShowOnlyWithErrors(e.target.checked)}
                      className="h-4 w-4 text-indigo-600 border-gray-300 rounded focus:ring-indigo-500"
                    />
                    <span className="ml-1 text-gray-700">Chyby</span>
                  </label>
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
                <button
                  onClick={() => setShowImportModal(true)}
                  disabled={importLoading}
                  className="bg-emerald-600 hover:bg-emerald-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200 text-sm flex items-center gap-2 disabled:opacity-50"
                >
                  {importLoading ? (
                    <>
                      <Loader2 className="h-4 w-4 animate-spin" />
                      Importuje...
                    </>
                  ) : (
                    <>
                      <Download className="h-4 w-4" />
                      Import
                    </>
                  )}
                </button>
              </div>
            </div>
          </div>

          {/* Data Grid - Scrollable */}
          <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0 relative">
            {loading ? (
              <div className="flex items-center justify-center h-64">
                <div className="flex items-center space-x-2">
                  <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
                  <div className="text-gray-500">Načítání faktur...</div>
                </div>
              </div>
            ) : error ? (
              <div className="flex items-center justify-center h-64">
                <div className="flex items-center space-x-2 text-red-600">
                  <AlertCircle className="h-5 w-5" />
                  <div>Chyba při načítání faktur: {(error as Error)?.message || 'Neočekávaná chyba'}</div>
                </div>
              </div>
            ) : (
              <>
                {/* Import loading overlay */}
                {importLoading && (
                  <div className="absolute inset-0 bg-white bg-opacity-75 flex items-center justify-center z-10">
                    <div className="flex items-center space-x-3">
                      <Loader2 className="h-6 w-6 animate-spin text-emerald-500" />
                      <div className="text-emerald-700 font-medium">Probíhá import faktur...</div>
                    </div>
                  </div>
                )}
                
                <div className="flex-1 overflow-auto">
                  <table className="min-w-full divide-y divide-gray-200">
                    <thead className="bg-gray-50 sticky top-0 z-10">
                      <tr>
                        <SortableHeader column="Id">
                          Číslo faktury
                        </SortableHeader>
                        <SortableHeader column="InvoiceDate">
                          Datum faktury
                        </SortableHeader>
                        <SortableHeader column="CustomerName">
                          Zákazník
                        </SortableHeader>
                        <SortableHeader column="Price">
                          Částka
                        </SortableHeader>
                        <th
                          scope="col"
                          className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                        >
                          Měna
                        </th>
                        <th
                          scope="col"
                          className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                        >
                          Stav
                        </th>
                        <SortableHeader column="LastSyncTime">
                          Poslední sync
                        </SortableHeader>
                      </tr>
                    </thead>
                    <tbody className="bg-white divide-y divide-gray-200">
                      {filteredItems.map((invoice: any) => (
                        <tr
                          key={invoice.id}
                          className="hover:bg-gray-50 cursor-pointer transition-colors duration-150"
                          onClick={() => handleItemClick(invoice)}
                          title="Klikněte pro zobrazení detailu"
                        >
                          <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                            {invoice.id}
                          </td>
                          <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                            {formatDate(invoice.invoiceDate)}
                          </td>
                          <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                            {invoice.customerName || "-"}
                          </td>
                          <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-right">
                            <span className="inline-flex items-center px-3 py-1 rounded-full text-sm font-semibold bg-indigo-100 text-indigo-800">
                              {formatCurrency(invoice.price)}
                            </span>
                          </td>
                          <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-center">
                            <span className={`inline-flex items-center px-2 py-1 rounded text-xs font-medium ${
                              (invoice as any).currency === 'EUR' 
                                ? 'bg-yellow-100 text-yellow-800' 
                                : 'bg-blue-100 text-blue-800'
                            }`}>
                              {(invoice as any).currency || 'CZK'}
                            </span>
                          </td>
                          <td className="px-6 py-4 whitespace-nowrap text-sm text-center">
                            {getSyncStatusIcon(invoice)}
                          </td>
                          <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                            {invoice.lastSyncTime ? formatDate(invoice.lastSyncTime) : "-"}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>

                  {filteredItems.length === 0 && (
                    <div className="text-center py-8">
                      <p className="text-gray-500">Žádné faktury nebyly nalezeny.</p>
                    </div>
                  )}
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
                          {Math.min((pageNumber - 1) * pageSize + 1, totalCount)}-
                          {Math.min(pageNumber * pageSize, totalCount)} z {totalCount}
                          {invoiceIdFilter || customerNameFilter ? (
                            <span className="text-gray-500"> (filtrováno)</span>
                          ) : (
                            ""
                          )}
                        </p>
                        <div className="flex items-center space-x-1">
                          <span className="text-xs text-gray-600">Zobrazit:</span>
                          <select
                            id="pageSize"
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
                        <nav
                          className="relative z-0 inline-flex rounded shadow-sm -space-x-px"
                          aria-label="Pagination"
                        >
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
                                  pageNum === pageNumber
                                    ? "z-10 bg-indigo-50 border-indigo-500 text-indigo-600"
                                    : "bg-white border-gray-300 text-gray-500 hover:bg-gray-50"
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
              </>
            )}
          </div>
        </>
      )}

      {/* Import Modal */}
      {showImportModal && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4">
            {/* Header */}
            <div className="flex justify-between items-center p-6 border-b border-gray-200">
              <h2 className="text-xl font-semibold text-gray-900 flex items-center gap-2">
                <Download className="h-5 w-5" />
                Import faktur
              </h2>
              <button
                onClick={() => setShowImportModal(false)}
                className="text-gray-400 hover:text-gray-600 transition-colors"
              >
                ✕
              </button>
            </div>

            {/* Content */}
            <div className="p-6 space-y-6">
              {/* Import Type Selection */}
              <div>
                <label className="text-sm font-medium text-gray-900 mb-3 block">
                  Typ importu
                </label>
                <div className="space-y-2">
                  <label className="flex items-center">
                    <input
                      type="radio"
                      name="importType"
                      value="date"
                      checked={importType === 'date'}
                      onChange={(e) => setImportType(e.target.value as 'date' | 'invoice')}
                      className="h-4 w-4 text-emerald-600 border-gray-300 focus:ring-emerald-500"
                    />
                    <span className="ml-2 text-sm text-gray-700">Rozsah datumů</span>
                  </label>
                  <label className="flex items-center">
                    <input
                      type="radio"
                      name="importType"
                      value="invoice"
                      checked={importType === 'invoice'}
                      onChange={(e) => setImportType(e.target.value as 'date' | 'invoice')}
                      className="h-4 w-4 text-emerald-600 border-gray-300 focus:ring-emerald-500"
                    />
                    <span className="ml-2 text-sm text-gray-700">Konkrétní faktura</span>
                  </label>
                </div>
              </div>

              {/* Currency Selection */}
              <div>
                <label className="text-sm font-medium text-gray-900 mb-2 block">
                  Měna
                </label>
                <select
                  value={importCurrency}
                  onChange={(e) => setImportCurrency(e.target.value as 'CZK' | 'EUR')}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:ring-emerald-500 focus:border-emerald-500"
                >
                  <option value="CZK">CZK</option>
                  <option value="EUR">EUR</option>
                </select>
              </div>

              {/* Conditional Input Fields */}
              {importType === 'date' ? (
                <div className="space-y-4">
                  <div>
                    <label className="text-sm font-medium text-gray-900 mb-2 block">
                      Datum od
                    </label>
                    <input
                      type="date"
                      value={importDateFrom}
                      onChange={(e) => setImportDateFrom(e.target.value)}
                      className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:ring-emerald-500 focus:border-emerald-500"
                    />
                  </div>
                  <div>
                    <label className="text-sm font-medium text-gray-900 mb-2 block">
                      Datum do
                    </label>
                    <input
                      type="date"
                      value={importDateTo}
                      onChange={(e) => setImportDateTo(e.target.value)}
                      className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:ring-emerald-500 focus:border-emerald-500"
                    />
                  </div>
                </div>
              ) : (
                <div>
                  <label className="text-sm font-medium text-gray-900 mb-2 block">
                    Číslo faktury
                  </label>
                  <input
                    type="text"
                    value={importInvoiceId}
                    onChange={(e) => setImportInvoiceId(e.target.value)}
                    placeholder="Zadejte číslo faktury"
                    className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:ring-emerald-500 focus:border-emerald-500"
                  />
                </div>
              )}
            </div>

            {/* Footer */}
            <div className="border-t border-gray-200 px-6 py-4 bg-gray-50 rounded-b-lg">
              <div className="flex justify-end gap-3">
                <button
                  onClick={() => setShowImportModal(false)}
                  disabled={importLoading}
                  className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-emerald-500 focus:ring-offset-2 disabled:opacity-50"
                >
                  Zrušit
                </button>
                <button
                  onClick={handleImportInvoices}
                  disabled={importLoading}
                  className="px-4 py-2 text-sm font-medium text-white bg-emerald-600 border border-transparent rounded-md hover:bg-emerald-700 focus:outline-none focus:ring-2 focus:ring-emerald-500 focus:ring-offset-2 disabled:opacity-50 flex items-center gap-2"
                >
                  {importLoading ? (
                    <>
                      <Loader2 className="h-4 w-4 animate-spin" />
                      Importuje...
                    </>
                  ) : (
                    <>
                      <Download className="h-4 w-4" />
                      Importovat
                    </>
                  )}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Modal for Invoice Detail */}
      <IssuedInvoiceDetailModal
        invoiceId={selectedInvoiceId || ""}
        isOpen={!!selectedInvoiceId}
        onClose={handleCloseDetail}
        onInvoiceUpdated={handleInvoiceUpdated}
      />
    </div>
  );
};

export default IssuedInvoicesPage;