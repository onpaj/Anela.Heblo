import React from "react";
import { Loader2 } from "lucide-react";
import { PricingTotalsDto } from "../../api/generated/api-client";
import { formatCurrency, formatPercentage } from "../../utils/formatters";

interface PricingTotalsBarProps {
  totals: PricingTotalsDto;
  isRecalculating?: boolean;
}

interface TotalsLineProps {
  label: string;
  before?: number;
  after?: number;
  delta?: number;
  deltaPercentage?: number;
}

const TotalsLine: React.FC<TotalsLineProps> = ({
  label,
  before,
  after,
  delta,
  deltaPercentage,
}) => {
  const isPositiveDelta = (delta ?? 0) >= 0;
  const deltaColorClass = isPositiveDelta
    ? "text-green-600 dark:text-emerald-400"
    : "text-red-600 dark:text-red-400";

  return (
    <div className="flex items-center justify-between gap-4 py-1.5">
      <span className="w-12 shrink-0 text-sm font-semibold text-gray-900 dark:text-graphite-text">
        {label}
      </span>
      <div className="flex flex-1 flex-wrap items-center justify-end gap-x-6 gap-y-1 text-sm">
        <span className="text-gray-500 dark:text-graphite-muted">
          Před:{" "}
          <span className="font-medium text-gray-900 dark:text-graphite-text">
            {formatCurrency(before ?? null)}
          </span>
        </span>
        <span className="text-gray-500 dark:text-graphite-muted">
          Po:{" "}
          <span className="font-medium text-gray-900 dark:text-graphite-text">
            {formatCurrency(after ?? null)}
          </span>
        </span>
        <span className={`font-semibold ${deltaColorClass}`}>
          Δ {formatCurrency(delta ?? null)} ({formatPercentage(deltaPercentage ?? null)})
        </span>
      </div>
    </div>
  );
};

// The totals bar is sticky at the top of the scroll container: it is the thing the
// user watches while editing rows far down the grid, so it must never scroll away.
// `top-0 z-10` plus a solid background keeps it pinned above the grid content.
const PricingTotalsBar: React.FC<PricingTotalsBarProps> = ({
  totals,
  isRecalculating = false,
}) => {
  const excludedProductCount = totals.excludedProductCount ?? 0;

  return (
    <div className="sticky top-0 z-10 flex-shrink-0 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg p-4 mb-4">
      <div className="flex items-center justify-between mb-2 gap-3 flex-wrap">
        <h2 className="text-sm font-semibold text-gray-900 dark:text-graphite-text">
          Souhrn
        </h2>
        <div className="flex items-center gap-3">
          {isRecalculating && (
            <div className="flex items-center gap-1 text-xs text-gray-500 dark:text-graphite-muted">
              <Loader2 className="h-3 w-3 animate-spin" />
              Přepočítávám...
            </div>
          )}
          {excludedProductCount > 0 && (
            <div
              data-testid="excluded-count"
              className="text-xs font-medium text-orange-600 dark:text-amber-400"
              title="Produkty bez ceny nebo historie marží jsou zobrazeny, ale nezapočítávají se do souhrnu"
            >
              Vyloučeno ze souhrnu: {excludedProductCount}{" "}
              {excludedProductCount === 1 ? "produkt" : "produktů"}
            </div>
          )}
        </div>
      </div>
      <div className="divide-y divide-gray-100 dark:divide-graphite-border">
        <TotalsLine
          label="Obrat"
          before={totals.revenueBefore}
          after={totals.revenueAfter}
          delta={totals.revenueDelta}
          deltaPercentage={totals.revenueDeltaPercentage}
        />
        <TotalsLine
          label="M0"
          before={totals.m0Before}
          after={totals.m0After}
          delta={totals.m0Delta}
          deltaPercentage={totals.m0DeltaPercentage}
        />
        <TotalsLine
          label="M1"
          before={totals.m1Before}
          after={totals.m1After}
          delta={totals.m1Delta}
          deltaPercentage={totals.m1DeltaPercentage}
        />
      </div>
    </div>
  );
};

export default PricingTotalsBar;
