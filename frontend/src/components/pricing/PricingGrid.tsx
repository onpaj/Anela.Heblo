import React from "react";
import { RotateCcw } from "lucide-react";
import { PricingEditField, PricingRowDto } from "../../api/generated/api-client";
import { formatCurrency, formatNumber, formatPercentage } from "../../utils/formatters";
import PricingEditableCell from "./PricingEditableCell";

export interface PricingGridProps {
  rows: PricingRowDto[];
  // (productCode, field, value) => void, committed on blur/Enter by PricingEditableCell.
  onEdit?: (productCode: string, field: PricingEditField, value: number) => void;
  editingDisabled?: boolean;
  // Called when the user clicks the per-row reset control on an edited row.
  onResetRow?: (productCode: string) => void;
  // Inline error messages for a rejected edit, keyed by pricingCellErrorKey().
  cellErrors?: Record<string, string>;
  // Bumped by the parent after every commit settles (success, rejection or network
  // failure) so every editable cell remounts and re-syncs its draft from `value` --
  // this is how a rejected/failed edit reverts the cell back to the prior value.
  resetToken?: number;
}

// Shared key format between PriceAnalysis (writer) and PricingGrid (reader) for the
// per-cell error lookup, so the two never drift apart.
export const pricingCellErrorKey = (productCode: string, field: PricingEditField): string =>
  `${productCode}::${field}`;

const EXCLUDED_ROW_TITLE =
  "Produkt je vyloučen ze souhrnu: chybí cena nebo historie marží";

// No pagination here on purpose: totals are computed over the whole filtered set,
// so paging the grid would make the totals bar lie about what it is summing.
const PricingGrid: React.FC<PricingGridProps> = ({
  rows,
  onEdit,
  editingDisabled = false,
  onResetRow,
  cellErrors = {},
  resetToken = 0,
}) => {
  const handleCommit = onEdit ?? (() => {});
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
              <th
                scope="col"
                className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                Akce
              </th>
            </tr>
          </thead>
          <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
            {rows.map((row) => {
              const isExcluded = row.isExcluded ?? false;
              const isEdited = row.isEdited ?? false;
              const productCode = row.productCode ?? "";

              const rowClassName = isExcluded
                ? "opacity-50 bg-gray-50 dark:bg-white/5"
                : isEdited
                  ? "bg-indigo-50 dark:bg-indigo-500/10 hover:bg-indigo-100 dark:hover:bg-indigo-500/20 transition-colors duration-150"
                  : "hover:bg-gray-50 dark:hover:bg-white/5 transition-colors duration-150";

              // A per-cell editable column: read-only text while editing is disabled
              // (or for the always-derived Materiál/Výroba columns), otherwise an
              // input that commits on blur/Enter. Re-mounted on every `resetToken`
              // bump so a rejected/failed edit reverts the draft back to `value`.
              const renderEditable = (field: PricingEditField, value: number | undefined) =>
                editingDisabled ? (
                  field === PricingEditField.ForecastQuantity ? (
                    formatNumber(value ?? null)
                  ) : field === PricingEditField.Price ? (
                    formatCurrency(value ?? null)
                  ) : (
                    formatPercentage(value ?? null)
                  )
                ) : (
                  <PricingEditableCell
                    key={`${productCode}-${field}-${resetToken}`}
                    value={value}
                    field={field}
                    productCode={productCode}
                    onCommit={handleCommit}
                    error={cellErrors[pricingCellErrorKey(productCode, field)]}
                  />
                );

              return (
                <tr
                  key={productCode}
                  data-testid={`pricing-row-${productCode}`}
                  className={rowClassName}
                  title={isExcluded ? EXCLUDED_ROW_TITLE : undefined}
                >
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-graphite-text">
                    {row.productCode}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-graphite-text">
                    {row.productName}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-gray-900 dark:text-graphite-text">
                    {renderEditable(PricingEditField.Price, row.price)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-gray-900 dark:text-graphite-text">
                    {formatCurrency(row.materialCost ?? null)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-gray-900 dark:text-graphite-text">
                    {formatCurrency(row.manufacturingCost ?? null)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-gray-600 dark:text-graphite-muted">
                    {renderEditable(PricingEditField.ForecastQuantity, row.forecastQuantity)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right font-semibold text-gray-900 dark:text-graphite-text">
                    {renderEditable(PricingEditField.M0Percentage, row.m0Percentage)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right font-semibold text-gray-900 dark:text-graphite-text">
                    {renderEditable(PricingEditField.M1Percentage, row.m1Percentage)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right">
                    {!editingDisabled && isEdited && (
                      <button
                        type="button"
                        data-testid={`pricing-row-reset-${productCode}`}
                        title="Obnovit původní hodnoty řádku"
                        onClick={() => onResetRow?.(productCode)}
                        className="inline-flex items-center justify-center rounded p-1 text-gray-400 hover:text-indigo-600 dark:text-graphite-faint dark:hover:text-indigo-400"
                      >
                        <RotateCcw className="h-4 w-4" />
                      </button>
                    )}
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
