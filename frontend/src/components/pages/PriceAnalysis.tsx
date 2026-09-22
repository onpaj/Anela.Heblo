import React, { useRef, useState } from "react";
import { Search, Filter, AlertCircle, AlertTriangle, Loader2, RotateCcw, Download } from "lucide-react";
import { toast } from "react-hot-toast";
import {
  usePricingBaselineQuery,
  usePricingSummaryQuery,
  useRecalculatePricingMutation,
  PricingBaselineFilter,
} from "../../api/hooks/usePricingSimulator";
import {
  IPricingEditDto,
  IPricingOverrideDto,
  PricingEditField,
  PricingRowDto,
  PricingScenarioSummaryDto,
  PricingTotalsDto,
  ProductType,
} from "../../api/generated/api-client";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import { useScreenView } from "../../telemetry/useScreenView";
import { resolveSwaggerErrorMessage } from "../../utils/errorHandler";
import PricingTotalsBar from "../pricing/PricingTotalsBar";
import PricingGrid, { pricingCellErrorKey } from "../pricing/PricingGrid";
import PricingScenarioBar from "../pricing/PricingScenarioBar";
import { filterWorkGroupRows } from "../pricing/pricingWorkGroup";
import { PricingBulkEdit, applyPricingBulkEdit } from "../pricing/pricingBulkEdit";
import { usePricingWorkGroup } from "../pricing/usePricingWorkGroup";
import { computePricingTotals } from "../pricing/pricingTotals";
import { czechPlural } from "../pricing/czechPlural";
import {
  DEFAULT_PRICING_SUMMARY_SCOPE,
  PRICING_SUMMARY_SCOPE_OPTIONS,
  PricingSummaryScope,
} from "../pricing/pricingSummaryScope";
import CatalogDetail from "./CatalogDetail";
import { exportPricingScenario } from "../pricing/exportPricingScenario";

// A rejected edit's parseable error body is a REJECTED EDIT (bad price, negative
// cost, ...): show it inline on the offending cell. Anything else (network failure,
// 500, unparseable body) is treated as a transient failure: toast + stale totals
// badge instead. resolveSwaggerErrorMessage is the shared SwaggerException-parsing
// helper (also used by PricingScenarioBar for save/delete failures).
const resolvePricingEditErrorMessage = resolveSwaggerErrorMessage;

const GENERIC_RECALCULATE_FAILURE_TOAST =
  "Přepočet se nezdařil, zkuste to prosím znovu.";

// A product is passed over for one of two reasons, and the message has to cover both:
// it has no catalogue value to scale from (a percentage of nothing is nothing -- a new
// product with no sales history cannot take a forecast change), or the result would be
// a value the server rejects anyway. Blaming "an invalid value" for the first case told
// the user their input was wrong when it was not.
const BULK_EDIT_SKIP_REASON =
  "nemají použitelnou původní hodnotu, nebo by výsledek nebyl platný";

const BULK_EDIT_NOTHING_APPLIED_TOAST = `Změnu nešlo použít na žádný z vybraných produktů: ${BULK_EDIT_SKIP_REASON}.`;

const bulkEditSkippedToast = (skippedCount: number): string =>
  `${skippedCount} ${czechPlural(skippedCount, {
    one: "produkt byl vynechán",
    few: "produkty byly vynechány",
    many: "produktů bylo vynecháno",
  })}: ${BULK_EDIT_SKIP_REASON}.`;

const GENERIC_EXPORT_FAILURE_TOAST =
  "Export ceníku se nezdařil, zkuste to prosím znovu.";

const PriceAnalysis: React.FC = () => {
  // Filter states - separate input values from applied filters, same shape as
  // ProductMarginsList so the two Finance screens behave consistently.
  const [productNameInput, setProductNameInput] = useState("");
  const [productCodeInput, setProductCodeInput] = useState("");
  const [productTypeInput, setProductTypeInput] = useState<string>("");
  const [productNameFilter, setProductNameFilter] = useState("");
  const [productCodeFilter, setProductCodeFilter] = useState("");
  const [productTypeFilter, setProductTypeFilter] = useState<string>("");

  // Product detail modal, opened from a row's product code / name cell.
  const [detailProductCode, setDetailProductCode] = useState<string | null>(null);

  // The work group -- the products the user is actually working on (edited rows plus
  // the ones pinned by hand). The grid and the summary each decide independently
  // whether they cover the whole filter result or just that group.
  const workGroup = usePricingWorkGroup();
  const [isWorkGroupFilterOn, setIsWorkGroupFilterOn] = useState(false);
  const [summaryScope, setSummaryScope] = useState<PricingSummaryScope>(
    DEFAULT_PRICING_SUMMARY_SCOPE,
  );

  useScreenView("Finance", "PriceAnalysis");

  const filter: PricingBaselineFilter = {
    productCode: productCodeFilter || undefined,
    productName: productNameFilter || undefined,
    productType: (productTypeFilter || undefined) as ProductType | undefined,
  };

  const { data, isLoading, error, refetch } = usePricingBaselineQuery(filter);

  // Editing state. `recalculated` shadows the baseline query's rows/totals once the
  // first successful recalculate lands -- the mutation response is the new source of
  // truth and is never merged with the baseline. `overrides` (and `overridesRef`,
  // its always-current mirror -- see performRecalculate) hold the server's
  // authoritative override set, replaced wholesale from each response's
  // `overrides`, never hand-merged. A cell holds no draft of its own -- it renders
  // whatever the latest response says and edits through a transient popover -- so a
  // failed recalculation needs no per-cell revert: the cell is already showing the
  // server's value.
  const [overrides, setOverrides] = useState<IPricingOverrideDto[]>([]);
  const overridesRef = useRef<IPricingOverrideDto[]>(overrides);
  // Tracks the tail of the current commit queue (see performRecalculate) so an
  // overlapping second commit is chained behind the first instead of racing it.
  const inFlightRequestRef = useRef<Promise<void> | null>(null);
  const [recalculated, setRecalculated] = useState<{
    rows: PricingRowDto[];
    totals: PricingTotalsDto | undefined;
  } | null>(null);
  const [cellErrors, setCellErrors] = useState<Record<string, string>>({});
  const [isTotalsStale, setIsTotalsStale] = useState(false);
  // Name of the currently active (loaded or last-saved) scenario -- used only as the
  // XLSX export's filename hint. PricingScenarioBar owns the save/load/delete flow
  // and its own name input; this is just what PriceAnalysis needs to label an export.
  const [activeScenarioName, setActiveScenarioName] = useState("");
  const [isExporting, setIsExporting] = useState(false);

  const recalculateMutation = useRecalculatePricingMutation();

  const rows = recalculated?.rows ?? data?.rows ?? [];
  const totals = recalculated?.totals ?? data?.totals;
  const hasEditedRows = rows.some((row) => row.isEdited);

  const visibleRows = isWorkGroupFilterOn
    ? filterWorkGroupRows(rows, workGroup.pinnedProductCodes)
    : rows;

  // "Filtrované produkty" is answered by the rows already on screen. The other two
  // scopes reach past the filter -- a product the name/code filter hides is missing
  // from `rows` entirely -- and are served from a second, unfiltered calculation.
  // With no name/code filter in play there is nothing to reach past: the main call
  // already covers the whole catalogue, so that second call is skipped entirely.
  const hasNarrowingFilter = Boolean(filter.productCode || filter.productName);
  const needsWiderSummary = summaryScope !== "filter" && hasNarrowingFilter;
  const summaryQuery = usePricingSummaryQuery(
    overrides,
    filter.productType,
    needsWiderSummary,
  );
  // The work-group total is a pure re-aggregation of rows the server already priced,
  // so ticking one more product into the group costs no further round trip.
  const summaryRows = needsWiderSummary ? (summaryQuery.data?.rows ?? []) : rows;
  const displayedTotals =
    summaryScope === "workGroup"
      ? computePricingTotals(
          filterWorkGroupRows(summaryRows, workGroup.pinnedProductCodes),
        )
      : needsWiderSummary
        ? summaryQuery.data?.totals
        : totals;
  const isWiderSummaryPending = needsWiderSummary && !summaryQuery.data;
  const summaryError = needsWiderSummary ? summaryQuery.error : null;

  // Commits are serialized through `inFlightRequestRef`: a commit fired while a
  // previous one is still in flight (e.g. blur cell A, then Enter in cell B before
  // A's response lands) is queued behind it rather than reading `overridesRef` at
  // the moment it was fired. Without this, B's request would be built from the
  // overrides captured BEFORE A's edit was applied, silently discarding A's edit
  // the instant B's response replaces the client's state. When nothing is in
  // flight, `run()` is invoked directly (not chained through `.then`), which keeps
  // the mutation call synchronous with the triggering blur/Enter for the common,
  // non-overlapping case.
  const performRecalculate = (
    // Returning null abandons the request: the callback is the only place that knows
    // the authoritative overrides, so it is also the only place that can find out
    // there is nothing left to ask the server for.
    buildOverrides: (current: IPricingOverrideDto[]) => IPricingOverrideDto[] | null,
    edit: IPricingEditDto | undefined,
    errorKey: string | undefined,
    // The filter this recalculation runs against. Defaults to the one currently applied;
    // applyFilter passes the NEW values explicitly, because its own setState calls have
    // not reached this render's `filter` yet (see applyFilter's own comment).
    requestFilter: PricingBaselineFilter = filter,
  ): Promise<void> => {
    const run = async () => {
      if (errorKey) {
        setCellErrors((prev) => {
          if (!(errorKey in prev)) return prev;
          const { [errorKey]: _removed, ...rest } = prev;
          return rest;
        });
      }

      const nextOverrides = buildOverrides(overridesRef.current);
      if (nextOverrides === null) {
        return;
      }

      try {
        const response = await recalculateMutation.mutateAsync({
          productCode: requestFilter.productCode,
          productName: requestFilter.productName,
          productType: requestFilter.productType,
          overrides: nextOverrides,
          edit,
        });
        const responseOverrides = response.overrides ?? [];
        overridesRef.current = responseOverrides;
        setRecalculated({ rows: response.rows ?? [], totals: response.totals });
        setOverrides(responseOverrides);
        setIsTotalsStale(false);
        // Every row in this response is freshly derived by the server, so no cell can
        // still be showing a rejected value -- including a cell OTHER than the one just
        // committed. Without this, a rejected edit on cell A kept its red ring and
        // message forever once the user moved on and successfully edited cell B.
        setCellErrors((prev) => (Object.keys(prev).length === 0 ? prev : {}));
      } catch (caughtError) {
        const rejectionMessage = errorKey ? resolvePricingEditErrorMessage(caughtError) : undefined;
        if (rejectionMessage && errorKey) {
          setCellErrors((prev) => ({ ...prev, [errorKey]: rejectionMessage }));
        } else {
          setIsTotalsStale(true);
          toast.error(GENERIC_RECALCULATE_FAILURE_TOAST);
        }
      }
    };

    const previous = inFlightRequestRef.current;
    const started: Promise<void> = previous ? previous.then(run, run) : run();
    const tracked = started.finally(() => {
      if (inFlightRequestRef.current === tracked) {
        inFlightRequestRef.current = null;
      }
    });
    inFlightRequestRef.current = tracked;
    return started;
  };

  const handleCellEdit = (productCode: string, field: PricingEditField, value: number) => {
    void performRecalculate(
      (current) => current,
      { productCode, field, value },
      pricingCellErrorKey(productCode, field),
    );
  };

  const handleResetRow = (productCode: string) => {
    void performRecalculate(
      (current) => current.filter((override) => override.productCode !== productCode),
      undefined,
      undefined,
    );
  };

  // A bulk edit acts on every row the grid lists: narrowing the grid -- by filter or
  // by the work group switch -- is how the user chooses what the change hits.
  const handleBulkEdit = (edit: PricingBulkEdit) => {
    // What a bulk edit does depends on the overrides it lands on -- a 0 % reset only
    // clears what is actually pinned -- so it is worked out inside the queue, against
    // whatever the previous commit left behind, and never against `overridesRef` as
    // it stands at click time. That ref is only ever written from a RESPONSE, so a
    // reset fired while an edit was still in flight used to read an empty override
    // set, conclude there was nothing to clear and return without a request or a
    // word to the user, silently dropping the undo.
    void performRecalculate(
      (current) => {
        const { overrides, appliedCount, skippedCount } = applyPricingBulkEdit(
          visibleRows,
          current,
          edit,
        );

        if (appliedCount === 0) {
          // A reset over rows that carry no override changes nothing and is not a
          // failure; anything else means every product refused the change.
          if (skippedCount > 0) {
            toast.error(BULK_EDIT_NOTHING_APPLIED_TOAST);
          }
          return null;
        }
        if (skippedCount > 0) {
          toast.error(bulkEditSkippedToast(skippedCount));
        }
        return overrides;
      },
      undefined,
      undefined,
    );
  };

  // The column header acts on what the grid currently lists, not on the whole
  // catalogue: what you see is what you pick.
  const handleToggleAllWorkGroup = (shouldPin: boolean) => {
    workGroup.setProductCodes(
      visibleRows.map((row) => row.productCode ?? ""),
      shouldPin,
    );
  };

  const handleResetAll = () => {
    void performRecalculate(() => [], undefined, undefined);
  };

  // A scenario load replaces rows/totals/overrides wholesale, exactly like a
  // successful recalculate response -- the server's `overrides` array is
  // authoritative and is never merged with whatever the client had before.
  const handleScenarioLoaded = (
    scenario: PricingScenarioSummaryDto,
    loadedRows: PricingRowDto[],
    loadedTotals: PricingTotalsDto | undefined,
    loadedOverrides: IPricingOverrideDto[],
  ) => {
    overridesRef.current = loadedOverrides;
    setOverrides(loadedOverrides);
    setRecalculated({ rows: loadedRows, totals: loadedTotals });
    setCellErrors({});
    setIsTotalsStale(false);
    setActiveScenarioName(scenario.name ?? "");
  };

  const handleScenarioSaved = (name: string) => {
    setActiveScenarioName(name);
  };

  const handleExport = async () => {
    if (!hasEditedRows || isExporting) {
      return;
    }
    setIsExporting(true);
    try {
      await exportPricingScenario(rows, activeScenarioName || "aktualni-analyza");
    } catch {
      // Without this the ExcelJS/createObjectURL failure escaped as an unhandled
      // rejection and the click looked like a no-op: no file, no message.
      toast.error(GENERIC_EXPORT_FAILURE_TOAST);
    } finally {
      setIsExporting(false);
    }
  };

  // Handed to PricingScenarioBar so a save waits for the edit its own click just
  // committed (the blur starts a recalculation) instead of persisting the previous
  // override set. Returns the overrides as of after the queue drains.
  const resolveOverridesForSave = async (): Promise<IPricingOverrideDto[]> => {
    const pending = inFlightRequestRef.current;
    if (pending) {
      await pending.catch(() => undefined);
    }
    return overridesRef.current;
  };

  // `rows` prefers `recalculated` over the baseline query, so once the first edit lands
  // a refetched baseline can never reach the screen on its own: applying or clearing a
  // filter would change nothing at all. Every filter change therefore goes through here.
  // With edits in play we re-run the recalculation against the NEW filter, so the user's
  // edits survive the filter change; with no edits there is nothing to recalculate and
  // dropping `recalculated` lets the refetched baseline through.
  const applyFilter = async (
    productName: string,
    productCode: string,
    productType: string,
  ) => {
    setProductNameFilter(productName);
    setProductCodeFilter(productCode);
    setProductTypeFilter(productType);

    // The `filter` captured by this closure still holds the PREVIOUS values -- the
    // setState calls above only take effect on the next render -- so the new filter is
    // built here and passed explicitly rather than read back out of state.
    const nextFilter: PricingBaselineFilter = {
      productCode: productCode || undefined,
      productName: productName || undefined,
      productType: (productType || undefined) as ProductType | undefined,
    };

    // Drain any commit still in flight BEFORE deciding which branch to take. The
    // decision reads `overridesRef`, which is only populated from a response -- so
    // during the very first edit's round trip it still reads empty, we took the
    // "no edits" branch, and that edit's response then re-shadowed the grid with rows
    // computed against the OLD filter. Unlike a commit, this path is not itself
    // chained through `inFlightRequestRef`, so it has to wait explicitly.
    const pending = inFlightRequestRef.current;
    if (pending) {
      await pending.catch(() => undefined);
    }

    if (overridesRef.current.length > 0) {
      await performRecalculate((current) => current, undefined, undefined, nextFilter);
    } else {
      setRecalculated(null);
      setCellErrors({});
      setIsTotalsStale(false);
    }

    await refetch();
  };

  const handleApplyFilters = () =>
    applyFilter(productNameInput, productCodeInput, productTypeInput);

  const handleKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === "Enter") {
      void handleApplyFilters();
    }
  };

  const handleClearFilters = async () => {
    setProductNameInput("");
    setProductCodeInput("");
    setProductTypeInput("");
    await applyFilter("", "", "");
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
                  void applyFilter(productNameFilter, productCodeFilter, e.target.value);
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

          <div className="flex items-center gap-3">
            <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-graphite-text whitespace-nowrap">
              <input
                type="checkbox"
                data-testid="pricing-work-group-filter"
                checked={isWorkGroupFilterOn}
                onChange={(e) => setIsWorkGroupFilterOn(e.target.checked)}
                className="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500 dark:border-graphite-border dark:bg-graphite-surface-2"
              />
              Pouze pracovní skupina
            </label>
            <button
              onClick={() => void handleApplyFilters()}
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

      {/* Scenarios + export - Fixed */}
      <div className="flex-shrink-0 mb-4">
        <div className="flex items-start justify-between gap-3 flex-wrap">
          <PricingScenarioBar
            productCode={filter.productCode}
            productName={filter.productName}
            productType={filter.productType}
            overrides={overrides}
            resolveOverrides={resolveOverridesForSave}
            onScenarioLoaded={handleScenarioLoaded}
            onScenarioSaved={handleScenarioSaved}
          />
          <button
            type="button"
            data-testid="pricing-export-xlsx"
            onClick={handleExport}
            disabled={!hasEditedRows || isExporting}
            title={
              hasEditedRows
                ? undefined
                : "Nejsou žádné úpravy k exportu"
            }
            className="inline-flex items-center gap-1 text-sm font-medium text-indigo-600 hover:text-indigo-700 disabled:text-gray-400 disabled:cursor-not-allowed dark:text-indigo-400 dark:hover:text-indigo-300 dark:disabled:text-graphite-faint"
          >
            <Download className="h-4 w-4" />
            Export ceníku (XLSX)
          </button>
        </div>
      </div>

      {/* Totals band - sticky, stays visible while the grid below scrolls. Kept
          rendered from whatever the last successful fetch/recalculate produced
          while a new recalculate is in flight -- isRecalculating just adds the
          spinner, it never blanks the numbers. Which products it adds up is the
          scope selector's business, not the grid filter's. */}
      <div className="flex-shrink-0">
        <div className="flex items-center justify-between gap-3 mb-2 flex-wrap">
          <div className="flex items-center gap-3 flex-wrap">
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
                className="inline-flex items-center gap-1 text-xs font-medium text-gray-500 hover:text-indigo-600 dark:text-graphite-muted dark:hover:text-indigo-400"
              >
                <RotateCcw className="h-3.5 w-3.5" />
                Zrušit všechny úpravy
              </button>
            )}
          </div>
          <fieldset className="ml-auto flex items-center gap-4">
            <legend className="sr-only">Rozsah souhrnu</legend>
            <span className="text-xs font-medium text-gray-500 dark:text-graphite-muted">
              Souhrn:
            </span>
            {PRICING_SUMMARY_SCOPE_OPTIONS.map((option) => (
              <label
                key={option.value}
                className="flex items-center gap-1.5 text-xs text-gray-600 dark:text-graphite-muted whitespace-nowrap cursor-pointer"
              >
                <input
                  type="radio"
                  name="pricing-summary-scope"
                  data-testid={`pricing-summary-scope-${option.value}`}
                  value={option.value}
                  checked={summaryScope === option.value}
                  onChange={() => setSummaryScope(option.value)}
                  className="h-3.5 w-3.5 border-gray-300 text-indigo-600 focus:ring-indigo-500 dark:border-graphite-border dark:bg-graphite-surface-2"
                />
                {option.label}
              </label>
            ))}
          </fieldset>
        </div>
        {summaryError ? (
          <div
            data-testid="pricing-summary-error"
            className="flex items-center gap-2 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg p-4 mb-4 text-sm text-orange-600 dark:text-amber-400"
          >
            <AlertTriangle className="h-4 w-4" />
            Souhrn pro zvolený rozsah se nepodařilo načíst.
          </div>
        ) : isWiderSummaryPending ? (
          // The wider summary is a different population than the grid, so showing the
          // filter's numbers under an "all products" label would simply be wrong.
          <div
            data-testid="pricing-summary-loading"
            className="flex items-center gap-2 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg p-4 mb-4 text-sm text-gray-500 dark:text-graphite-muted"
          >
            <Loader2 className="h-4 w-4 animate-spin" />
            Načítám souhrn...
          </div>
        ) : (
          displayedTotals && (
            <PricingTotalsBar
              totals={displayedTotals}
              isRecalculating={
                recalculateMutation.isPending || summaryQuery.isFetching
              }
            />
          )
        )}
      </div>

      {/* Product grid - every row, no pagination */}
      <PricingGrid
        rows={visibleRows}
        onEdit={handleCellEdit}
        onResetRow={handleResetRow}
        cellErrors={cellErrors}
        onProductDetail={setDetailProductCode}
        workGroupProductCodes={workGroup.pinnedProductCodes}
        onToggleWorkGroup={workGroup.toggleProductCode}
        onToggleAllWorkGroup={handleToggleAllWorkGroup}
        onBulkEdit={handleBulkEdit}
      />

      <CatalogDetail
        productCode={detailProductCode}
        isOpen={detailProductCode !== null}
        onClose={() => setDetailProductCode(null)}
      />
    </div>
  );
};

export default PriceAnalysis;
