import React from "react";
import { AlertCircle } from "lucide-react";
import { CatalogAutocomplete } from "../common/CatalogAutocomplete";
import { CatalogItemDto } from "../../api/generated/api-client";

export const MAX_SELECTED_PRODUCTS = 10;

export interface SelectedProduct {
  productCode: string;
  productName: string;
}

export interface ProductStatisticsFilterProps {
  selectedProducts: SelectedProduct[];
  onProductsChange: (products: SelectedProduct[]) => void;
  dateFrom: string;
  dateTo: string;
  onDateFromChange: (value: string) => void;
  onDateToChange: (value: string) => void;
}

function toMonthKey(date: Date): string {
  const year = date.getFullYear();
  const month = `${date.getMonth() + 1}`.padStart(2, "0");
  return `${year}-${month}`;
}

/** Current month, "yyyy-MM". */
export function defaultDateTo(now: Date = new Date()): string {
  return toMonthKey(now);
}

/**
 * Twelve months before the current month, which with an inclusive range gives the
 * 13-month window CatalogDetail already shows.
 */
export function defaultDateFrom(now: Date = new Date()): string {
  const from = new Date(now.getFullYear(), now.getMonth() - 12, 1);
  return toMonthKey(from);
}

const ProductStatisticsFilter: React.FC<ProductStatisticsFilterProps> = ({
  selectedProducts,
  onProductsChange,
  dateFrom,
  dateTo,
  onDateFromChange,
  onDateToChange,
}) => {
  const isRangeInverted = Boolean(dateFrom && dateTo && dateFrom > dateTo);
  const isAtCap = selectedProducts.length >= MAX_SELECTED_PRODUCTS;

  const handleProductsChange = (items: CatalogItemDto[]) => {
    const mapped = items
      .filter((item) => Boolean(item.productCode))
      .map((item) => ({
        productCode: item.productCode as string,
        productName: item.productName ?? (item.productCode as string),
      }));

    // Cap defensively: the backend rejects more than MAX_SELECTED_PRODUCTS anyway.
    onProductsChange(mapped.slice(0, MAX_SELECTED_PRODUCTS));
  };

  return (
    <div className="bg-white dark:bg-graphite-surface border border-gray-200 dark:border-graphite-border rounded-lg p-4 mb-4">
      <div className="flex flex-col lg:flex-row lg:items-start gap-4">
        <div className="flex-1 min-w-0">
          <label className="block text-sm font-medium text-gray-700 dark:text-graphite-text mb-1">
            Produkty
          </label>
          <CatalogAutocomplete<CatalogItemDto>
            isMulti
            values={selectedProducts.map(
              (product) =>
                new CatalogItemDto({
                  productCode: product.productCode,
                  productName: product.productName,
                }),
            )}
            onSelect={() => {}}
            onSelectMany={handleProductsChange}
            placeholder="Vyberte produkty..."
          />
          {isAtCap && (
            <div className="mt-1 text-sm text-gray-500 dark:text-graphite-muted">
              Maximálně {MAX_SELECTED_PRODUCTS} produktů.
            </div>
          )}
        </div>

        <div>
          <label
            htmlFor="product-statistics-date-from"
            className="block text-sm font-medium text-gray-700 dark:text-graphite-text mb-1"
          >
            Od
          </label>
          <input
            id="product-statistics-date-from"
            type="month"
            value={dateFrom}
            onChange={(event) => onDateFromChange(event.target.value)}
            className="border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md px-3 py-1.5 text-sm"
          />
        </div>

        <div>
          <label
            htmlFor="product-statistics-date-to"
            className="block text-sm font-medium text-gray-700 dark:text-graphite-text mb-1"
          >
            Do
          </label>
          <input
            id="product-statistics-date-to"
            type="month"
            value={dateTo}
            onChange={(event) => onDateToChange(event.target.value)}
            className="border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md px-3 py-1.5 text-sm"
          />
        </div>
      </div>

      {isRangeInverted && (
        <div className="mt-2 flex items-center text-sm text-red-600 dark:text-red-400">
          <AlertCircle className="h-4 w-4 mr-1" />
          Datum &quot;Od&quot; musí být dříve než &quot;Do&quot;.
        </div>
      )}
    </div>
  );
};

export default ProductStatisticsFilter;
