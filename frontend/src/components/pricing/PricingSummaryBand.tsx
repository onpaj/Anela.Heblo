import React from "react";
import { AlertTriangle, Loader2, RotateCcw } from "lucide-react";
import { IPricingTotalsDto } from "../../api/generated/api-client";
import PricingTotalsBar from "./PricingTotalsBar";
import {
  PRICING_SUMMARY_SCOPE_OPTIONS,
  PricingSummaryScope,
} from "./pricingSummaryScope";

export interface PricingSummaryBandProps {
  totals?: IPricingTotalsDto;
  scope: PricingSummaryScope;
  onScopeChange: (scope: PricingSummaryScope) => void;
  /** True while a scope wider than the grid has nothing to show yet. */
  isLoading: boolean;
  /** True while the numbers on screen are being brought up to date. */
  isRecalculating: boolean;
  /** Set when the recalculation behind the totals failed and they may be behind. */
  isStale: boolean;
  /** Set when the wider-scope summary could not be loaded at all. */
  hasError: boolean;
  /** Absent while the user has made no edits to undo. */
  onResetAll?: () => void;
}

/**
 * The totals band and the control that decides which products it adds up. Sticky, so
 * it stays visible while the grid below scrolls, and it is never blanked by a
 * recalculation in flight -- isRecalculating only adds the spinner. Which products it
 * covers is this scope selector's business, not the grid filter's.
 */
const PricingSummaryBand: React.FC<PricingSummaryBandProps> = ({
  totals,
  scope,
  onScopeChange,
  isLoading,
  isRecalculating,
  isStale,
  hasError,
  onResetAll,
}) => (
  <div className="flex-shrink-0">
    <div className="flex items-center justify-between gap-3 mb-2 flex-wrap">
      <div className="flex items-center gap-3 flex-wrap">
        {isStale && (
          <div
            data-testid="totals-stale-badge"
            className="flex items-center gap-1 text-xs font-medium text-orange-600 dark:text-amber-400"
          >
            <AlertTriangle className="h-3.5 w-3.5" />
            Souhrn nemusí odpovídat poslední úpravě (přepočet selhal)
          </div>
        )}
        {onResetAll && (
          <button
            type="button"
            data-testid="pricing-reset-all"
            onClick={onResetAll}
            className="inline-flex items-center gap-1 text-xs font-medium text-gray-500 hover:text-indigo-600 dark:text-graphite-muted dark:hover:text-indigo-400"
          >
            <RotateCcw className="h-3.5 w-3.5" />
            Zrušit všechny úpravy
          </button>
        )}
      </div>
      <fieldset className="ml-auto flex items-center gap-4">
        <legend className="sr-only">Rozsah souhrnu</legend>
        <span className="text-xs font-medium text-gray-500 dark:text-graphite-muted">
          Souhrn:
        </span>
        {PRICING_SUMMARY_SCOPE_OPTIONS.map((option) => (
          <label
            key={option.value}
            className="flex items-center gap-1.5 text-xs text-gray-600 dark:text-graphite-muted whitespace-nowrap cursor-pointer"
          >
            <input
              type="radio"
              name="pricing-summary-scope"
              data-testid={`pricing-summary-scope-${option.value}`}
              value={option.value}
              checked={scope === option.value}
              onChange={() => onScopeChange(option.value)}
              className="h-3.5 w-3.5 border-gray-300 text-indigo-600 focus:ring-indigo-500 dark:border-graphite-border dark:bg-graphite-surface-2"
            />
            {option.label}
          </label>
        ))}
      </fieldset>
    </div>
    {hasError ? (
      <div
        data-testid="pricing-summary-error"
        className="flex items-center gap-2 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg p-4 mb-4 text-sm text-orange-600 dark:text-amber-400"
      >
        <AlertTriangle className="h-4 w-4" />
        Souhrn pro zvolený rozsah se nepodařilo načíst.
      </div>
    ) : isLoading ? (
      // The wider summary is a different population than the grid, so showing the
      // filter's numbers under an "all products" label would simply be wrong.
      <div
        data-testid="pricing-summary-loading"
        className="flex items-center gap-2 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg p-4 mb-4 text-sm text-gray-500 dark:text-graphite-muted"
      >
        <Loader2 className="h-4 w-4 animate-spin" />
        Načítám souhrn...
      </div>
    ) : (
      totals && (
        <PricingTotalsBar totals={totals} isRecalculating={isRecalculating} />
      )
    )}
  </div>
);

export default PricingSummaryBand;
