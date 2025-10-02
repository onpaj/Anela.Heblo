import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Search,
  Filter,
  ChevronDown,
  AlertTriangle,
} from "lucide-react";
import { ManufactureOrderState, ProductType } from "../../../api/generated/api-client";
import { GetManufactureOrdersRequest } from "../../../api/hooks/useManufactureOrders";
import CatalogAutocomplete from "../../common/CatalogAutocomplete";
import ResponsiblePersonCombobox from "../../common/ResponsiblePersonCombobox";

interface ManufactureOrderFiltersProps {
  onFiltersChange: (filters: GetManufactureOrdersRequest) => void;
  onApplyFilters: () => Promise<void>;
}

const ManufactureOrderFilters: React.FC<ManufactureOrderFiltersProps> = ({
  onFiltersChange,
  onApplyFilters,
}) => {
  const { t } = useTranslation();

  // Helper function to get translated state label
  const getStateLabel = (state: ManufactureOrderState): string => {
    return t(`manufacture.states.${ManufactureOrderState[state]}`);
  };

  // State for collapsible filter section
  const [isFiltersCollapsed, setIsFiltersCollapsed] = useState(true);

  // Filter states - separate input values from applied filters
  const [orderNumberInput, setOrderNumberInput] = useState("");
  const [stateInput, setStateInput] = useState<ManufactureOrderState | "">("");
  const [fromDateInput, setFromDateInput] = useState("");
  const [toDateInput, setToDateInput] = useState("");
  const [responsiblePersonInput, setResponsiblePersonInput] = useState("");
  const [productCodeInput, setProductCodeInput] = useState("");
  const [erpDocumentNumberInput, setErpDocumentNumberInput] = useState("");
  const [manualActionRequiredInput, setManualActionRequiredInput] = useState<boolean | null>(null);

  const [orderNumberFilter, setOrderNumberFilter] = useState("");
  const [stateFilter, setStateFilter] = useState<ManufactureOrderState | null>(null);
  const [responsiblePersonFilter, setResponsiblePersonFilter] = useState("");
  const [productCodeFilter, setProductCodeFilter] = useState("");
  const [erpDocumentNumberFilter, setErpDocumentNumberFilter] = useState("");
  const [manualActionRequiredFilter, setManualActionRequiredFilter] = useState<boolean | null>(null);

  // Handler for applying filters on Enter or button click
  const handleApplyFilters = async () => {
    setOrderNumberFilter(orderNumberInput);
    setStateFilter(stateInput === "" ? null : (stateInput as ManufactureOrderState));
    setResponsiblePersonFilter(responsiblePersonInput);
    setProductCodeFilter(productCodeInput);
    setErpDocumentNumberFilter(erpDocumentNumberInput);
    setManualActionRequiredFilter(manualActionRequiredInput);

    // Build request object
    const filters: GetManufactureOrdersRequest = {
      orderNumber: orderNumberInput || null,
      state: stateInput === "" ? null : (stateInput as ManufactureOrderState),
      dateFrom: fromDateInput ? new Date(fromDateInput) : null,
      dateTo: toDateInput ? new Date(toDateInput) : null,
      responsiblePerson: responsiblePersonInput || null,
      productCode: productCodeInput || null,
      erpDocumentNumber: erpDocumentNumberInput || null,
      manualActionRequired: manualActionRequiredInput,
    };

    onFiltersChange(filters);
    await onApplyFilters();
  };

  // Reset filters
  const handleResetFilters = () => {
    setOrderNumberInput("");
    setStateInput("");
    setFromDateInput("");
    setToDateInput("");
    setResponsiblePersonInput("");
    setProductCodeInput("");
    setErpDocumentNumberInput("");
    setManualActionRequiredInput(null);
    
    setOrderNumberFilter("");
    setStateFilter(null);
    setResponsiblePersonFilter("");
    setProductCodeFilter("");
    setErpDocumentNumberFilter("");
    setManualActionRequiredFilter(null);

    const emptyFilters: GetManufactureOrdersRequest = {
      orderNumber: null,
      state: null,
      dateFrom: null,
      dateTo: null,
      responsiblePerson: null,
      productCode: null,
      erpDocumentNumber: null,
      manualActionRequired: null,
    };

    onFiltersChange(emptyFilters);
  };

  // Handle Enter key for filters
  const handleKeyPress = (event: React.KeyboardEvent) => {
    if (event.key === "Enter") {
      handleApplyFilters();
    }
  };

  // Handle product code selection from autocomplete
  const handleProductCodeSelect = (productCode: string | null) => {
    setProductCodeInput(productCode || "");
  };

  return (
    <div className="flex-shrink-0 bg-white shadow rounded-lg mb-4">
      <div className="p-3 border-b border-gray-200">
        <div className="flex items-center justify-between">
          <button
            onClick={() => setIsFiltersCollapsed(!isFiltersCollapsed)}
            className="flex items-center space-x-2 text-sm font-medium text-gray-900 hover:text-gray-700"
          >
            <ChevronDown
              className={`h-4 w-4 transition-transform ${
                isFiltersCollapsed ? "-rotate-90" : ""
              }`}
            />
            <Filter className="h-4 w-4" />
            <span>Filtry</span>
          </button>
          
          {/* Quick summary when collapsed */}
          {isFiltersCollapsed && (
            <div className="flex items-center space-x-3 text-xs">
              {/* Quick filter info */}
              {(orderNumberFilter || stateFilter || responsiblePersonFilter || productCodeFilter || erpDocumentNumberFilter || manualActionRequiredFilter !== null) ? (
                <span className="text-gray-600">Aktivní filtry</span>
              ) : (
                <span className="text-gray-500">Klikněte pro rozbalení filtrů</span>
              )}
              
              {/* Quick apply button when collapsed */}
              <div className="flex items-center space-x-2">
                {/* Manual Action Required 3-state checkbox */}
                <div className="flex items-center space-x-1">
                  <button
                    onClick={() => {
                      // Cycle through: null (všechny) -> true (vyžaduje ruční zásah) -> false (nevyžaduje ruční zásah) -> null
                      if (manualActionRequiredInput === null) {
                        setManualActionRequiredInput(true);
                      } else if (manualActionRequiredInput === true) {
                        setManualActionRequiredInput(false);
                      } else {
                        setManualActionRequiredInput(null);
                      }
                      // Auto-apply filter
                      setTimeout(() => handleApplyFilters(), 50);
                    }}
                    className={`flex items-center space-x-1 px-2 py-1 rounded text-xs transition-colors ${
                      manualActionRequiredFilter === true
                        ? "bg-red-100 text-red-700 border border-red-300"
                        : manualActionRequiredFilter === false
                        ? "bg-green-100 text-green-700 border border-green-300"
                        : "bg-gray-100 text-gray-600 border border-gray-300 hover:bg-gray-200"
                    }`}
                    title={
                      manualActionRequiredFilter === true
                        ? "Aktuálně se zobrazují zakázky vyžadující ruční zásah"
                        : manualActionRequiredFilter === false
                        ? "Aktuálně se zobrazují zakázky nevyžadující ruční zásah"
                        : "Aktuálně se zobrazují všechny zakázky (klikněte pro filtrování)"
                    }
                  >
                    <AlertTriangle className="h-3 w-3" />
                    <span>
                      {manualActionRequiredFilter === true
                        ? "Vyžaduje ruční zásah"
                        : manualActionRequiredFilter === false
                        ? "Nevyžaduje ruční zásah"
                        : "Vše"}
                    </span>
                    {manualActionRequiredFilter !== null && (
                      <span className="ml-1 text-xs">
                        {manualActionRequiredFilter ? "✓" : "✗"}
                      </span>
                    )}
                  </button>
                </div>
                
                <CatalogAutocomplete<string>
                  value={productCodeInput}
                  onSelect={handleProductCodeSelect}
                  placeholder="Produkt..."
                  className="w-48 text-xs"
                  allowManualEntry={true}
                  productTypes={[ProductType.Product, ProductType.SemiProduct]}
                  itemAdapter={(item) => item.productCode || ""}
                  size="sm"
                />
                <button
                  onClick={handleApplyFilters}
                  className="px-6 py-1 bg-indigo-600 text-white rounded text-xs hover:bg-indigo-700 whitespace-nowrap"
                >
                  Hledat produkt
                </button>
              </div>
            </div>
          )}
        </div>
      </div>

      {!isFiltersCollapsed && (
        <div className="p-3 bg-gray-50">
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-3 text-xs">
            {/* Order Number */}
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">
                Číslo zakázky
              </label>
              <div className="relative">
                <Search className="absolute left-2 top-1/2 transform -translate-y-1/2 h-3 w-3 text-gray-400" />
                <input
                  type="text"
                  placeholder="Číslo zakázky"
                  value={orderNumberInput}
                  onChange={(e) => setOrderNumberInput(e.target.value)}
                  onKeyDown={handleKeyPress}
                  className="pl-7 w-full border border-gray-300 rounded px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                />
              </div>
            </div>

            {/* State */}
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">
                Stav zakázky
              </label>
              <select
                value={stateInput}
                onChange={(e) => setStateInput(e.target.value as ManufactureOrderState | "")}
                className="w-full border border-gray-300 rounded px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
              >
                <option value="">Všechny stavy</option>
                {Object.values(ManufactureOrderState).map((state) => (
                  <option key={state} value={state}>
                    {getStateLabel(state)}
                  </option>
                ))}
              </select>
            </div>

            {/* Date From */}
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">
                Od data
              </label>
              <input
                type="date"
                value={fromDateInput}
                onChange={(e) => setFromDateInput(e.target.value)}
                className="w-full border border-gray-300 rounded px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
              />
            </div>

            {/* Date To */}
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">
                Do data
              </label>
              <input
                type="date"
                value={toDateInput}
                onChange={(e) => setToDateInput(e.target.value)}
                className="w-full border border-gray-300 rounded px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
              />
            </div>

            {/* Responsible Person */}
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">
                Odpovědná osoba
              </label>
              <ResponsiblePersonCombobox
                value={responsiblePersonInput}
                onChange={(value) => setResponsiblePersonInput(value || "")}
                placeholder="Odpovědná osoba"
                allowManualEntry={true}
                className="w-full text-xs"
              />
            </div>

            {/* Product Code */}
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">
                Kód produktu
              </label>
              <CatalogAutocomplete<string>
                value={productCodeInput}
                onSelect={handleProductCodeSelect}
                placeholder="Kód produktu"
                className="w-full text-xs"
                allowManualEntry={true}
                productTypes={[ProductType.Product, ProductType.SemiProduct]}
                itemAdapter={(item) => item.productCode || ""}
              />
            </div>

            {/* ERP Document Number */}
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">
                ERP číslo dokladu
              </label>
              <div className="relative">
                <Search className="absolute left-2 top-1/2 transform -translate-y-1/2 h-3 w-3 text-gray-400" />
                <input
                  type="text"
                  placeholder="ERP číslo dokladu"
                  value={erpDocumentNumberInput}
                  onChange={(e) => setErpDocumentNumberInput(e.target.value)}
                  onKeyDown={handleKeyPress}
                  className="pl-7 w-full border border-gray-300 rounded px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
                />
              </div>
            </div>

            {/* Manual Action Required */}
            <div>
              <label className="block text-xs font-medium text-gray-700 mb-1">
                Ruční zásah
              </label>
              <select
                value={manualActionRequiredInput === null ? "" : manualActionRequiredInput.toString()}
                onChange={(e) => {
                  const value = e.target.value;
                  setManualActionRequiredInput(value === "" ? null : value === "true");
                }}
                className="w-full border border-gray-300 rounded px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"
              >
                <option value="">Vše</option>
                <option value="true">Vyžaduje ruční zásah</option>
                <option value="false">Nevyžaduje ruční zásah</option>
              </select>
            </div>
          </div>

          {/* Filter buttons */}
          <div className="mt-3 flex items-center gap-2">
            <button
              onClick={handleApplyFilters}
              className="bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-1.5 px-3 rounded text-xs transition-colors duration-200 flex items-center gap-1"
            >
              <Filter className="h-3 w-3" />
              Použít filtry
            </button>
            <button
              onClick={handleResetFilters}
              className="bg-gray-600 hover:bg-gray-700 text-white font-medium py-1.5 px-3 rounded text-xs transition-colors duration-200"
            >
              Vymazat filtry
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

export default ManufactureOrderFilters;