import React, { useState } from "react";
import { AlertCircle } from "lucide-react";
import { CatalogAutocomplete } from "../common/CatalogAutocomplete";
import { CatalogItemDto } from "../../api/generated/api-client";
import {
  getMonthRangeError,
  HISTORY_FLOOR_MONTH,
} from "../../api/hooks/useProductStatistics";
import {
  TimePeriod,
  resolveTimePeriod,
  getTimePeriodDisplayText,
} from "../../utils/timePeriod";

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

/**
 * The shared time-period buckets, minus CustomPeriod, which has no range of its own.
 * They resolve to day-precision ranges; this page only keeps the month of each end.
 */
const QUICK_PERIODS: TimePeriod[] = [
  TimePeriod.Y2Y,
  TimePeriod.PreviousQuarter,
  TimePeriod.FutureQuarter,
  TimePeriod.PreviousSeason,
  TimePeriod.Q9M,
];

interface QuickPeriodRange {
  dateFrom: string;
  dateTo: string;
}

function resolveQuickPeriodMonths(
  period: TimePeriod,
): QuickPeriodRange | null {
  const { primary } = resolveTimePeriod(period);
  if (!primary) {
    return null;
  }

  return {
    dateFrom: toMonthKey(primary.from),
    dateTo: toMonthKey(primary.to),
  };
}

const ProductStatisticsFilter: React.FC<ProductStatisticsFilterProps> = ({
  selectedProducts,
  onProductsChange,
  dateFrom,
  dateTo,
  onDateFromChange,
  onDateToChange,
}) => {
  // Same rule the query enforces, so a range the filter accepts always fires a request.
  const rangeError = getMonthRangeError(dateFrom, dateTo);
  const isAtCap = selectedProducts.length >= MAX_SELECTED_PRODUCTS;

  // react-select appends new picks, so the product dropped by the cap is the one just
  // clicked. Without this the click looks like a broken control rather than a refusal.
  const [wasCapExceeded, setWasCapExceeded] = useState(false);

  const handleQuickPeriod = (range: QuickPeriodRange) => {
    onDateFromChange(range.dateFrom);
    onDateToChange(range.dateTo);
  };

  const handleProductsChange = (items: CatalogItemDto[]) => {
    const mapped = items
      .filter((item) => Boolean(item.productCode))
      .map((item) => ({
        productCode: item.productCode as string,
        productName: item.productName ?? (item.productCode as string),
      }));

    setWasCapExceeded(mapped.length > MAX_SELECTED_PRODUCTS);

    // Cap defensively: the backend rejects more than MAX_SELECTED_PRODUCTS anyway.
    onProductsChange(mapped.slice(0, MAX_SELECTED_PRODUCTS));
  };

  return (
    <div className="flex-shrink-0 bg-white dark:bg-graphite-surface border border-gray-200 dark:border-graphite-border rounded-lg p-4 mb-4">
      <div className="flex flex-col lg:flex-row lg:items-start gap-4">
        <div className="flex-1 min-w-0">
          <label
            htmlFor="product-statistics-products"
            className="block text-sm font-medium text-gray-700 dark:text-graphite-text mb-1"
          >
            Produkty
          </label>
          <CatalogAutocomplete<CatalogItemDto>
            isMulti
            inputId="product-statistics-products"
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
          {wasCapExceeded ? (
            <div className="mt-1 flex items-center text-sm text-red-600 dark:text-red-400">
              <AlertCircle className="h-4 w-4 mr-1" />
              Porovnat lze nejvýše {MAX_SELECTED_PRODUCTS} produktů, další výběr
              byl ignorován.
            </div>
          ) : (
            isAtCap && (
              <div className="mt-1 text-sm text-gray-500 dark:text-graphite-muted">
                Maximálně {MAX_SELECTED_PRODUCTS} produktů.
              </div>
            )
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
            min={HISTORY_FLOOR_MONTH}
            max={defaultDateTo()}
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
            min={HISTORY_FLOOR_MONTH}
            max={defaultDateTo()}
            value={dateTo}
            onChange={(event) => onDateToChange(event.target.value)}
            className="border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md px-3 py-1.5 text-sm"
          />
        </div>

        <div>
          <span className="block text-sm font-medium text-gray-700 dark:text-graphite-text mb-1">
            Rychlé volby
          </span>
          <div className="flex flex-wrap gap-1.5">
            {QUICK_PERIODS.map((period) => {
              const range = resolveQuickPeriodMonths(period);
              if (!range) {
                return null;
              }

              const isActive =
                range.dateFrom === dateFrom && range.dateTo === dateTo;

              return (
                <button
                  key={period}
                  type="button"
                  onClick={() => handleQuickPeriod(range)}
                  aria-pressed={isActive}
                  title={`${range.dateFrom} – ${range.dateTo}`}
                  className={`px-2 py-1.5 text-sm rounded-md border transition-colors whitespace-nowrap ${
                    isActive
                      ? "border-indigo-500 bg-indigo-50 text-indigo-700 dark:bg-graphite-surface-2 dark:border-graphite-accent dark:text-graphite-accent"
                      : "border-gray-300 bg-white text-gray-700 hover:bg-gray-50 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-muted dark:hover:bg-graphite-hover"
                  }`}
                >
                  {getTimePeriodDisplayText(period)}
                </button>
              );
            })}
          </div>
        </div>
      </div>

      {rangeError && (
        <div className="mt-2 flex items-center text-sm text-red-600 dark:text-red-400">
          <AlertCircle className="h-4 w-4 mr-1" />
          {rangeError}
        </div>
      )}
    </div>
  );
};

export default ProductStatisticsFilter;
