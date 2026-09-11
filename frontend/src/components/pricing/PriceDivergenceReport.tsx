import React, { useState } from "react";
import { Loader2, AlertCircle, AlertTriangle, ShieldCheck, Pencil, Search, Filter, RefreshCw } from "lucide-react";
import {
  usePriceDivergenceReport,
  useSetProductPrice,
  useSyncProductPrices,
  GENERIC_SYNC_ERROR,
} from "../../api/hooks/useProductPricing";
// Imported from the generated client directly (not from the hooks module) so this
// component keeps working when tests mock ../../api/hooks/useProductPricing.
import { PriceDivergenceKind, PriceDivergenceRowDto } from "../../api/generated/api-client";
import { countChangedRows } from "../../api/hooks/priceDivergenceMerge";
import { ErrorCodes } from "../../types/errors";
import { formatCurrency } from "../../utils/formatters";
import { getErrorMessage } from "../../utils/errorHandler";

const KIND_LABELS: Record<PriceDivergenceKind, string> = {
  [PriceDivergenceKind.InAgreement]: "Ve shodě",
  [PriceDivergenceKind.FlexiDiffers]: "Flexi se liší",
  [PriceDivergenceKind.MissingInShoptet]: "Chybí v Shoptetu",
  [PriceDivergenceKind.MissingInFlexi]: "Chybí ve Flexi",
  [PriceDivergenceKind.FlexiPriceTypeUnknown]: "Neznámý typ ceny (Flexi)",
  [PriceDivergenceKind.FlexiVatRateUnknown]: "Neznámá sazba DPH (Flexi)",
};

const KIND_BADGE_STYLES: Record<PriceDivergenceKind, string> = {
  [PriceDivergenceKind.InAgreement]: "bg-green-100 text-green-800 dark:bg-emerald-900/30 dark:text-emerald-300",
  [PriceDivergenceKind.FlexiDiffers]: "bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-300",
  [PriceDivergenceKind.MissingInShoptet]: "bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300",
  [PriceDivergenceKind.MissingInFlexi]: "bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300",
  [PriceDivergenceKind.FlexiPriceTypeUnknown]:
    "bg-gray-200 text-gray-800 dark:bg-graphite-surface-2 dark:text-graphite-muted",
  [PriceDivergenceKind.FlexiVatRateUnknown]:
    "bg-gray-200 text-gray-800 dark:bg-graphite-surface-2 dark:text-graphite-muted",
};

const isDivergent = (kind: PriceDivergenceKind | undefined) => kind !== PriceDivergenceKind.InAgreement;

interface SummaryTileProps {
  label: string;
  value: number;
  /** Identifies this tile's number so a test can assert the count under a specific label. */
  testId: string;
  emphasize?: boolean;
}

const SummaryTile: React.FC<SummaryTileProps> = ({ label, value, testId, emphasize }) => (
  <div className="bg-white dark:bg-graphite-surface rounded-lg shadow dark:shadow-soft-dark px-4 py-3">
    <div className="text-xs text-gray-500 dark:text-graphite-muted uppercase tracking-wider">{label}</div>
    <div
      data-testid={testId}
      className={`text-2xl font-bold ${
        emphasize ? "text-amber-600 dark:text-amber-400" : "text-gray-900 dark:text-graphite-text"
      }`}
    >
      {value}
    </div>
  </div>
);

const DIVERGENT_COLUMN_COUNT = 8;
const GENERIC_SET_PRICE_ERROR = "Cenu se nepodařilo uložit.";

// No server-side price ceiling is deliberately enforced (any ceiling would be an
// arbitrary magic number) — the operator's own confirmation is the only guard against a
// mistyped price reaching the live shop.
const LARGE_CHANGE_CONFIRM_THRESHOLD = 0.5;

// Mirrors the backend's GreaterThanOrEqualTo(0.01m) validator. Rejecting client-side avoids
// an avoidable round-trip to two live systems on obviously-invalid input (a blank field
// coerces to 0 via `Number("")`, which is finite and would otherwise slip past the guard).
const MIN_PRICE_WITH_VAT = 0.01;

// Mirrors SetProductPriceRequestValidator's ceiling so a mistyped extra digit is refused here
// with a message the operator can read, rather than as a validation error from the server.
const MAX_PRICE_WITH_VAT = 1_000_000;

const INVALID_PRICE_ERROR =
  `Zadejte cenu mezi ${MIN_PRICE_WITH_VAT} a ${MAX_PRICE_WITH_VAT} Kč (desetinná tečka).`;

/** What the last sync did, so a run that changed nothing is still visibly a run. */
interface SyncStatus {
  at: Date;
  changedCount: number;
}

// Czech counts one, a few (2-4) and many (5+) differently, and each form needs its own verb.
const FEW_UPPER_BOUND = 5;

const changedRowsLabel = (count: number): string => {
  if (count === 0) return "beze změn";
  if (count === 1) return "1 řádek se změnil";
  if (count < FEW_UPPER_BOUND) return `${count} řádky se změnily`;
  return `${count} řádků se změnilo`;
};

const formatSyncTime = (at: Date): string =>
  at.toLocaleTimeString("cs-CZ", { hour: "2-digit", minute: "2-digit" });

const readErrorCode = (error: unknown): string | undefined => {
  if (error && typeof error === "object" && "errorCode" in error) {
    const value = (error as { errorCode?: unknown }).errorCode;
    return typeof value === "string" ? value : undefined;
  }
  return undefined;
};

interface PriceDivergenceReportProps {
  canWrite: boolean;
}

const PriceDivergenceReport: React.FC<PriceDivergenceReportProps> = ({ canWrite }) => {
  const { data, isLoading, error } = usePriceDivergenceReport();
  const { mutateAsync: setPrice, isPending } = useSetProductPrice();
  const { mutateAsync: syncPrices, isPending: isSyncing } = useSyncProductPrices();
  const [showDivergentOnly, setShowDivergentOnly] = useState(false);
  // Input vs applied, mirroring CatalogList: typing does not re-filter until Enter, so the
  // table does not churn on every keystroke.
  const [productNameInput, setProductNameInput] = useState("");
  const [productCodeInput, setProductCodeInput] = useState("");
  const [productNameFilter, setProductNameFilter] = useState("");
  const [productCodeFilter, setProductCodeFilter] = useState("");
  const [editingCode, setEditingCode] = useState<string | null>(null);
  const [draftValue, setDraftValue] = useState("");
  const [rowErrors, setRowErrors] = useState<Record<string, string>>({});
  const [syncError, setSyncError] = useState<string | null>(null);
  const [syncStatus, setSyncStatus] = useState<SyncStatus | null>(null);

  const rows = data?.rows ?? [];
  const summary = data?.summary;

  const applyFilters = () => {
    setProductNameFilter(productNameInput);
    setProductCodeFilter(productCodeInput);
  };

  const handleFilterKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Enter") {
      applyFilters();
    }
  };

  // Filtering is client-side on purpose: unlike CatalogList this screen is not paginated and
  // already holds every row, so a round-trip per apply would re-read the whole Shoptet price
  // list and a Flexi query for data that is already here.
  const matchesText = (value: string | undefined, filter: string) =>
    filter === "" || (value ?? "").toLowerCase().includes(filter.toLowerCase());

  const visibleRows = rows.filter(
    (row) =>
      (!showDivergentOnly || isDivergent(row.kind)) &&
      matchesText(row.productName, productNameFilter) &&
      matchesText(row.productCode, productCodeFilter),
  );

  // Only rows the filters left on screen: the sync re-reads two live systems, so it covers
  // what the operator is actually looking at rather than the whole catalogue.
  const visibleProductCodes = visibleRows
    .map((row) => row.productCode)
    .filter((code): code is string => !!code);

  const handleSync = async () => {
    // Both cleared up front, not only on success: leaving the previous failure on screen for
    // the duration of the retry makes a running sync look like it has already failed again,
    // and a stale confirmation next to a fresh failure reads as both at once.
    setSyncError(null);
    setSyncStatus(null);
    // Captured before the await: `rows` re-reads the cache the sync itself is about to
    // rewrite, so comparing against it afterwards would compare the merged rows with
    // themselves and always report no change.
    const rowsBeforeSync = rows;
    try {
      const syncedRows = await syncPrices(visibleProductCodes);
      setSyncStatus({ at: new Date(), changedCount: countChangedRows(rowsBeforeSync, syncedRows) });
    } catch (err) {
      const errorCode = readErrorCode(err);
      setSyncError(errorCode ? getErrorMessage(errorCode as ErrorCodes) : GENERIC_SYNC_ERROR);
    }
  };

  const startEdit = (row: PriceDivergenceRowDto) => {
    if (!row.productCode) return;
    setEditingCode(row.productCode);
    setDraftValue(row.shoptetPriceWithVat != null ? String(row.shoptetPriceWithVat) : "");
  };

  const cancelEdit = () => {
    setEditingCode(null);
    setDraftValue("");
  };

  const saveEdit = async (row: PriceDivergenceRowDto) => {
    const productCode = row.productCode;
    if (!productCode) return;

    const priceWithVat = Number(draftValue);
    const isDraftUsable =
      draftValue.trim() !== "" &&
      Number.isFinite(priceWithVat) &&
      priceWithVat >= MIN_PRICE_WITH_VAT &&
      priceWithVat <= MAX_PRICE_WITH_VAT;
    if (!isDraftUsable) {
      // Returning silently made "Uložit" a dead button: <input type="number"> reports an
      // empty value for anything the browser cannot parse — the Czech decimal comma in
      // "420,50", for one — so the operator saw a click that did nothing and no reason why.
      // The row stays in edit mode so the value can be corrected in place.
      setRowErrors((prev) => ({ ...prev, [productCode]: INVALID_PRICE_ERROR }));
      return;
    }

    const currentPrice = row.shoptetPriceWithVat;
    const hasShoptetBaseline = currentPrice != null && currentPrice > 0;

    if (hasShoptetBaseline) {
      const changeRatio = Math.abs(priceWithVat - currentPrice) / currentPrice;
      if (changeRatio > LARGE_CHANGE_CONFIRM_THRESHOLD) {
        const confirmed = window.confirm(
          `Nová cena ${formatCurrency(priceWithVat)} se liší od aktuální ceny v Shoptetu ` +
            `${formatCurrency(currentPrice)} o více než 50 %. Opravdu chcete cenu uložit?`,
        );
        if (!confirmed) return;
      }
    } else {
      // No Shoptet price to compare against (e.g. a MissingInShoptet row) — there is no
      // ratio to gate on, so the operator's confirmation is the only judgement of whether the
      // number is right (the server's ceiling catches only absurd ones). Always ask, and name
      // this explicitly as a first price going straight onto the live shop.
      const confirmed = window.confirm(
        `Produkt ${row.productName} nemá v Shoptetu žádnou cenu k porovnání. Nastavuje se ` +
          `první cena ${formatCurrency(priceWithVat)} přímo v živém e-shopu. Opravdu chcete ` +
          `cenu uložit?`,
      );
      if (!confirmed) return;
    }

    try {
      await setPrice({ productCode, priceWithVat });
      setRowErrors((prev) => {
        const { [productCode]: _removed, ...rest } = prev;
        return rest;
      });
      setEditingCode(null);
      setDraftValue("");
    } catch (err) {
      const errorCode = readErrorCode(err);
      const message = errorCode ? getErrorMessage(errorCode as ErrorCodes) : GENERIC_SET_PRICE_ERROR;
      setRowErrors((prev) => ({ ...prev, [productCode]: message }));
      setEditingCode(null);
      setDraftValue("");
    }
  };

  if (isLoading) {
    return (
      <div className="p-6 flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500 dark:text-graphite-muted">Načítání kontroly cen...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-6 flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600 dark:text-red-400">
          <AlertCircle className="h-5 w-5" />
          <div>Chybu při načítání kontroly cen: {(error as Error).message}</div>
        </div>
      </div>
    );
  }

  return (
    <div>
      {canWrite ? (
        <div
          data-testid="divergence-readonly-banner"
          className="mb-4 flex items-center gap-2 px-4 py-3 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-900/40 rounded-md text-sm text-amber-800 dark:text-amber-300"
        >
          <AlertTriangle className="h-4 w-4 flex-shrink-0" />
          <span>
            Pozor — uložení zapisuje cenu přímo do živého Shoptetu a živého ERP Flexi. Ani jeden systém nemá
            testovací prostředí.
          </span>
        </div>
      ) : (
        <div
          data-testid="divergence-readonly-banner"
          className="mb-4 flex items-center gap-2 px-4 py-3 bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-900/40 rounded-md text-sm text-blue-800 dark:text-blue-300"
        >
          <ShieldCheck className="h-4 w-4 flex-shrink-0" />
          <span>
            Pouze čtení — tato kontrola pouze porovnává ceny, nic se nezapisuje do Shoptetu, Flexi ani do Hebla.
          </span>
        </div>
      )}

      <div
        data-testid="divergence-summary"
        className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3 mb-6"
      >
        <SummaryTile testId="summary-total-in-scope" label="Celkem v rozsahu" value={summary?.totalInScope ?? 0} />
        <SummaryTile testId="summary-in-agreement" label="Ve shodě" value={summary?.inAgreementCount ?? 0} />
        <SummaryTile testId="summary-flexi-differs" label="Flexi se liší" value={summary?.flexiDiffersCount ?? 0} emphasize />
        <SummaryTile testId="summary-missing-in-shoptet" label="Chybí v Shoptetu" value={summary?.missingInShoptetCount ?? 0} emphasize />
        <SummaryTile testId="summary-missing-in-flexi" label="Chybí ve Flexi" value={summary?.missingInFlexiCount ?? 0} emphasize />
        <SummaryTile testId="summary-flexi-price-type-unknown" label="Neznámý typ ceny" value={summary?.flexiPriceTypeUnknownCount ?? 0} emphasize />
        <SummaryTile testId="summary-flexi-vat-rate-unknown" label="Neznámá sazba DPH" value={summary?.flexiVatRateUnknownCount ?? 0} emphasize />
      </div>

      <div className="flex flex-wrap items-center gap-4 mb-4">
        <div className="flex items-center">
          <Filter className="h-4 w-4 text-gray-400 dark:text-graphite-faint mr-2" />
          <span className="text-sm font-medium text-gray-900 dark:text-graphite-text">Filtry:</span>
        </div>

        <div className="flex-1 max-w-xs">
          <div className="relative">
            <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
              <Search className="h-4 w-4 text-gray-400 dark:text-graphite-faint" />
            </div>
            <input
              type="text"
              id="priceProductName"
              value={productNameInput}
              onChange={(e) => setProductNameInput(e.target.value)}
              onKeyDown={handleFilterKeyDown}
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
              id="priceProductCode"
              value={productCodeInput}
              onChange={(e) => setProductCodeInput(e.target.value)}
              onKeyDown={handleFilterKeyDown}
              className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md"
              placeholder="Kód produktu..."
            />
          </div>
        </div>

        <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-graphite-text cursor-pointer">
          <input
            type="checkbox"
            checked={showDivergentOnly}
            onChange={(e) => setShowDivergentOnly(e.target.checked)}
            className="rounded border-gray-300 dark:border-graphite-border"
          />
          Zobrazit pouze rozdílné
        </label>

        <button
          type="button"
          data-testid="sync-prices-button"
          onClick={handleSync}
          disabled={isSyncing || visibleProductCodes.length === 0}
          aria-busy={isSyncing}
          title="Znovu načte ceny zobrazených produktů ze Shoptetu a z Flexi. Nic nezapisuje."
          className="inline-flex items-center gap-2 px-3 py-2 text-sm font-medium rounded-md border border-gray-300 dark:border-graphite-border bg-white dark:bg-graphite-surface-2 text-gray-700 dark:text-graphite-text hover:bg-gray-50 dark:hover:bg-graphite-surface disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {isSyncing ? (
            <Loader2 className="h-4 w-4 animate-spin" />
          ) : (
            <RefreshCw className="h-4 w-4" />
          )}
          {/* The label carries the state too, not just the spinner: a spinner swap alone
              changes nothing a screen reader announces. */}
          {isSyncing ? "Synchronizuji…" : `Synchronizovat (${visibleProductCodes.length})`}
        </button>
      </div>

      {/* The table is identical whenever the two systems held what the report already showed,
          so without this line a successful sync is indistinguishable from a dead button. */}
      {syncStatus && (
        <div
          role="status"
          data-testid="sync-prices-status"
          className="mb-4 text-sm text-gray-500 dark:text-graphite-muted"
        >
          Synchronizováno v {formatSyncTime(syncStatus.at)} — {changedRowsLabel(syncStatus.changedCount)}
        </div>
      )}

      {syncError && (
        <div
          role="alert"
          data-testid="sync-prices-error"
          className="mb-4 flex items-center gap-2 px-4 py-3 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-900/40 rounded-md text-sm text-red-700 dark:text-red-300"
        >
          <AlertCircle className="h-4 w-4 flex-shrink-0" />
          <span>{syncError}</span>
        </div>
      )}

      <div className="bg-white dark:bg-graphite-surface rounded-lg shadow dark:shadow-soft-dark overflow-hidden">
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
            <thead className="bg-gray-50 dark:bg-graphite-surface-2">
              <tr>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Kód</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Název</th>
                <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Shoptet (s DPH)</th>
                <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Flexi (s DPH)</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Typ ceny Flexi</th>
                <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Rozdíl</th>
                <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Rozdíl %</th>
                <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">Stav</th>
              </tr>
            </thead>
            <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
              {visibleRows.length === 0 ? (
                <tr>
                  <td colSpan={DIVERGENT_COLUMN_COUNT} className="px-4 py-12 text-center text-gray-500 dark:text-graphite-muted">
                    {rows.length === 0 ? "Žádné produkty k zobrazení." : "Žádné rozdílné produkty."}
                  </td>
                </tr>
              ) : (
                visibleRows.map((row) => (
                  <DivergenceRow
                    key={row.productCode}
                    row={row}
                    canWrite={canWrite}
                    isEditing={editingCode === row.productCode}
                    isSaving={isPending && editingCode === row.productCode}
                    draftValue={draftValue}
                    rowError={row.productCode ? rowErrors[row.productCode] : undefined}
                    onDraftChange={setDraftValue}
                    onEdit={() => startEdit(row)}
                    onCancel={cancelEdit}
                    onSave={() => saveEdit(row)}
                  />
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
};

interface DivergenceRowProps {
  row: PriceDivergenceRowDto;
  canWrite: boolean;
  isEditing: boolean;
  isSaving: boolean;
  draftValue: string;
  rowError?: string;
  onDraftChange: (value: string) => void;
  onEdit: () => void;
  onCancel: () => void;
  onSave: () => void;
}

const DivergenceRow: React.FC<DivergenceRowProps> = ({
  row,
  canWrite,
  isEditing,
  isSaving,
  draftValue,
  rowError,
  onDraftChange,
  onEdit,
  onCancel,
  onSave,
}) => (
  <>
    <tr className="hover:bg-gray-50 dark:hover:bg-white/5">
      <td className="px-4 py-3 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-graphite-text">
        {row.productCode}
      </td>
      <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-900 dark:text-graphite-text">{row.productName}</td>
      <td className="px-4 py-3 whitespace-nowrap text-right text-sm text-gray-900 dark:text-graphite-text">
        {isEditing ? (
          <div className="flex items-center justify-end gap-2">
            <input
              type="number"
              aria-label="Cena s DPH"
              value={draftValue}
              onChange={(e) => onDraftChange(e.target.value)}
              disabled={isSaving}
              className="w-24 rounded border border-gray-300 dark:border-graphite-border px-2 py-1 text-right bg-white dark:bg-graphite-surface-2 text-gray-900 dark:text-graphite-text disabled:opacity-50"
            />
            <button
              type="button"
              onClick={onSave}
              disabled={isSaving}
              className="text-indigo-600 dark:text-indigo-400 hover:underline disabled:opacity-50"
            >
              Uložit
            </button>
            <button
              type="button"
              onClick={onCancel}
              disabled={isSaving}
              className="text-gray-500 dark:text-graphite-muted hover:underline disabled:opacity-50"
            >
              Zrušit
            </button>
          </div>
        ) : (
          <div className="flex items-center justify-end gap-2">
            <span>{row.shoptetPriceWithVat != null ? formatCurrency(row.shoptetPriceWithVat) : "—"}</span>
            {canWrite && (
              <button
                type="button"
                aria-label={`Upravit cenu ${row.productName}`}
                onClick={onEdit}
                className="text-gray-400 hover:text-indigo-600 dark:hover:text-indigo-400"
              >
                <Pencil className="h-4 w-4" />
              </button>
            )}
          </div>
        )}
      </td>
      <td
        data-testid={`divergence-flexi-price-${row.productCode}`}
        className="px-4 py-3 whitespace-nowrap text-right text-sm text-gray-900 dark:text-graphite-text"
      >
        {row.flexiPriceWithVat != null ? formatCurrency(row.flexiPriceWithVat) : "—"}
      </td>
      <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-900 dark:text-graphite-text">
        {row.flexiPriceType ?? "neznámý"}
      </td>
      <td className="px-4 py-3 whitespace-nowrap text-right text-sm text-gray-900 dark:text-graphite-text">
        {row.differenceWithVat != null ? formatCurrency(row.differenceWithVat) : "—"}
      </td>
      <td className="px-4 py-3 whitespace-nowrap text-right text-sm text-gray-900 dark:text-graphite-text">
        {row.differencePercent != null ? `${row.differencePercent} %` : "—"}
      </td>
      <td className="px-4 py-3 whitespace-nowrap">
        <span
          data-testid={`divergence-kind-${row.productCode}`}
          className={`inline-flex px-2 py-1 text-xs font-semibold rounded-full ${
            row.kind !== undefined ? KIND_BADGE_STYLES[row.kind] : ""
          }`}
        >
          {row.kind !== undefined ? KIND_LABELS[row.kind] : "—"}
        </span>
      </td>
    </tr>
    {rowError && (
      <tr>
        <td colSpan={DIVERGENT_COLUMN_COUNT} className="px-4 py-2">
          <div role="alert" className="text-sm text-red-800 dark:text-red-300">
            {rowError}
          </div>
        </td>
      </tr>
    )}
  </>
);

export default PriceDivergenceReport;
