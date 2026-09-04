import React from "react";
import { AlertTriangle } from "lucide-react";
import { useResolvePriceConflict } from "../../api/hooks/useProductPricing";
// Enums are imported from the generated client directly (not from the hooks
// module) so this component keeps working when tests mock ../../api/hooks/useProductPricing.
import { PriceConflictResolution, PriceSyncTarget } from "../../api/generated/api-client";
import { formatCurrency } from "../../utils/formatters";

interface PriceConflictBannerProps {
  productCode: string;
  target: PriceSyncTarget;
  hebloPrice: number;
  remotePrice: number | null;
  /** True while this row's price is being saved — resolving now would race that save. */
  disabled?: boolean;
  canWrite: boolean;
}

const TARGET_LABELS: Record<PriceSyncTarget, string> = {
  [PriceSyncTarget.Shoptet]: "Shoptet",
  [PriceSyncTarget.Flexi]: "Flexi",
};

const PriceConflictBanner: React.FC<PriceConflictBannerProps> = ({
  productCode,
  target,
  hebloPrice,
  remotePrice,
  disabled = false,
  canWrite,
}) => {
  const resolveConflict = useResolvePriceConflict();
  const isBusy = disabled || resolveConflict.isPending;

  const handleKeepHeblo = () =>
    resolveConflict.mutate({
      productCode,
      target,
      resolution: PriceConflictResolution.KeepHebloPrice,
    });

  const handleAcceptRemote = () =>
    resolveConflict.mutate({
      productCode,
      target,
      resolution: PriceConflictResolution.AcceptRemotePrice,
    });

  return (
    <div
      data-testid={`price-conflict-${productCode}-${target}`}
      className="flex flex-wrap items-center gap-3 px-4 py-3 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-900/40 rounded-md"
    >
      <AlertTriangle className="h-4 w-4 text-amber-600 dark:text-amber-400 flex-shrink-0" />
      <div className="text-sm text-amber-800 dark:text-amber-300">
        Konflikt ceny pro <strong>{TARGET_LABELS[target]}</strong>: Heblo {formatCurrency(hebloPrice)},
        {" "}
        {TARGET_LABELS[target]} {remotePrice !== null ? formatCurrency(remotePrice) : "neznámá cena"}
      </div>
      {canWrite && (
        <div className="flex gap-2 ml-auto">
          <button
            type="button"
            onClick={handleKeepHeblo}
            disabled={isBusy}
            className="px-3 py-1.5 text-xs font-medium text-amber-800 dark:text-amber-200 bg-white dark:bg-graphite-surface border border-amber-300 dark:border-amber-900/40 rounded-md hover:bg-amber-100 dark:hover:bg-amber-900/30 transition-colors disabled:opacity-50"
          >
            Ponechat cenu z Hebla
          </button>
          <button
            type="button"
            onClick={handleAcceptRemote}
            disabled={isBusy}
            className="px-3 py-1.5 text-xs font-medium text-white bg-amber-600 hover:bg-amber-700 rounded-md transition-colors disabled:opacity-50"
          >
            Převzít externí cenu
          </button>
        </div>
      )}
      {resolveConflict.isError && (
        <div
          data-testid={`price-conflict-error-${productCode}-${target}`}
          role="alert"
          className="w-full text-xs text-red-700 dark:text-red-400"
        >
          {resolveConflict.error?.message ?? "Konflikt se nepodařilo vyřešit."}
        </div>
      )}
    </div>
  );
};

export default PriceConflictBanner;
