import React from "react";
import { ProductStatisticsSeries } from "./ProductStatisticsChart";

export interface ProductStatisticsTableProps {
  months: string[];
  series: ProductStatisticsSeries[];
}

const numberFormatter = new Intl.NumberFormat("cs-CZ", {
  maximumFractionDigits: 2,
});

const ProductStatisticsTable: React.FC<ProductStatisticsTableProps> = ({
  months,
  series,
}) => {
  if (months.length === 0 || series.length === 0) {
    return (
      <div className="py-8 text-center text-sm text-gray-500 dark:text-graphite-muted">
        Žádná data k zobrazení
      </div>
    );
  }

  // Newest month first — the chart reads left-to-right ascending, but every table
  // in this app puts the most recent row on top.
  const rowIndexes = months.map((_, index) => index).reverse();

  const columnTotals = series.map((item) =>
    item.values.reduce((sum, value) => sum + value, 0),
  );

  const grandTotal = columnTotals.reduce((sum, value) => sum + value, 0);

  const rowTotal = (index: number) =>
    series.reduce((sum, item) => sum + (item.values[index] ?? 0), 0);

  const cellClass =
    "px-4 py-2 text-sm text-right text-gray-900 dark:text-graphite-text whitespace-nowrap";
  const headerClass =
    "px-4 py-2 text-xs font-medium text-right uppercase tracking-wider text-gray-500 dark:text-graphite-muted whitespace-nowrap";

  return (
    <div className="overflow-x-auto border border-gray-200 dark:border-graphite-border rounded-lg">
      <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
        <thead className="bg-gray-50 dark:bg-graphite-surface-2">
          <tr>
            <th scope="col" className={`${headerClass} text-left`}>
              Měsíc
            </th>
            {series.map((item) => (
              <th scope="col" key={item.productCode} className={headerClass}>
                {item.productName} ({item.productCode})
              </th>
            ))}
            <th scope="col" className={`${headerClass} font-semibold`}>
              Celkem
            </th>
          </tr>
        </thead>

        <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
          {rowIndexes.map((index) => (
            <tr key={months[index]}>
              <td className={`${cellClass} text-left font-mono`}>
                {months[index]}
              </td>
              {series.map((item) => (
                <td key={item.productCode} className={cellClass}>
                  {numberFormatter.format(item.values[index] ?? 0)}
                </td>
              ))}
              <td className={`${cellClass} font-semibold`}>
                {numberFormatter.format(rowTotal(index))}
              </td>
            </tr>
          ))}
        </tbody>

        <tfoot className="bg-gray-50 dark:bg-graphite-surface-2">
          <tr>
            <td className={`${cellClass} text-left font-semibold`}>Celkem</td>
            {columnTotals.map((total, index) => (
              <td
                key={series[index].productCode}
                className={`${cellClass} font-semibold`}
              >
                {numberFormatter.format(total)}
              </td>
            ))}
            <td className={`${cellClass} font-semibold`}>
              {numberFormatter.format(grandTotal)}
            </td>
          </tr>
        </tfoot>
      </table>
    </div>
  );
};

export default ProductStatisticsTable;
