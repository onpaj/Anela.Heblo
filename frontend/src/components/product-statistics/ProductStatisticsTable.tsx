import React, { useState } from "react";
import { ProductStatisticsSeries } from "./ProductStatisticsChart";

export interface ProductStatisticsTableProps {
  months: string[];
  series: ProductStatisticsSeries[];
}

const numberFormatter = new Intl.NumberFormat("cs-CZ", {
  maximumFractionDigits: 2,
});

// Alignment is appended per column rather than baked in: Tailwind emits text-left
// before text-right, so a class string holding both would always render right.
const cellClass =
  "px-4 py-2 text-sm text-gray-900 dark:text-graphite-text whitespace-nowrap";
const headerClass =
  "px-4 py-2 text-xs font-medium uppercase tracking-wider text-gray-500 dark:text-graphite-muted whitespace-nowrap";

const tabId = (productCode: string) => `product-statistics-tab-${productCode}`;
const panelId = (productCode: string) =>
  `product-statistics-panel-${productCode}`;

interface ProductTableProps {
  months: string[];
  rowIndexes: number[];
  series: ProductStatisticsSeries;
}

const ProductTable: React.FC<ProductTableProps> = ({
  months,
  rowIndexes,
  series,
}) => {
  const total = series.values.reduce((sum, value) => sum + value, 0);

  return (
    <div className="overflow-x-auto border border-gray-200 dark:border-graphite-border rounded-lg">
      <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
        <thead className="bg-gray-50 dark:bg-graphite-surface-2">
          <tr>
            <th scope="col" className={`${headerClass} text-left`}>
              Měsíc
            </th>
            <th scope="col" className={`${headerClass} text-right`}>
              Množství
            </th>
          </tr>
        </thead>

        <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
          {rowIndexes.map((index) => (
            <tr key={months[index]}>
              <td className={`${cellClass} text-left font-mono`}>
                {months[index]}
              </td>
              <td className={`${cellClass} text-right`}>
                {numberFormatter.format(series.values[index] ?? 0)}
              </td>
            </tr>
          ))}
        </tbody>

        <tfoot className="bg-gray-50 dark:bg-graphite-surface-2">
          <tr>
            <td className={`${cellClass} text-left font-semibold`}>Celkem</td>
            <td className={`${cellClass} text-right font-semibold`}>
              {numberFormatter.format(total)}
            </td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
};

const ProductStatisticsTable: React.FC<ProductStatisticsTableProps> = ({
  months,
  series,
}) => {
  const [selectedProductCode, setSelectedProductCode] = useState<string | null>(
    null,
  );

  if (months.length === 0 || series.length === 0) {
    return (
      <div className="py-8 text-center text-sm text-gray-500 dark:text-graphite-muted">
        Žádná data k zobrazení
      </div>
    );
  }

  // The selection is resolved rather than synced: removing the active product from
  // the filter falls back to the first tab without an effect round-trip.
  const activeSeries =
    series.find((item) => item.productCode === selectedProductCode) ?? series[0];

  // Newest month first — the chart reads left-to-right ascending, but every table
  // in this app puts the most recent row on top.
  const rowIndexes = months.map((_, index) => index).reverse();

  return (
    <div className="flex gap-4">
      <div
        role="tablist"
        aria-orientation="vertical"
        aria-label="Produkt"
        className="flex flex-col flex-shrink-0 w-max max-w-sm border-r border-gray-200 dark:border-graphite-border"
      >
        {series.map((item) => (
          <button
            key={item.productCode}
            type="button"
            role="tab"
            id={tabId(item.productCode)}
            aria-selected={item.productCode === activeSeries.productCode}
            aria-controls={panelId(item.productCode)}
            onClick={() => setSelectedProductCode(item.productCode)}
            title={`${item.productName} (${item.productCode})`}
            className={`px-4 py-2 -mr-px text-sm font-medium text-left truncate border-r-2 transition-colors ${
              item.productCode === activeSeries.productCode
                ? "border-indigo-500 text-indigo-600 dark:text-graphite-accent dark:border-graphite-accent"
                : "border-transparent text-gray-500 hover:text-gray-700 dark:text-graphite-muted"
            }`}
          >
            {item.productName} ({item.productCode})
          </button>
        ))}
      </div>

      <div
        role="tabpanel"
        id={panelId(activeSeries.productCode)}
        aria-labelledby={tabId(activeSeries.productCode)}
        className="flex-1 min-w-0"
      >
        <ProductTable
          months={months}
          rowIndexes={rowIndexes}
          series={activeSeries}
        />
      </div>
    </div>
  );
};

export default ProductStatisticsTable;
