import React from "react";
import { PricingEditField, PricingRowDto } from "../../api/generated/api-client";
import { formatCurrency, formatNumber, formatPercentage } from "../../utils/formatters";

export interface PricingGridProps {
  rows: PricingRowDto[];
  // Wired up in Task 9 (cell editing). Declared here, optional and unused, so the
  // signature does not change when editing lands.
  onEdit?: (productCode: string, field: PricingEditField, value: number) => void;
  editingDisabled?: boolean;
}

const EXCLUDED_ROW_TITLE =
  "Produkt je vyloučen ze souhrnu: chybí cena nebo historie marží";

// No pagination here on purpose: totals are computed over the whole filtered set,
// so paging the grid would make the totals bar lie about what it is summing.
const PricingGrid: React.FC<PricingGridProps> = ({ rows }) => {
  if (rows.length === 0) {
    return (
      <div className="flex-1 flex items-center justify-center h-64 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg text-gray-500 dark:text-graphite-muted">
        Žádné produkty neodpovídají zadaným filtrům.
      </div>
    );
  }

  return (
    <div className="flex-1 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg overflow-hidden flex flex-col min-h-0">
      <div className="flex-1 overflow-auto">
        <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
          <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0 z-10">
            <tr>
              <th
                scope="col"
                className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                Kód produktu
              </th>
              <th
                scope="col"
                className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                Název produktu
              </th>
              <th
                scope="col"
                className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                Cena
              </th>
              <th
                scope="col"
                className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                Materiál
              </th>
              <th
                scope="col"
                className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                Výroba
              </th>
              <th
                scope="col"
                className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                Prognóza ks
              </th>
              <th
                scope="col"
                className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                M0 %
              </th>
              <th
                scope="col"
                className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                M1 %
              </th>
            </tr>
          </thead>
          <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
            {rows.map((row) => {
              const isExcluded = row.isExcluded ?? false;

              return (
                <tr
                  key={row.productCode}
                  className={
                    isExcluded
                      ? "opacity-50 bg-gray-50 dark:bg-white/5"
                      : "hover:bg-gray-50 dark:hover:bg-white/5 transition-colors duration-150"
                  }
                  title={isExcluded ? EXCLUDED_ROW_TITLE : undefined}
                >
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-graphite-text">
                    {row.productCode}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-graphite-text">
                    {row.productName}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-gray-900 dark:text-graphite-text">
                    {formatCurrency(row.price ?? null)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-gray-900 dark:text-graphite-text">
                    {formatCurrency(row.materialCost ?? null)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-gray-900 dark:text-graphite-text">
                    {formatCurrency(row.manufacturingCost ?? null)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-gray-600 dark:text-graphite-muted">
                    {formatNumber(row.forecastQuantity ?? null)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right font-semibold text-gray-900 dark:text-graphite-text">
                    {formatPercentage(row.m0Percentage ?? null)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right font-semibold text-gray-900 dark:text-graphite-text">
                    {formatPercentage(row.m1Percentage ?? null)}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
};

export default PricingGrid;
