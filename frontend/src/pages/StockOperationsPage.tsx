import React, { useState, useEffect } from "react";
import { useSearchParams } from "react-router-dom";
import {
  Search,
  AlertCircle,
  ChevronUp,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  RefreshCw,
  X,
  CheckCircle,
  Clock,
  XCircle,
  AlertTriangle,
  Play,
  Calendar,
} from "lucide-react";
import { differenceInMinutes } from "date-fns";
import {
  useStockUpOperationsQuery,
  useRetryStockUpOperationMutation,
} from "../api/hooks/useStockUpOperations";
import { StockUpOperationState, StockUpSourceType } from "../api/generated/api-client";
import { CatalogAutocomplete } from "../components/common/CatalogAutocomplete";
import {
  catalogItemToCodeAndName,
  PRODUCT_TYPE_FILTERS,
} from "../components/common/CatalogAutocompleteAdapters";
import { PAGE_CONTAINER_HEIGHT } from "../constants/layout";

const StockOperationsPage: React.FC = () => {
  const [searchParams, setSearchParams] = useSearchParams();

  // Filter input states (what user is typing/selecting)
  const [stateInput, setStateInput] = useState<string>("Active");
  const [sourceTypeInput, setSourceTypeInput] = useState<string>("All");
  const [selectedProduct, setSelectedProduct] = useState<string | null>(null);
  const [documentNumberInput, setDocumentNumberInput] = useState("");
  const [dateFromInput, setDateFromInput] = useState<string>("");
  const [dateToInput, setDateToInput] = useState<string>("");

  // Applied filter states (sent to API)
  const [stateFilter, setStateFilter] = useState<string>("Active");
  const [sourceTypeFilter, setSourceTypeFilter] = useState<StockUpSourceType | undefined>();
  const [sourceIdFilter, setSourceIdFilter] = useState<number | undefined>();
  const [productCodeFilter, setProductCodeFilter] = useState<string | undefined>();
  const [documentNumberFilter, setDocumentNumberFilter] = useState<string | undefined>();
  const [createdFromFilter, setCreatedFromFilter] = useState<Date | undefined>();
  const [createdToFilter, setCreatedToFilter] = useState<Date | undefined>();

  // Pagination states
  const [currentPage, setCurrentPage] = useState(1);
  const pageSize = 50;

  // Sorting states
  const [sortBy, setSortBy] = useState("createdAt");
  const [sortDescending, setSortDescending] = useState(true);

  // UI state
  const [isFiltersCollapsed, setIsFiltersCollapsed] = useState(false);

  // Initialize filters from URL parameters
  useEffect(() => {
    const state = searchParams.get("state");
    const sourceType = searchParams.get("sourceType");
    const sourceId = searchParams.get("sourceId");
    const productCode = searchParams.get("productCode");
    const documentNumber = searchParams.get("documentNumber");
    const createdFrom = searchParams.get("createdFrom");
    const createdTo = searchParams.get("createdTo");
    const sort = searchParams.get("sortBy");
    const sortDesc = searchParams.get("sortDescending");
    const page = searchParams.get("page");

    if (state) {
      setStateInput(state);
      setStateFilter(state);
    }

    if (sourceType && sourceType !== "All") {
      setSourceTypeInput(sourceType);
      setSourceTypeFilter(sourceType as StockUpSourceType);
    }

    if (sourceId) {
      setSourceIdFilter(parseInt(sourceId));
    }

    if (productCode) {
      setProductCodeFilter(productCode);
      setSelectedProduct(`${productCode} - Loading...`);
    }

    if (documentNumber) {
      setDocumentNumberInput(documentNumber);
      setDocumentNumberFilter(documentNumber);
    }

    if (createdFrom) {
      setDateFromInput(createdFrom);
      setCreatedFromFilter(new Date(createdFrom));
    }

    if (createdTo) {
      setDateToInput(createdTo);
      setCreatedToFilter(new Date(createdTo));
    }

    if (sort) {
      setSortBy(sort);
    }

    if (sortDesc) {
      setSortDescending(sortDesc === "true");
    }

    if (page) {
      setCurrentPage(parseInt(page));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []); // Run only on mount

  // Fetch data
  const { data, isLoading, error, refetch } = useStockUpOperationsQuery({
    state: stateFilter,
    sourceType: sourceTypeFilter,
    sourceId: sourceIdFilter,
    productCode: productCodeFilter,
    documentNumber: documentNumberFilter,
    createdFrom: createdFromFilter,
    createdTo: createdToFilter,
    sortBy,
    sortDescending,
    page: currentPage,
    pageSize,
  });

  const retryMutation = useRetryStockUpOperationMutation();

  // Helper functions
  const isOperationStuck = (operation: any): boolean => {
    const now = new Date();
    if (operation.state === StockUpOperationState.Submitted && operation.submittedAt) {
      const minutesSinceSubmit = differenceInMinutes(now, new Date(operation.submittedAt));
      return minutesSinceSubmit > 5;
    }
    if (operation.state === StockUpOperationState.Pending) {
      const minutesSinceCreation = differenceInMinutes(now, new Date(operation.createdAt));
      return minutesSinceCreation > 10;
    }
    return false;
  };

  const getStuckMessage = (operation: any): string => {
    const now = new Date();
    if (operation.state === StockUpOperationState.Submitted && operation.submittedAt) {
      const minutes = differenceInMinutes(now, new Date(operation.submittedAt));
      return `Operace je ve stavu Submitted ${minutes} minut. Může být uvízlá.`;
    }
    if (operation.state === StockUpOperationState.Pending) {
      const minutes = differenceInMinutes(now, new Date(operation.createdAt));
      return `Operace je ve stavu Pending ${minutes} minut. Nebyla zpracována.`;
    }
    return "";
  };

  const getStateColor = (state: StockUpOperationState) => {
    switch (state) {
      case StockUpOperationState.Completed:
        return "bg-green-100 text-green-800";
      case StockUpOperationState.Failed:
        return "bg-red-100 text-red-800";
      case StockUpOperationState.Pending:
        return "bg-yellow-100 text-yellow-800";
      case StockUpOperationState.Submitted:
        return "bg-blue-100 text-blue-800";
      default:
        return "bg-gray-100 text-gray-800";
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
        return <RefreshCw className="h-4 w-4" />;
      default:
        return null;
    }
  };

  const canRetry = (state?: StockUpOperationState): boolean => {
    if (!state) return false;
    return (
      state === StockUpOperationState.Failed ||
      state === StockUpOperationState.Submitted ||
      state === StockUpOperationState.Pending
    );
  };

  const getRetryButtonColor = (state?: StockUpOperationState): string => {
    switch (state) {
      case StockUpOperationState.Failed:
        return "bg-red-600 hover:bg-red-700";
      case StockUpOperationState.Submitted:
        return "bg-orange-500 hover:bg-orange-600";
      case StockUpOperationState.Pending:
        return "bg-yellow-600 hover:bg-yellow-700";
      default:
        return "bg-gray-400";
    }
  };

  const getRetryButtonLabel = (state?: StockUpOperationState): string => {
    switch (state) {
      case StockUpOperationState.Failed:
        return "Opakovat";
      case StockUpOperationState.Submitted:
        return "Znovu zkusit";
      case StockUpOperationState.Pending:
        return "Spustit";
      default:
        return "Retry";
    }
  };

  const getRetryButtonIcon = (state?: StockUpOperationState): JSX.Element => {
    switch (state) {
      case StockUpOperationState.Failed:
        return <RefreshCw className="h-3 w-3" />;
      case StockUpOperationState.Submitted:
        return <AlertTriangle className="h-3 w-3" />;
      case StockUpOperationState.Pending:
        return <Play className="h-3 w-3" />;
      default:
        return <RefreshCw className="h-3 w-3" />;
    }
  };

  const formatDate = (date?: string | Date | null) => {
    if (!date) return "N/A";
    const dateObj = date instanceof Date ? date : new Date(date);
    return dateObj.toLocaleString("cs-CZ");
  };

  // Event handlers
  const handleApplyFilters = () => {
    // Apply all input states to filter states
    setStateFilter(stateInput);
    setSourceTypeFilter(
      sourceTypeInput === "All" ? undefined : (sourceTypeInput as StockUpSourceType)
    );
    setProductCodeFilter(selectedProduct ? selectedProduct.split(" - ")[0] : undefined);
    setDocumentNumberFilter(documentNumberInput || undefined);
    setCreatedFromFilter(dateFromInput ? new Date(dateFromInput) : undefined);
    setCreatedToFilter(dateToInput ? new Date(dateToInput) : undefined);

    // Reset to page 1
    setCurrentPage(1);

    // Update URL
    const params = new URLSearchParams();
    if (stateInput && stateInput !== "Active") params.set("state", stateInput);
    if (sourceTypeInput !== "All") params.set("sourceType", sourceTypeInput);
    if (selectedProduct) params.set("productCode", selectedProduct.split(" - ")[0]);
    if (documentNumberInput) params.set("documentNumber", documentNumberInput);
    if (dateFromInput) params.set("createdFrom", dateFromInput);
    if (dateToInput) params.set("createdTo", dateToInput);
    if (sortBy !== "createdAt") params.set("sortBy", sortBy);
    if (!sortDescending) params.set("sortDescending", "false");
    params.set("page", "1");

    setSearchParams(params);
  };

  const handleClearFilters = () => {
    // Reset all inputs to defaults
    setStateInput("Active");
    setSourceTypeInput("All");
    setSelectedProduct(null);
    setDocumentNumberInput("");
    setDateFromInput("");
    setDateToInput("");

    // Clear applied filters
    setStateFilter("Active");
    setSourceTypeFilter(undefined);
    setSourceIdFilter(undefined);
    setProductCodeFilter(undefined);
    setDocumentNumberFilter(undefined);
    setCreatedFromFilter(undefined);
    setCreatedToFilter(undefined);

    // Reset pagination and sorting
    setCurrentPage(1);
    setSortBy("createdAt");
    setSortDescending(true);

    // Clear URL params
    setSearchParams({});
  };

  const handleSort = (column: string) => {
    if (sortBy === column) {
      setSortDescending(!sortDescending);
    } else {
      setSortBy(column);
      setSortDescending(true);
    }
    setCurrentPage(1);
  };

  const handleProductSelect = (productCodeAndName: string | null) => {
    setSelectedProduct(productCodeAndName);
  };

  const handleRetryWithConfirmation = async (operation: any) => {
    const messages: Record<StockUpOperationState, string> = {
      [StockUpOperationState.Failed]: "Opravdu chcete znovu spustit tuto selhanou operaci?",
      [StockUpOperationState.Submitted]:
        "Tato operace je ve stavu Submitted. Pokud je uvízlá, retry může způsobit duplikát v Shoptet. Pokračovat?",
      [StockUpOperationState.Pending]: "Tato operace nebyla nikdy zpracována. Chcete ji spustit?",
      [StockUpOperationState.Completed]: "",
    };

    const confirmMessage =
      operation.state && messages[operation.state as StockUpOperationState]
        ? messages[operation.state as StockUpOperationState]
        : "Opravdu chcete znovu spustit tuto operaci?";

    if (window.confirm(confirmMessage)) {
      try {
        await retryMutation.mutateAsync(operation.id!);
        refetch();
      } catch (error) {
        console.error("Chyba při opakování operace:", error);
      }
    }
  };

  // Pagination helpers
  const totalCount = data?.totalCount || 0;
  const totalPages = Math.ceil(totalCount / pageSize);
  const skip = (currentPage - 1) * pageSize;

  const handlePageChange = (newPage: number) => {
    setCurrentPage(newPage);
    const params = new URLSearchParams(searchParams);
    params.set("page", newPage.toString());
    setSearchParams(params);
  };

  if (error) {
    return (
      <div className="p-6 bg-red-50 border border-red-200 rounded-lg">
        <div className="flex items-center gap-2">
          <AlertCircle className="h-5 w-5 text-red-600" />
          <div>
            <h3 className="text-red-800 font-semibold">Chyba při načítání operací</h3>
            <p className="text-red-600 text-sm mt-1">
              {error instanceof Error ? error.message : "Neznámá chyba"}
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

  const operations = data?.operations || [];

  return (
    <div className="flex flex-col w-full" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      {/* Header */}
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900">Operace naskladnění</h1>
      </div>

      {/* Collapsible Filter Panel */}
      <div className="flex-shrink-0 bg-white rounded-lg shadow mb-4">
        <div className="p-3 border-b border-gray-200">
          <div className="flex items-center justify-between">
            <button
              onClick={() => setIsFiltersCollapsed(!isFiltersCollapsed)}
              className="flex items-center space-x-2 text-sm font-medium text-gray-900 hover:text-gray-700"
            >
              {isFiltersCollapsed ? (
                <ChevronRight className="h-4 w-4" />
              ) : (
                <ChevronDown className="h-4 w-4" />
              )}
              <span>Filtry a nastavení</span>
              <span className="text-xs text-gray-500">({totalCount} operací)</span>
            </button>

            {/* Action buttons - always visible */}
            <div className="flex items-center space-x-3">
              <button
                onClick={() => refetch()}
                disabled={isLoading}
                className="flex items-center px-2 py-1 border border-gray-300 rounded-md shadow-sm text-xs font-medium text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50"
              >
                <RefreshCw className={`h-3 w-3 mr-1 ${isLoading ? "animate-spin" : ""}`} />
                {isFiltersCollapsed ? "" : "Obnovit"}
              </button>
            </div>
          </div>
        </div>

        {!isFiltersCollapsed && (
          <div className="p-3 space-y-4">
            {/* Row 1: State dropdown + SourceType radios + Product autocomplete */}
            <div className="flex gap-3 items-end">
              <div className="w-48">
                <label className="block text-xs font-medium text-gray-700 mb-1">Stav</label>
                <select
                  value={stateInput}
                  onChange={(e) => setStateInput(e.target.value)}
                  className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500"
                >
                  <option value="All">Všechny</option>
                  <option value="Active">Aktivní</option>
                  <option value="Pending">Pending</option>
                  <option value="Submitted">Submitted</option>
                  <option value="Failed">Failed</option>
                  <option value="Completed">Completed</option>
                </select>
              </div>

              <div className="flex items-center gap-3">
                <span className="text-xs font-medium text-gray-700">Typ zdroje:</span>
                <label className="flex items-center gap-1 text-xs">
                  <input
                    type="radio"
                    value="All"
                    checked={sourceTypeInput === "All"}
                    onChange={(e) => setSourceTypeInput(e.target.value)}
                    className="text-indigo-600"
                  />
                  Všechny
                </label>
                <label className="flex items-center gap-1 text-xs">
                  <input
                    type="radio"
                    value="TransportBox"
                    checked={sourceTypeInput === "TransportBox"}
                    onChange={(e) => setSourceTypeInput(e.target.value)}
                    className="text-indigo-600"
                  />
                  Transport Box
                </label>
                <label className="flex items-center gap-1 text-xs">
                  <input
                    type="radio"
                    value="GiftPackageManufacture"
                    checked={sourceTypeInput === "GiftPackageManufacture"}
                    onChange={(e) => setSourceTypeInput(e.target.value)}
                    className="text-indigo-600"
                  />
                  Balení dárků
                </label>
              </div>

              <div className="flex-1">
                <label className="block text-xs font-medium text-gray-700 mb-1">
                  Produkt
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

            {/* Row 2: Date range + Document number */}
            <div className="flex gap-3 items-end">
              <div className="w-40">
                <label className="block text-xs font-medium text-gray-700 mb-1">
                  Vytvořeno od
                </label>
                <input
                  type="date"
                  value={dateFromInput}
                  onChange={(e) => setDateFromInput(e.target.value)}
                  className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500"
                />
              </div>

              <div className="w-40">
                <label className="block text-xs font-medium text-gray-700 mb-1">
                  Vytvořeno do
                </label>
                <input
                  type="date"
                  value={dateToInput}
                  onChange={(e) => setDateToInput(e.target.value)}
                  className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500"
                />
              </div>

              <div className="flex-1">
                <label className="block text-xs font-medium text-gray-700 mb-1">
                  Číslo dokladu
                </label>
                <div className="relative">
                  <Search className="absolute left-2 top-1/2 transform -translate-y-1/2 h-3 w-3 text-gray-400" />
                  <input
                    type="text"
                    value={documentNumberInput}
                    onChange={(e) => setDocumentNumberInput(e.target.value)}
                    placeholder="Hledat číslo dokladu..."
                    className="pl-8 pr-8 w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500"
                  />
                  {documentNumberInput && (
                    <button
                      onClick={() => setDocumentNumberInput("")}
                      className="absolute right-2 top-1/2 transform -translate-y-1/2 text-gray-400 hover:text-gray-600"
                    >
                      <X className="h-3 w-3" />
                    </button>
                  )}
                </div>
              </div>
            </div>

            {/* Action buttons */}
            <div className="flex justify-end gap-2">
              <button
                onClick={handleClearFilters}
                className="flex items-center gap-1 px-3 py-1.5 text-xs text-gray-600 hover:text-gray-800 hover:bg-gray-100 rounded-md transition-colors border border-gray-300"
              >
                <X className="h-3 w-3" />
                Vymazat filtry
              </button>
              <button
                onClick={handleApplyFilters}
                className="flex items-center px-3 py-1.5 border border-transparent rounded-md shadow-sm text-xs font-medium text-white bg-indigo-600 hover:bg-indigo-700"
              >
                Použít filtry
              </button>
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
        ) : operations.length === 0 ? (
          <div className="flex-1 flex items-center justify-center">
            <div className="text-center">
              <AlertCircle className="h-12 w-12 text-gray-400 mx-auto mb-4" />
              <h3 className="text-lg font-medium text-gray-900 mb-2">Žádné výsledky</h3>
              <p className="text-gray-600">
                Zkuste upravit filtry nebo vyhledávací kritéria.
              </p>
            </div>
          </div>
        ) : (
          <div className="flex-1 overflow-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50 sticky top-0 z-10">
                <tr>
                  <th
                    onClick={() => handleSort("id")}
                    className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
                  >
                    <div className="flex items-center">
                      ID
                      {sortBy === "id" &&
                        (sortDescending ? (
                          <ChevronDown className="ml-1 h-4 w-4" />
                        ) : (
                          <ChevronUp className="ml-1 h-4 w-4" />
                        ))}
                    </div>
                  </th>
                  <th
                    onClick={() => handleSort("documentNumber")}
                    className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
                  >
                    <div className="flex items-center">
                      Číslo dokladu
                      {sortBy === "documentNumber" &&
                        (sortDescending ? (
                          <ChevronDown className="ml-1 h-4 w-4" />
                        ) : (
                          <ChevronUp className="ml-1 h-4 w-4" />
                        ))}
                    </div>
                  </th>
                  <th
                    onClick={() => handleSort("productCode")}
                    className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
                  >
                    <div className="flex items-center">
                      Kód produktu
                      {sortBy === "productCode" &&
                        (sortDescending ? (
                          <ChevronDown className="ml-1 h-4 w-4" />
                        ) : (
                          <ChevronUp className="ml-1 h-4 w-4" />
                        ))}
                    </div>
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Množství
                  </th>
                  <th
                    onClick={() => handleSort("state")}
                    className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
                  >
                    <div className="flex items-center">
                      Stav
                      {sortBy === "state" &&
                        (sortDescending ? (
                          <ChevronDown className="ml-1 h-4 w-4" />
                        ) : (
                          <ChevronUp className="ml-1 h-4 w-4" />
                        ))}
                    </div>
                  </th>
                  <th
                    onClick={() => handleSort("createdAt")}
                    className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
                  >
                    <div className="flex items-center">
                      Vytvořeno
                      {sortBy === "createdAt" &&
                        (sortDescending ? (
                          <ChevronDown className="ml-1 h-4 w-4" />
                        ) : (
                          <ChevronUp className="ml-1 h-4 w-4" />
                        ))}
                    </div>
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
                      <div className="flex items-center space-x-2">
                        <span
                          className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${getStateColor(
                            operation.state ?? StockUpOperationState.Pending
                          )}`}
                        >
                          {getStateIcon(operation.state ?? StockUpOperationState.Pending)}
                          <span className="ml-1">
                            {StockUpOperationState[operation.state ?? StockUpOperationState.Pending]}
                          </span>
                        </span>

                        {isOperationStuck(operation) && (
                          <span
                            className="inline-flex items-center text-red-600"
                            title={getStuckMessage(operation)}
                          >
                            <AlertTriangle className="h-4 w-4 animate-pulse" />
                          </span>
                        )}
                      </div>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                      <div className="flex items-center">
                        <Calendar className="h-4 w-4 text-gray-400 mr-1" />
                        {formatDate(operation.createdAt)}
                      </div>
                    </td>
                    <td className="px-6 py-4 text-sm text-red-600 max-w-xs truncate">
                      {operation.errorMessage || "-"}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm">
                      {canRetry(operation.state) && operation.id && (
                        <button
                          onClick={() => handleRetryWithConfirmation(operation)}
                          disabled={retryMutation.isPending}
                          className={`inline-flex items-center px-3 py-1 disabled:bg-gray-400 text-white text-xs font-medium rounded transition-colors duration-200 ${getRetryButtonColor(
                            operation.state
                          )}`}
                        >
                          {getRetryButtonIcon(operation.state)}
                          <span className="ml-1">{getRetryButtonLabel(operation.state)}</span>
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
        {totalCount > 0 && (
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
                  {skip + 1}-{Math.min(skip + pageSize, totalCount)} z {totalCount}
                </p>
              </div>
              <div>
                <nav
                  className="relative z-0 inline-flex rounded shadow-sm -space-x-px"
                  aria-label="Pagination"
                >
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
                            ? "z-10 bg-indigo-50 border-indigo-500 text-indigo-600"
                            : "bg-white border-gray-300 text-gray-500 hover:bg-gray-50"
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
    </div>
  );
};

export default StockOperationsPage;
