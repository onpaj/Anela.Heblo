import React, { useState } from "react";
import { Search, Filter, AlertCircle, AlertTriangle, Loader2, RotateCcw } from "lucide-react";
import { toast } from "react-hot-toast";
import {
  usePricingBaselineQuery,
  useRecalculatePricingMutation,
  PricingBaselineFilter,
} from "../../api/hooks/usePricingSimulator";
import {
  IPricingEditDto,
  IPricingOverrideDto,
  PricingEditField,
  PricingRowDto,
  PricingTotalsDto,
  ProductType,
  SwaggerException,
} from "../../api/generated/api-client";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import { useScreenView } from "../../telemetry/useScreenView";
import { handleApiError } from "../../utils/errorHandler";
import PricingTotalsBar from "../pricing/PricingTotalsBar";
import PricingGrid, { pricingCellErrorKey } from "../pricing/PricingGrid";

// The generated client throws SwaggerException for any non-2xx response, with
// `error.response` being the raw body text -- same pattern as
// LabelIdentificationScreen.resolveIdentifyErrorMessage. A parseable body with a
// structured errorCode is a REJECTED EDIT (bad price, negative cost, ...): show it
// inline on the offending cell. Anything else (network failure, 500, unparseable
// body) is treated as a transient failure: toast + stale totals badge instead.
const resolvePricingEditErrorMessage = (error: unknown): string | undefined => {
  if (!(error instanceof SwaggerException)) {
    return undefined;
  }
  try {
    const parsed = JSON.parse(error.response);
    if (parsed?.success === false && typeof parsed.errorCode === "string") {
      return handleApiError({ success: false, errorCode: parsed.errorCode, params: parsed.params });
    }
  } catch {
    // Not JSON -- fall through to the generic network-failure handling.
  }
  return undefined;
};

const GENERIC_RECALCULATE_FAILURE_TOAST =
  "Přepočet se nezdařil, zkuste to prosím znovu.";

const PriceAnalysis: React.FC = () => {
  // Filter states - separate input values from applied filters, same shape as
  // ProductMarginsList so the two Finance screens behave consistently.
  const [productNameInput, setProductNameInput] = useState("");
  const [productCodeInput, setProductCodeInput] = useState("");
  const [productTypeInput, setProductTypeInput] = useState<string>("");
  const [productNameFilter, setProductNameFilter] = useState("");
  const [productCodeFilter, setProductCodeFilter] = useState("");
  const [productTypeFilter, setProductTypeFilter] = useState<string>("");

  useScreenView("Finance", "PriceAnalysis");

  const filter: PricingBaselineFilter = {
    productCode: productCodeFilter || undefined,
    productName: productNameFilter || undefined,
    productType: (productTypeFilter || undefined) as ProductType | undefined,
  };

  const { data, isLoading, error, refetch } = usePricingBaselineQuery(filter);

  // Editing state. `recalculated` shadows the baseline query's rows/totals once the
  // first successful recalculate lands -- the mutation response is the new source of
  // truth and is never merged with the baseline. `overrides` mirrors the server's
  // authoritative override set (replaced wholesale from each response's `overrides`,
  // never hand-merged). `resetToken` forces every PricingEditableCell to remount
  // after a commit settles, which is how a rejected/failed edit reverts its cell.
  const [overrides, setOverrides] = useState<IPricingOverrideDto[]>([]);
  const [recalculated, setRecalculated] = useState<{
    rows: PricingRowDto[];
    totals: PricingTotalsDto | undefined;
  } | null>(null);
  const [cellErrors, setCellErrors] = useState<Record<string, string>>({});
  const [isTotalsStale, setIsTotalsStale] = useState(false);
  const [resetToken, setResetToken] = useState(0);

  const recalculateMutation = useRecalculatePricingMutation();

  const rows = recalculated?.rows ?? data?.rows ?? [];
  const totals = recalculated?.totals ?? data?.totals;

  const performRecalculate = async (
    nextOverrides: IPricingOverrideDto[],
    edit: IPricingEditDto | undefined,
    errorKey: string | undefined,
  ) => {
    if (errorKey) {
      setCellErrors((prev) => {
        if (!(errorKey in prev)) return prev;
        const { [errorKey]: _removed, ...rest } = prev;
        return rest;
      });
    }

    try {
      const response = await recalculateMutation.mutateAsync({
        productCode: filter.productCode,
        productName: filter.productName,
        productType: filter.productType,
        overrides: nextOverrides,
        edit,
      });
      setRecalculated({ rows: response.rows ?? [], totals: response.totals });
      setOverrides(response.overrides ?? []);
      setIsTotalsStale(false);
    } catch (caughtError) {
      const rejectionMessage = errorKey ? resolvePricingEditErrorMessage(caughtError) : undefined;
      if (rejectionMessage && errorKey) {
        setCellErrors((prev) => ({ ...prev, [errorKey]: rejectionMessage }));
      } else {
        setIsTotalsStale(true);
        toast.error(GENERIC_RECALCULATE_FAILURE_TOAST);
      }
    } finally {
      setResetToken((token) => token + 1);
    }
  };

  const handleCellEdit = (productCode: string, field: PricingEditField, value: number) => {
    void performRecalculate(
      overrides,
      { productCode, field, value },
      pricingCellErrorKey(productCode, field),
    );
  };

  const handleResetRow = (productCode: string) => {
    void performRecalculate(
      overrides.filter((override) => override.productCode !== productCode),
      undefined,
      undefined,
    );
  };

  const handleResetAll = () => {
    void performRecalculate([], undefined, undefined);
  };

  const handleApplyFilters = async () => {
    setProductNameFilter(productNameInput);
    setProductCodeFilter(productCodeInput);
    setProductTypeFilter(productTypeInput);
    await refetch();
  };

  const handleKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === "Enter") {
      handleApplyFilters();
    }
  };

  const handleClearFilters = async () => {
    setProductNameInput("");
    setProductCodeInput("");
    setProductTypeInput("");
    setProductNameFilter("");
    setProductCodeFilter("");
    setProductTypeFilter("");
    await refetch();
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500 dark:text-graphite-muted">
            Načítání analýzy cen...
          </div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600 dark:text-red-400">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání analýzy cen: {error.message}</div>
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
        <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">
          Analýza cen
        </h1>
      </div>

      {/* Filters - Fixed */}
      <div className="flex-shrink-0 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg p-4 mb-4">
        <div className="flex items-center justify-between flex-wrap gap-3">
          <div className="flex items-center gap-3 flex-1 min-w-0">
            <div className="flex items-center">
              <Filter className="h-4 w-4 text-gray-400 dark:text-graphite-faint mr-2" />
              <span className="text-sm font-medium text-gray-900 dark:text-graphite-text">
                Filtry:
              </span>
            </div>

            <div className="flex-1 max-w-xs">
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Search className="h-4 w-4 text-gray-400 dark:text-graphite-faint" />
                </div>
                <input
                  type="text"
                  id="productName"
                  value={productNameInput}
                  onChange={(e) => setProductNameInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md"
                  placeholder="Název produktu..."
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
                  id="productCode"
                  value={productCodeInput}
                  onChange={(e) => setProductCodeInput(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md"
                  placeholder="Kód produktu..."
                />
              </div>
            </div>

            <div className="flex-1 max-w-xs">
              <select
                value={productTypeInput}
                onChange={(e) => {
                  setProductTypeInput(e.target.value);
                  setProductTypeFilter(e.target.value);
                }}
                className="focus:ring-indigo-500 focus:border-indigo-500 block w-full py-2 px-3 sm:text-sm border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md"
              >
                <option value="">Výchozí (výrobky a zboží)</option>
                <option value="Product">Výrobky</option>
                <option value="Goods">Zboží</option>
                <option value="Material">Materiál</option>
                <option value="SemiProduct">Polotovar</option>
                <option value="Set">Dárkový balíček</option>
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

      {/* Totals band - sticky, stays visible while the grid below scrolls. Kept
          rendered from whatever the last successful fetch/recalculate produced
          while a new recalculate is in flight -- isRecalculating just adds the
          spinner, it never blanks the numbers. */}
      {totals && (
        <div className="flex-shrink-0">
          <div className="flex items-center justify-between gap-3 mb-2">
            {isTotalsStale && (
              <div
                data-testid="totals-stale-badge"
                className="flex items-center gap-1 text-xs font-medium text-orange-600 dark:text-amber-400"
              >
                <AlertTriangle className="h-3.5 w-3.5" />
                Souhrn nemusí odpovídat poslední úpravě (přepočet selhal)
              </div>
            )}
            {overrides.length > 0 && (
              <button
                type="button"
                data-testid="pricing-reset-all"
                onClick={handleResetAll}
                className="ml-auto inline-flex items-center gap-1 text-xs font-medium text-gray-500 hover:text-indigo-600 dark:text-graphite-muted dark:hover:text-indigo-400"
              >
                <RotateCcw className="h-3.5 w-3.5" />
                Zrušit všechny úpravy
              </button>
            )}
          </div>
          <PricingTotalsBar totals={totals} isRecalculating={recalculateMutation.isPending} />
        </div>
      )}

      {/* Product grid - every row, no pagination */}
      <PricingGrid
        rows={rows}
        onEdit={handleCellEdit}
        onResetRow={handleResetRow}
        cellErrors={cellErrors}
        resetToken={resetToken}
      />
    </div>
  );
};

export default PriceAnalysis;
