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
} from "../../api/hooks/useBankStatements";
import { formatDate, formatDateTime } from "../../utils/formatters";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";

const BankStatementsOverviewPage: React.FC = () => {
  // Filter states
  const [transferIdInput, setTransferIdInput] = useState("");
  const [accountInput, setAccountInput] = useState("");
  const [transferIdFilter, setTransferIdFilter] = useState("");
  const [accountFilter, setAccountFilter] = useState("");
  const [statementDateFrom, setStatementDateFrom] = useState("");
  const [statementDateTo, setStatementDateTo] = useState("");
  const [showOnlyErrors, setShowOnlyErrors] = useState(false);

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
  const handleApplyFilters = async () => {
    setTransferIdFilter(transferIdInput);
    setAccountFilter(accountInput);
    setPageNumber(1); // Reset to first page when applying filters
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
    setTransferIdInput("");
    setAccountInput("");
    setTransferIdFilter("");
    setAccountFilter("");
    setStatementDateFrom("");
    setStatementDateTo("");
    setShowOnlyErrors(false);
    setPageNumber(1);
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
        statementDate: importDate,
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

  // Status indicator for import result
  const getImportStatusIcon = (importResult: string) => {
    if (importResult === "OK") {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800">
          <CheckCircle className="h-3 w-3 mr-1" />
          Úspěch
        </span>
      );
    } else {
      return (
        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-800">
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
          <div className="text-gray-500">Načítání výpisů...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání výpisů: {error.message}</div>
        </div>
      </div>
    );
  }

  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      {/* Header - Fixed */}
      <div className="flex-shrink-0 mb-3">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-lg font-semibold text-gray-900 flex items-center gap-3">
              <CreditCard className="h-6 w-6 text-indigo-600" />
              Přehled bankovních výpisů
            </h1>
            <p className="text-gray-600 mt-1 text-sm">
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
                  id="transferId"
                  value={transferIdInput}
                  onChange={(e) => setTransferIdInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"
                  placeholder="Transfer ID..."
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
                  id="account"
                  value={accountInput}
                  onChange={(e) => setAccountInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"
                  placeholder="Účet..."
                />
              </div>
            </div>

            <div className="flex gap-2">
              <input
                type="date"
                value={statementDateFrom}
                onChange={(e) => setStatementDateFrom(e.target.value)}
                className="focus:ring-indigo-500 focus:border-indigo-500 block py-2 px-3 sm:text-sm border-gray-300 rounded-md"
                placeholder="Od"
              />
              <input
                type="date"
                value={statementDateTo}
                onChange={(e) => setStatementDateTo(e.target.value)}
                className="focus:ring-indigo-500 focus:border-indigo-500 block py-2 px-3 sm:text-sm border-gray-300 rounded-md"
                placeholder="Do"
              />
            </div>

            <div className="flex gap-2">
              <label className="flex items-center text-sm">
                <input
                  type="checkbox"
                  checked={showOnlyErrors}
                  onChange={(e) => setShowOnlyErrors(e.target.checked)}
                  className="h-4 w-4 text-indigo-600 border-gray-300 rounded focus:ring-indigo-500"
                />
                <span className="ml-1 text-gray-700">Jen chyby</span>
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
      <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0">
        <div className="flex-1 overflow-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50 sticky top-0 z-10">
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
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Měna
                </th>
                <th
                  scope="col"
                  className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Stav importu
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {filteredItems.map((statement) => (
                <tr
                  key={statement.id}
                  className="hover:bg-gray-50 transition-colors duration-150"
                >
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                    {statement.transferId}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                    {statement.account}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {formatDate(statement.statementDate)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {formatDateTime(statement.importDate)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-center">
                    <span className="inline-flex items-center px-3 py-1 rounded-full text-sm font-semibold bg-blue-100 text-blue-800">
                      {statement.itemCount}
                    </span>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-center">
                    <span className={`inline-flex items-center px-2 py-1 rounded text-xs font-medium ${
                      statement.currency === 'EUR' 
                        ? 'bg-yellow-100 text-yellow-800' 
                        : 'bg-blue-100 text-blue-800'
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
              <p className="text-gray-500">Žádné bankovní výpisy nebyly nalezeny.</p>
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
                  {transferIdFilter || accountFilter ? (
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
      </div>

      {/* Import Modal */}
      {showImportModal && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg shadow-xl max-w-md w-full mx-4">
            {/* Header */}
            <div className="flex justify-between items-center p-6 border-b border-gray-200">
              <h2 className="text-xl font-semibold text-gray-900 flex items-center gap-2">
                <Download className="h-5 w-5" />
                Import bankovních výpisů
              </h2>
              <button
                onClick={() => {
                  setShowImportModal(false);
                  resetImportForm();
                }}
                className="text-gray-400 hover:text-gray-600 transition-colors"
              >
                ✕
              </button>
            </div>

            {/* Content */}
            <div className="p-6 space-y-6">
              {/* Account Selection */}
              <div>
                <label className="text-sm font-medium text-gray-900 mb-2 block">
                  Účet
                </label>
                {accountsLoading ? (
                  <div className="flex items-center gap-2 text-sm text-gray-500">
                    <Loader2 className="h-4 w-4 animate-spin" />
                    Načítání účtů...
                  </div>
                ) : (
                  <select
                    value={selectedAccount}
                    onChange={(e) => setSelectedAccount(e.target.value)}
                    className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:ring-emerald-500 focus:border-emerald-500"
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
                <label className="text-sm font-medium text-gray-900 mb-2 block">
                  Datum výpisu
                </label>
                <input
                  type="date"
                  value={importDate}
                  onChange={(e) => setImportDate(e.target.value)}
                  className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:ring-emerald-500 focus:border-emerald-500"
                />
              </div>

              {/* Info note */}
              <div className="bg-blue-50 border border-blue-200 rounded-lg p-3">
                <p className="text-xs text-blue-700">
                  <strong>Poznámka:</strong> Import se provede pro vybraný účet a konkrétní datum výpisu.
                </p>
              </div>
            </div>

            {/* Footer */}
            <div className="border-t border-gray-200 px-6 py-4 bg-gray-50 rounded-b-lg">
              <div className="flex justify-end gap-3">
                <button
                  onClick={() => {
                    setShowImportModal(false);
                    resetImportForm();
                  }}
                  disabled={isImporting}
                  className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-emerald-500 focus:ring-offset-2 disabled:opacity-50"
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

export default BankStatementsOverviewPage;