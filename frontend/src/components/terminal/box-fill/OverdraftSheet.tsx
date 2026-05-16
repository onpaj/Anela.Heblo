import React from "react";
import { AlertCircle } from "lucide-react";
import type { ManufacturedProductInventoryItem } from "../../../api/hooks/useManufacturedProductInventory";

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
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/40" onClick={onCancel}>
      <div
        role="dialog"
        aria-modal="true"
        aria-label={`Nedostatek zásob – ${item.productName}`}
        className="bg-white rounded-t-2xl w-full max-w-md p-5 space-y-4"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start gap-3">
          <AlertCircle className="h-6 w-6 text-amber-500 flex-shrink-0 mt-0.5" />
          <div>
            <p className="font-semibold text-neutral-slate">{item.productName}</p>
            <p className="text-sm text-neutral-gray mt-1">
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
          className="w-full py-4 text-base font-semibold text-neutral-slate bg-gray-100 rounded-xl disabled:opacity-50"
        >
          Přidat pouze zbývající ({item.amount} ks)
        </button>
        <button type="button" onClick={onCancel} disabled={isSubmitting} className="w-full py-2 text-sm text-neutral-gray disabled:opacity-50">
          Zrušit
        </button>
      </div>
    </div>
  );
};

export default OverdraftSheet;
