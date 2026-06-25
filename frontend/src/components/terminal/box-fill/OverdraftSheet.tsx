import React from "react";
import { AlertCircle } from "lucide-react";
import type { ManufacturedProductInventoryItem } from "../../../api/hooks/useManufacturedProductInventory";
import { BottomSheet } from "../shell/BottomSheet";

interface OverdraftSheetProps {
  item: ManufacturedProductInventoryItem;
  requestedAmount: number;
  isSubmitting: boolean;
  onAddNegative: () => void;
  onAddRemaining: () => void;
  onCancel: () => void;
}

const OverdraftSheet: React.FC<OverdraftSheetProps> = ({
  item,
  requestedAmount,
  isSubmitting,
  onAddNegative,
  onAddRemaining,
  onCancel,
}) => {
  const missing = requestedAmount - item.amount;
  return (
    <BottomSheet open onClose={onCancel} ariaLabel={`Nedostatek zásob – ${item.productName}`}>
      <div
        className="space-y-4"
      >
        <div className="flex items-start gap-3">
          <AlertCircle className="h-6 w-6 text-amber-500 flex-shrink-0 mt-0.5" />
          <div>
            <p className="font-semibold text-neutral-slate dark:text-graphite-text">{item.productName}</p>
            <p className="text-sm text-neutral-gray dark:text-graphite-muted mt-1">
              Na skladě je pouze <strong>{item.amount}</strong>, požadováno <strong>{requestedAmount}</strong>.
            </p>
          </div>
        </div>
        <button
          type="button"
          onClick={onAddNegative}
          disabled={isSubmitting}
          data-testid="overdraft-add-negative"
          className="w-full py-4 text-base font-semibold text-white bg-amber-600 rounded-xl disabled:opacity-50"
        >
          Přidat záporný stav ({requestedAmount} ks, {missing} chybí)
        </button>
        <button
          type="button"
          onClick={onAddRemaining}
          disabled={isSubmitting}
          data-testid="overdraft-add-remaining"
          className="w-full py-4 text-base font-semibold text-neutral-slate dark:text-graphite-text bg-gray-100 dark:bg-graphite-surface-2 rounded-xl disabled:opacity-50"
        >
          Přidat pouze zbývající ({item.amount} ks)
        </button>
        <button type="button" onClick={onCancel} disabled={isSubmitting} className="w-full py-2 text-sm text-neutral-gray dark:text-graphite-muted disabled:opacity-50">
          Zrušit
        </button>
      </div>
    </BottomSheet>
  );
};

export default OverdraftSheet;
