import React, { useState } from "react";
import { Loader2, AlertCircle, ShieldCheck } from "lucide-react";
import { usePriceDivergenceReport } from "../../api/hooks/useProductPricing";
// Imported from the generated client directly (not from the hooks module) so this
// component keeps working when tests mock ../../api/hooks/useProductPricing.
import { PriceDivergenceKind, PriceDivergenceRowDto } from "../../api/generated/api-client";
import { formatCurrency } from "../../utils/formatters";

const KIND_LABELS: Record<PriceDivergenceKind, string> = {
  [PriceDivergenceKind.InAgreement]: "Ve shodě",
  [PriceDivergenceKind.FlexiDiffers]: "Flexi se liší",
  [PriceDivergenceKind.MissingInShoptet]: "Chybí v Shoptetu",
  [PriceDivergenceKind.MissingInFlexi]: "Chybí ve Flexi",
  [PriceDivergenceKind.FlexiPriceTypeUnknown]: "Neznámý typ ceny (Flexi)",
};

const KIND_BADGE_STYLES: Record<PriceDivergenceKind, string> = {
  [PriceDivergenceKind.InAgreement]: "bg-green-100 text-green-800 dark:bg-emerald-900/30 dark:text-emerald-300",
  [PriceDivergenceKind.FlexiDiffers]: "bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-300",
  [PriceDivergenceKind.MissingInShoptet]: "bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300",
  [PriceDivergenceKind.MissingInFlexi]: "bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300",
  [PriceDivergenceKind.FlexiPriceTypeUnknown]:
    "bg-gray-200 text-gray-800 dark:bg-graphite-surface-2 dark:text-graphite-muted",
};

const isDivergent = (kind: PriceDivergenceKind | undefined) => kind !== PriceDivergenceKind.InAgreement;

interface SummaryTileProps {
  label: string;
  value: number;
  emphasize?: boolean;
}

const SummaryTile: React.FC<SummaryTileProps> = ({ label, value, emphasize }) => (
  <div className="bg-white dark:bg-graphite-surface rounded-lg shadow dark:shadow-soft-dark px-4 py-3">
    <div className="text-xs text-gray-500 dark:text-graphite-muted uppercase tracking-wider">{label}</div>
    <div
      className={`text-2xl font-bold ${
        emphasize ? "text-amber-600 dark:text-amber-400" : "text-gray-900 dark:text-graphite-text"
      }`}
    >
      {value}
    </div>
  </div>
);

const DIVERGENT_COLUMN_COUNT = 8;

const PriceDivergenceReport: React.FC = () => {
  const { data, isLoading, error } = usePriceDivergenceReport();
  const [showDivergentOnly, setShowDivergentOnly] = useState(false);

  const rows = data?.rows ?? [];
  const summary = data?.summary;

  const visibleRows = showDivergentOnly ? rows.filter((row) => isDivergent(row.kind)) : rows;

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
      <div
        data-testid="divergence-readonly-banner"
        className="mb-4 flex items-center gap-2 px-4 py-3 bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-900/40 rounded-md text-sm text-blue-800 dark:text-blue-300"
      >
        <ShieldCheck className="h-4 w-4 flex-shrink-0" />
        <span>
          Pouze čtení — tato kontrola pouze porovnává ceny, nic se nezapisuje do Shoptetu, Flexi ani do Hebla.
        </span>
      </div>

      <div
        data-testid="divergence-summary"
        className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3 mb-6"
      >
        <SummaryTile label="Celkem v rozsahu" value={summary?.totalInScope ?? 0} />
        <SummaryTile label="Ve shodě" value={summary?.inAgreementCount ?? 0} />
        <SummaryTile label="Flexi se liší" value={summary?.flexiDiffersCount ?? 0} emphasize />
        <SummaryTile label="Chybí v Shoptetu" value={summary?.missingInShoptetCount ?? 0} emphasize />
        <SummaryTile label="Chybí ve Flexi" value={summary?.missingInFlexiCount ?? 0} emphasize />
        <SummaryTile label="Neznámý typ ceny" value={summary?.flexiPriceTypeUnknownCount ?? 0} emphasize />
      </div>

      <div className="flex items-center mb-4">
        <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-graphite-text cursor-pointer">
          <input
            type="checkbox"
            checked={showDivergentOnly}
            onChange={(e) => setShowDivergentOnly(e.target.checked)}
            className="rounded border-gray-300 dark:border-graphite-border"
          />
          Zobrazit pouze rozdílné
        </label>
      </div>

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
                visibleRows.map((row) => <DivergenceRow key={row.productCode} row={row} />)
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
};

const DivergenceRow: React.FC<{ row: PriceDivergenceRowDto }> = ({ row }) => (
  <tr className="hover:bg-gray-50 dark:hover:bg-white/5">
    <td className="px-4 py-3 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-graphite-text">
      {row.productCode}
    </td>
    <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-900 dark:text-graphite-text">{row.productName}</td>
    <td className="px-4 py-3 whitespace-nowrap text-right text-sm text-gray-900 dark:text-graphite-text">
      {row.shoptetPriceWithVat != null ? formatCurrency(row.shoptetPriceWithVat) : "—"}
    </td>
    <td className="px-4 py-3 whitespace-nowrap text-right text-sm text-gray-900 dark:text-graphite-text">
      {row.flexiPriceWithVat != null ? formatCurrency(row.flexiPriceWithVat) : "—"}
    </td>
    <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-900 dark:text-graphite-text">
      {row.flexiPriceType ?? "neznámý"}
    </td>
    <td className="px-4 py-3 whitespace-nowrap text-right text-sm text-gray-900 dark:text-graphite-text">
      {row.differenceWithVat != null ? formatCurrency(row.differenceWithVat) : "—"}
    </td>
    <td className="px-4 py-3 whitespace-nowrap text-right text-sm text-gray-900 dark:text-graphite-text">
      {row.differencePercent != null ? `${row.differencePercent} %` : "—"}
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
);

export default PriceDivergenceReport;
