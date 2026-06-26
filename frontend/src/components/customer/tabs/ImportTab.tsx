import React, { useState } from "react";
import {
  CreditCard,
  AlertCircle,
  CheckCircle,
  Loader2,
  ChevronUp,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  RefreshCw,
  Filter,
  Search,
  Download,
} from "lucide-react";
import { 
  useBankStatementsList, 
  useBankStatementImport, 
  useBankStatementAccounts
} from "../../../api/hooks/useBankStatements";
import { formatDate, formatDateTime } from "../../../utils/formatters";

const ImportTab: React.FC = () => {
  // Local input state (uncommitted)
  const [transferIdInput, setTransferIdInput] = useState("");
  const [accountInput, setAccountInput] = useState("");
  const [statementDateFrom, setStatementDateFrom] = useState("");
  const [statementDateTo, setStatementDateTo] = useState("");
  const [showOnlyErrors, setShowOnlyErrors] = useState(false);
  const [dateRangeError, setDateRangeError] = useState<string | null>(null);

  // Committed filters (drive the hook; updated only on Apply / Clear)
  const [committedFilters, setCommittedFilters] = useState<{
    transferId?: string;
    account?: string;
    dateFrom?: string;
    dateTo?: string;
    errorsOnly?: boolean;
  }>({});

  const hasActiveFilter =
    !!committedFilters.transferId ||
    !!committedFilters.account ||
    !!committedFilters.dateFrom ||
    !!committedFilters.dateTo ||
    !!committedFilters.errorsOnly;

  // Pagination states
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);

  // Sorting states - default to ImportDate descending
  const [sortBy, setSortBy] = useState("ImportDate");
  const [sortDescending, setSortDescending] = useState(true);

  // Import modal states
  const [showImportModal, setShowImportModal] = useState(false);
  const [selectedAccount, setSelectedAccount] = useState("");
  const [importDate, setImportDate] = useState("");
  const [isImporting, setIsImporting] = useState(false);

  // Use the actual API call for data
  const {
    data,
    isLoading: loading,
    error,
    refetch,
  } = useBankStatementsList({
    transferId: committedFilters.transferId,
    account: committedFilters.account,
    dateFrom: committedFilters.dateFrom,
    dateTo: committedFilters.dateTo,
    errorsOnly: committedFilters.errorsOnly,
    skip: (pageNumber - 1) * pageSize,
    take: pageSize,
    orderBy: sortBy,
    ascending: !sortDescending,
  });

  // Import related hooks
  const importMutation = useBankStatementImport();
  const { data: accounts, isLoading: accountsLoading } = useBankStatementAccounts();

  const filteredItems = data?.items || [];
  const totalCount = data?.totalCount || 0;
  const totalPages = Math.ceil(totalCount / pageSize);

  // Handler for applying filters
  const handleApplyFilters = () => {
    if (statementDateFrom && statementDateTo && statementDateFrom > statementDateTo) {
      setDateRangeError('"Od" musí být dříve nebo stejně jako "Do".');
      return;
    }
    setDateRangeError(null);

    const trimmedTransferId = transferIdInput.trim();
    const trimmedAccount = accountInput.trim();

    setCommittedFilters({
      transferId: trimmedTransferId || undefined,
      account: trimmedAccount || undefined,
      dateFrom: statementDateFrom || undefined,
      dateTo: statementDateTo || undefined,
      errorsOnly: showOnlyErrors || undefined,
    });
    setPageNumber(1);
  };

  // Handler for Enter key press
  const handleKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === "Enter") {
      handleApplyFilters();
    }
  };

  // Handler for clearing all filters
  const handleClearFilters = () => {
    setTransferIdInput("");
    setAccountInput("");
    setStatementDateFrom("");
    setStatementDateTo("");
    setShowOnlyErrors(false);
    setDateRangeError(null);
    setCommittedFilters({});
    setPageNumber(1);
  };

  // Sorting handler
  const handleSort = (column: string) => {
    if (sortBy === column) {
      setSortDescending(!sortDescending);
    } else {
      setSortBy(column);
      setSortDescending(false);
    }
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

  // Import handlers
  const handleImportSubmit = async () => {
    if (!selectedAccount || !importDate) {
      return; // Validation - all fields required
    }

    setIsImporting(true);
    try {
      // Single import request for the selected date
      await importMutation.mutateAsync({
        accountName: selectedAccount,
        dateFrom: importDate,
        dateTo: importDate,
      });

      // Show success message
      alert(`Import dokončen pro datum ${importDate}`);
      
      // Refresh the bank statements list
      await refetch();

      // Close modal and refresh data (after success message and refresh)
      setShowImportModal(false);
      resetImportForm();
      
    } catch (error) {
      console.error('Import error:', error);
      alert(`Chyba při importu: ${error instanceof Error ? error.message : 'Neznámá chyba'}`);
    } finally {
      setIsImporting(false);
    }
  };

  // Helper function to reset import modal form
  const resetImportForm = () => {
    setSelectedAccount("");
    setImportDate("");
  };

  const handleOpenImportModal = () => {
    setShowImportModal(true);
    
    // Set default account if only one is available
    if (accounts && accounts.length === 1) {
      setSelectedAccount(accounts[0].value);
    }
    
    // Set default date to yesterday
    const yesterday = new Date();
    yesterday.setDate(yesterday.getDate() - 1);
    const yesterdayString = yesterday.toISOString().split('T')[0]; // YYYY-MM-DD format
    
    setImportDate(yesterdayString);
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
        className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100 select-none dark:text-graphite-muted dark:hover:bg-white/5"
        onClick={() => handleSort(column)}
      >
        <div className="flex items-center space-x-1">
          <span>{children}</span>
          <div className="flex flex-col">
            <ChevronUp
              className={`h-3 w-3 ${isAscending ? "text-indigo-600 dark:text-graphite-accent" : "text-gray-300 dark:text-graphite-faint"}`}
            />
            <ChevronDown
              className={`h-3 w-3 -mt-1 ${isDescending ? "text-indigo-600 dark:text-graphite-accent" : "text-gray-300 dark:text-graphite-faint"}`}
            />
          </div>
        </div>
      </th>
    );
  };

  // Status indicator for import result
  const getImportStatusIcon = (importResult: string) => {
    if (importResult === "OK") {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800 dark:bg-emerald-900/30 dark:text-emerald-300">
          <CheckCircle className="h-3 w-3 mr-1" />
          Úspěch
        </span>
      );
    } else {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300">
          <AlertCircle className="h-3 w-3 mr-1" />
          {importResult || "Chyba"}
        </span>
      );
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500 dark:text-graphite-muted">Načítání výpisů...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600 dark:text-red-400">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání výpisů: {(error as Error).message}</div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full overflow-hidden">
      {/* Header Actions - Fixed */}
      <div className="flex-shrink-0 mb-4">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-lg font-semibold text-gray-900 flex items-center gap-3 dark:text-graphite-text">
              <CreditCard className="h-6 w-6 text-indigo-600 dark:text-graphite-accent" />
              Přehled bankovních výpisů
            </h2>
            <p className="text-gray-600 mt-1 text-sm dark:text-graphite-muted">
              Seznam všech naimportovaných bankovních výpisů
            </p>
          </div>
          
          <div className="flex items-center gap-3">
            <button
              onClick={handleOpenImportModal}
              disabled={isImporting}
              className="flex items-center gap-2 px-4 py-2 bg-emerald-600 text-white rounded-lg hover:bg-emerald-700 transition-colors text-sm disabled:opacity-50"
            >
              {isImporting ? (
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
            <button
              onClick={() => refetch()}
              className="flex items-center gap-2 px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors text-sm"
            >
              <RefreshCw className="h-4 w-4" />
              Obnovit
            </button>
          </div>
        </div>
      </div>

      {/* Filters - Fixed */}
      <div className="flex-shrink-0 bg-white shadow rounded-lg p-4 mb-4 dark:bg-graphite-surface dark:shadow-soft-dark">
        <div className="flex items-center justify-between flex-wrap gap-3">
          <div className="flex items-center gap-3 flex-1 min-w-0">
            <div className="flex items-center">
              <Filter className="h-4 w-4 text-gray-400 mr-2 dark:text-graphite-faint" />
              <span className="text-sm font-medium text-gray-900 dark:text-graphite-text">Filtry:</span>
            </div>

            <div className="flex-1 max-w-xs">
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Search className="h-4 w-4 text-gray-400 dark:text-graphite-faint" />
                </div>
                <input
                  type="text"
                  id="transferId"
                  value={transferIdInput}
                  onChange={(e) => setTransferIdInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
                  placeholder="Transfer ID..."
                />
              </div>
            </div>

            <div className="flex-1 max-w-xs">
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Search className="h-4 w-4 text-gray-400 dark:text-graphite-faint" />
                </div>
                <input
                  type="text"
                  id="account"
                  value={accountInput}
                  onChange={(e) => setAccountInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
                  placeholder="Účet..."
                />
              </div>
            </div>

            <div className="flex gap-2">
              <input
                type="date"
                value={statementDateFrom}
                onChange={(e) => setStatementDateFrom(e.target.value)}
                className="focus:ring-indigo-500 focus:border-indigo-500 block py-2 px-3 sm:text-sm border-gray-300 rounded-md dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
                placeholder="Od"
              />
              <input
                type="date"
                value={statementDateTo}
                onChange={(e) => setStatementDateTo(e.target.value)}
                className="focus:ring-indigo-500 focus:border-indigo-500 block py-2 px-3 sm:text-sm border-gray-300 rounded-md dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
                placeholder="Do"
              />
            </div>
            {dateRangeError && (
              <p className="text-xs text-red-600 dark:text-red-400">{dateRangeError}</p>
            )}

            <div className="flex gap-2">
              <label className="flex items-center text-sm">
                <input
                  type="checkbox"
                  checked={showOnlyErrors}
                  onChange={(e) => setShowOnlyErrors(e.target.checked)}
                  className="h-4 w-4 text-indigo-600 border-gray-300 rounded focus:ring-indigo-500 dark:border-graphite-border dark:bg-graphite-surface-2"
                />
                <span className="ml-1 text-gray-700 dark:text-graphite-muted">Jen chyby</span>
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
          </div>
        </div>
      </div>

      {/* Data Grid - Scrollable */}
      <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0 dark:bg-graphite-surface dark:shadow-soft-dark">
        <div className="flex-1 overflow-auto">
          <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
            <thead className="bg-gray-50 sticky top-0 z-10 dark:bg-graphite-surface-2">
              <tr>
                <SortableHeader column="TransferId">
                  Transfer ID
                </SortableHeader>
                <SortableHeader column="Account">
                  Účet
                </SortableHeader>
                <SortableHeader column="StatementDate">
                  Datum výpisu
                </SortableHeader>
                <SortableHeader column="ImportDate">
                  Datum importu
                </SortableHeader>
                <SortableHeader column="ItemCount">
                  Počet položek
                </SortableHeader>
                <th
                  scope="col"
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider dark:text-graphite-muted"
                >
                  Měna
                </th>
                <th
                  scope="col"
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider dark:text-graphite-muted"
                >
                  Stav importu
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200 dark:bg-graphite-surface dark:divide-graphite-border">
              {filteredItems.map((statement) => (
                <tr
                  key={statement.id}
                  className="hover:bg-gray-50 transition-colors duration-150 dark:hover:bg-white/5"
                >
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-graphite-text">
                    {statement.transferId}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-graphite-text">
                    {statement.account}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-graphite-muted">
                    {formatDate(statement.statementDate)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-graphite-muted">
                    {formatDateTime(statement.importDate)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-center dark:text-graphite-text">
                    <span className="inline-flex items-center px-3 py-1 rounded-full text-sm font-semibold bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300">
                      {statement.itemCount}
                    </span>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-center dark:text-graphite-text">
                    <span className={`inline-flex items-center px-2 py-1 rounded text-xs font-medium ${
                      statement.currency === 'EUR'
                        ? 'bg-yellow-100 text-yellow-800 dark:bg-amber-900/30 dark:text-amber-300'
                        : 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300'
                    }`}>
                      {statement.currency}
                    </span>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-center">
                    {getImportStatusIcon(statement.importResult)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {filteredItems.length === 0 && (
            <div className="text-center py-8">
              <p className="text-gray-500 dark:text-graphite-muted">Žádné bankovní výpisy nebyly nalezeny.</p>
            </div>
          )}
        </div>

        {/* Pagination - Compact */}
        {totalCount > 0 && (
          <div className="flex-shrink-0 bg-white px-3 py-2 flex items-center justify-between border-t border-gray-200 text-xs dark:bg-graphite-surface dark:border-graphite-border">
            <div className="flex-1 flex justify-between sm:hidden">
              <button
                onClick={() => handlePageChange(pageNumber - 1)}
                disabled={pageNumber <= 1}
                className="relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed dark:border-graphite-border dark:text-graphite-muted dark:bg-graphite-surface dark:hover:bg-white/5"
              >
                Předchozí
              </button>
              <button
                onClick={() => handlePageChange(pageNumber + 1)}
                disabled={pageNumber >= totalPages}
                className="ml-2 relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed dark:border-graphite-border dark:text-graphite-muted dark:bg-graphite-surface dark:hover:bg-white/5"
              >
                Další
              </button>
            </div>
            <div className="hidden sm:flex-1 sm:flex sm:items-center sm:justify-between">
              <div className="flex items-center space-x-3">
                <p className="text-xs text-gray-600 dark:text-graphite-muted">
                  {Math.min((pageNumber - 1) * pageSize + 1, totalCount)}-
                  {Math.min(pageNumber * pageSize, totalCount)} z {totalCount}
                  {hasActiveFilter ? (
                    <span className="text-gray-500 dark:text-graphite-faint"> (filtrováno)</span>
                  ) : (
                    ""
                  )}
                </p>
                <div className="flex items-center space-x-1">
                  <span className="text-xs text-gray-600 dark:text-graphite-muted">Zobrazit:</span>
                  <select
                    id="pageSize"
                    value={pageSize}
                    onChange={(e) => handlePageSizeChange(Number(e.target.value))}
                    className="border border-gray-300 rounded px-1 py-0.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text"
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
                    className="relative inline-flex items-center px-1 py-1 rounded-l border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed dark:border-graphite-border dark:bg-graphite-surface dark:text-graphite-muted dark:hover:bg-white/5"
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
                            ? "z-10 bg-indigo-50 border-indigo-500 text-indigo-600 dark:bg-graphite-accent/10 dark:border-graphite-accent dark:text-graphite-accent"
                            : "bg-white border-gray-300 text-gray-500 hover:bg-gray-50 dark:bg-graphite-surface dark:border-graphite-border dark:text-graphite-muted dark:hover:bg-white/5"
                        }`}
                      >
                        {pageNum}
                      </button>
                    );
                  })}

                  <button
                    onClick={() => handlePageChange(pageNumber + 1)}
                    disabled={pageNumber >= totalPages}
                    className="relative inline-flex items-center px-1 py-1 rounded-r border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed dark:border-graphite-border dark:bg-graphite-surface dark:text-graphite-muted dark:hover:bg-white/5"
                  >
                    <ChevronRight className="h-3 w-3" />
                  </button>
                </nav>
              </div>
            </div>
          </div>
        )}
      </div>

      {/* Import Modal */}
      {showImportModal && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4 dark:bg-graphite-surface dark:shadow-soft-dark">
            {/* Header */}
            <div className="flex justify-between items-center p-6 border-b border-gray-200 dark:border-graphite-border">
              <h2 className="text-xl font-semibold text-gray-900 flex items-center gap-2 dark:text-graphite-text">
                <Download className="h-5 w-5" />
                Import bankovních výpisů
              </h2>
              <button
                onClick={() => {
                  setShowImportModal(false);
                  resetImportForm();
                }}
                className="text-gray-400 hover:text-gray-600 transition-colors dark:text-graphite-faint dark:hover:text-graphite-muted"
              >
                ✕
              </button>
            </div>

            {/* Content */}
            <div className="p-6 space-y-6">
              {/* Account Selection */}
              <div>
                <label className="text-sm font-medium text-gray-900 mb-2 block dark:text-graphite-text">
                  Účet
                </label>
                {accountsLoading ? (
                  <div className="flex items-center gap-2 text-sm text-gray-500 dark:text-graphite-muted">
                    <Loader2 className="h-4 w-4 animate-spin" />
                    Načítání účtů...
                  </div>
                ) : (
                  <select
                    value={selectedAccount}
                    onChange={(e) => setSelectedAccount(e.target.value)}
                    className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:ring-emerald-500 focus:border-emerald-500 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text"
                  >
                    <option value="">Vyberte účet...</option>
                    {accounts?.map((account) => (
                      <option key={account.value} value={account.value}>
                        {account.label}
                      </option>
                    ))}
                  </select>
                )}
              </div>

              {/* Date Selection */}
              <div>
                <label className="text-sm font-medium text-gray-900 mb-2 block dark:text-graphite-text">
                  Datum výpisu
                </label>
                <input
                  type="date"
                  value={importDate}
                  onChange={(e) => setImportDate(e.target.value)}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:ring-emerald-500 focus:border-emerald-500 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text"
                />
              </div>

              {/* Info note */}
              <div className="bg-blue-50 border border-blue-200 rounded-lg p-3 dark:bg-blue-900/30 dark:border-blue-900/40">
                <p className="text-xs text-blue-700 dark:text-blue-300">
                  <strong>Poznámka:</strong> Import se provede pro vybraný účet a konkrétní datum výpisu.
                </p>
              </div>
            </div>

            {/* Footer */}
            <div className="border-t border-gray-200 px-6 py-4 bg-gray-50 rounded-b-lg dark:border-graphite-border dark:bg-graphite-surface-2">
              <div className="flex justify-end gap-3">
                <button
                  onClick={() => {
                    setShowImportModal(false);
                    resetImportForm();
                  }}
                  disabled={isImporting}
                  className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-emerald-500 focus:ring-offset-2 disabled:opacity-50 dark:text-graphite-muted dark:bg-graphite-surface-2 dark:border-graphite-border dark:hover:bg-white/5"
                >
                  Zrušit
                </button>
                <button
                  onClick={handleImportSubmit}
                  disabled={isImporting || !selectedAccount || !importDate}
                  className="px-4 py-2 text-sm font-medium text-white bg-emerald-600 border border-transparent rounded-md hover:bg-emerald-700 focus:outline-none focus:ring-2 focus:ring-emerald-500 focus:ring-offset-2 disabled:opacity-50 flex items-center gap-2"
                >
                  {isImporting ? (
                    <>
                      <Loader2 className="h-4 w-4 animate-spin" />
                      Importuje...
                    </>
                  ) : (
                    <>
                      <Download className="h-4 w-4" />
                      Spustit import
                    </>
                  )}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default ImportTab;