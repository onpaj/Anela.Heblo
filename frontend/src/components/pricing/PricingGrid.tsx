import React from "react";
import { AlertTriangle, RotateCcw } from "lucide-react";
import { PricingEditField, PricingRowDto } from "../../api/generated/api-client";
import { formatNumber } from "../../utils/formatters";
import PricingEditableCell from "./PricingEditableCell";
import { isInWorkGroup } from "./pricingWorkGroup";
import PricingBulkEditHeader from "./PricingBulkEditHeader";
import { PricingBulkEdit, PricingBulkEditField } from "./pricingBulkEdit";

export interface PricingGridProps {
  rows: PricingRowDto[];
  // (productCode, field, value) => void, committed on blur/Enter by PricingEditableCell.
  onEdit?: (productCode: string, field: PricingEditField, value: number) => void;
  editingDisabled?: boolean;
  // Called when the user clicks the per-row reset control on an edited row.
  onResetRow?: (productCode: string) => void;
  // Inline error messages for a rejected edit, keyed by pricingCellErrorKey().
  cellErrors?: Record<string, string>;
  // Called when the user clicks a row's product code or name to open its detail.
  onProductDetail: (productCode: string) => void;
  // Product codes the user pinned to the work group (edited rows belong to it anyway).
  workGroupProductCodes: ReadonlySet<string>;
  onToggleWorkGroup: (productCode: string) => void;
  // Column header: pins or unpins every row the grid currently lists.
  onToggleAllWorkGroup: (shouldPin: boolean) => void;
  // One relative change applied to every product the grid lists at once.
  onBulkEdit: (edit: PricingBulkEdit) => void;
}

// Shared key format between PriceAnalysis (writer) and PricingGrid (reader) for the
// per-cell error lookup, so the two never drift apart.
export const pricingCellErrorKey = (productCode: string, field: PricingEditField): string =>
  `${productCode}::${field}`;

const EXCLUDED_ROW_TITLE =
  "Produkt je vyloučen ze souhrnu: chybí cena nebo historie marží";

// A saved scenario snapshots the baseline it was decided against. GetPricingScenarioHandler
// compares that snapshot with today's catalog and sets BaselineDrifted per row, so a
// reopened scenario can say which rows were decided against numbers that have since moved.
const DRIFTED_ROW_TITLE =
  "Podklady se od uložení scénáře změnily: cena nebo náklady tohoto produktu se posunuly";

const DETAIL_LINK_TITLE = "Klikněte pro zobrazení detailu produktu";

const WORK_GROUP_TITLE = "Přidat produkt do pracovní skupiny";

// An edited row is in the work group through its own edit, so its checkbox has
// nothing to toggle -- unpicking it would be undone by the edit on the next render.
const WORK_GROUP_EDITED_TITLE =
  "Produkt má úpravu, v pracovní skupině je automaticky";

const WORK_GROUP_ALL_TITLE =
  "Přidat všechny zobrazené produkty do pracovní skupiny";

// The rest of the row is made of inline editors, so only the code and name cells
// open the product detail -- a click handler on the whole <tr> would fire while
// the user is editing a value.
const DETAIL_LINK_CLASS_NAME =
  "text-left hover:text-indigo-600 hover:underline dark:hover:text-indigo-400";

// No pagination here on purpose: totals are computed over the whole filtered set,
// so paging the grid would make the totals bar lie about what it is summing.
const PricingGrid: React.FC<PricingGridProps> = ({
  rows,
  onEdit,
  editingDisabled = false,
  onResetRow,
  cellErrors = {},
  onProductDetail,
  workGroupProductCodes,
  onToggleWorkGroup,
  onToggleAllWorkGroup,
  onBulkEdit,
}) => {
  const handleCommit = onEdit ?? (() => {});
  if (rows.length === 0) {
    return (
      <div className="flex-1 flex items-center justify-center h-64 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg text-gray-500 dark:text-graphite-muted">
        Žádné produkty neodpovídají zadaným filtrům.
      </div>
    );
  }

  // A bulk edit can only land on a row with usable data: an excluded row renders
  // read-only and is left out of every total, so promising the user it is included
  // would be a lie the toast then has to take back.
  const bulkEditableRowCount = rows.filter(
    (row) => !(row.isExcluded ?? false),
  ).length;

  const renderBulkEditHeader = (field: PricingBulkEditField, label: string) => (
    <PricingBulkEditHeader
      field={field}
      label={label}
      disabled={editingDisabled}
      productCount={bulkEditableRowCount}
      onApply={onBulkEdit}
    />
  );

  const rowsInWorkGroup = rows.filter((row) =>
    isInWorkGroup(row, workGroupProductCodes),
  ).length;
  const areAllRowsInWorkGroup = rowsInWorkGroup === rows.length;
  const isWorkGroupPartiallySelected =
    rowsInWorkGroup > 0 && !areAllRowsInWorkGroup;

  return (
    <div className="flex-1 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg overflow-hidden flex flex-col min-h-0">
      <div className="flex-1 overflow-auto">
        <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
          <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0 z-10">
            <tr>
              <th
                scope="col"
                className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                <input
                  type="checkbox"
                  data-testid="pricing-workgroup-all"
                  aria-label="Pracovní skupina: všechny zobrazené produkty"
                  title={WORK_GROUP_ALL_TITLE}
                  checked={areAllRowsInWorkGroup}
                  // React has no prop for the tri-state, so the partial selection is
                  // set on the node itself.
                  ref={(node) => {
                    if (node) {
                      node.indeterminate = isWorkGroupPartiallySelected;
                    }
                  }}
                  onChange={() => onToggleAllWorkGroup(!areAllRowsInWorkGroup)}
                  className="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500 dark:border-graphite-border dark:bg-graphite-surface-2"
                />
              </th>
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
                className="relative px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                {renderBulkEditHeader(PricingEditField.Price, "Cena")}
              </th>
              <th
                scope="col"
                className="relative px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                {renderBulkEditHeader(PricingEditField.MaterialCost, "Materiál")}
              </th>
              <th
                scope="col"
                className="relative px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                {renderBulkEditHeader(PricingEditField.ManufacturingCost, "Výroba")}
              </th>
              <th
                scope="col"
                className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                Prodáno 12m
              </th>
              <th
                scope="col"
                className="relative px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                {renderBulkEditHeader(PricingEditField.ForecastQuantity, "Prognóza ks")}
              </th>
              <th
                scope="col"
                className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                M0 Kč/%
              </th>
              <th
                scope="col"
                className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                M1 Kč/%
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
              const isBaselineDrifted = row.baselineDrifted ?? false;
              const productCode = row.productCode ?? "";

              const rowClassName = isExcluded
                ? "opacity-50 bg-gray-50 dark:bg-white/5"
                : isEdited
                  ? "bg-indigo-50 dark:bg-indigo-500/10 hover:bg-indigo-100 dark:hover:bg-indigo-500/20 transition-colors duration-150"
                  : "hover:bg-gray-50 dark:hover:bg-white/5 transition-colors duration-150";

              // Every editable column goes through the same cell, which formats its own
              // value, reports its change against the real state and opens the
              // value/percentage editor. An excluded row has no usable baseline, so it
              // is left out of every total -- editing one changed nothing on screen
              // while still marking the row edited, so it renders read-only.
              const renderEditable = (field: PricingEditField) => (
                <PricingEditableCell
                  row={row}
                  field={field}
                  onCommit={handleCommit}
                  readOnly={editingDisabled || isExcluded}
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
                  <td className="px-4 py-4 whitespace-nowrap">
                    <input
                      type="checkbox"
                      data-testid={`pricing-row-workgroup-${productCode}`}
                      aria-label={`Pracovní skupina: ${productCode}`}
                      title={isEdited ? WORK_GROUP_EDITED_TITLE : WORK_GROUP_TITLE}
                      checked={isInWorkGroup(row, workGroupProductCodes)}
                      disabled={isEdited}
                      onChange={() => onToggleWorkGroup(productCode)}
                      className="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500 disabled:opacity-60 dark:border-graphite-border dark:bg-graphite-surface-2"
                    />
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-graphite-text">
                    <button
                      type="button"
                      data-testid={`pricing-row-detail-code-${productCode}`}
                      title={DETAIL_LINK_TITLE}
                      onClick={() => onProductDetail(productCode)}
                      className={`inline-flex items-center gap-1 ${DETAIL_LINK_CLASS_NAME}`}
                    >
                      {row.productCode}
                      {isBaselineDrifted && (
                        <span
                          data-testid={`pricing-row-drift-${productCode}`}
                          title={DRIFTED_ROW_TITLE}
                          className="inline-flex text-orange-600 dark:text-amber-400"
                        >
                          <AlertTriangle className="h-3.5 w-3.5" aria-hidden="true" />
                        </span>
                      )}
                    </button>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-graphite-text">
                    <button
                      type="button"
                      data-testid={`pricing-row-detail-name-${productCode}`}
                      title={DETAIL_LINK_TITLE}
                      onClick={() => onProductDetail(productCode)}
                      className={DETAIL_LINK_CLASS_NAME}
                    >
                      {row.productName}
                    </button>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-gray-900 dark:text-graphite-text">
                    {renderEditable(PricingEditField.Price)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-gray-900 dark:text-graphite-text">
                    {renderEditable(PricingEditField.MaterialCost)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-gray-900 dark:text-graphite-text">
                    {renderEditable(PricingEditField.ManufacturingCost)}
                  </td>
                  <td
                    data-testid={`pricing-row-sold12m-${productCode}`}
                    className="px-6 py-4 whitespace-nowrap text-sm text-right text-gray-900 dark:text-graphite-text"
                  >
                    {formatNumber(row.baselineQuantity ?? null)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right text-gray-600 dark:text-graphite-muted">
                    {renderEditable(PricingEditField.ForecastQuantity)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right font-semibold text-gray-900 dark:text-graphite-text">
                    <div className="flex items-center justify-end gap-1">
                      <div className="w-28">
                        {renderEditable(PricingEditField.M0Amount)}
                      </div>
                      <span className="text-gray-400 dark:text-graphite-faint">/</span>
                      <div className="w-24">
                        {renderEditable(PricingEditField.M0Percentage)}
                      </div>
                    </div>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-right font-semibold text-gray-900 dark:text-graphite-text">
                    <div className="flex items-center justify-end gap-1">
                      <div className="w-28">
                        {renderEditable(PricingEditField.M1Amount)}
                      </div>
                      <span className="text-gray-400 dark:text-graphite-faint">/</span>
                      <div className="w-24">
                        {renderEditable(PricingEditField.M1Percentage)}
                      </div>
                    </div>
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
