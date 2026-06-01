import React, { useState } from "react";
import { AlertCircle } from "lucide-react";
import type { ManufacturedProductInventoryItem } from "../../../api/hooks/useManufacturedProductInventory";
import { BottomSheet } from "../shell/BottomSheet";

interface AmountEntrySheetProps {
  item: ManufacturedProductInventoryItem;
  initialAmount?: number;
  isSubmitting: boolean;
  onConfirm: (amount: number) => void;
  onCancel: () => void;
}

const AmountEntrySheet: React.FC<AmountEntrySheetProps> = ({
  item,
  initialAmount,
  isSubmitting,
  onConfirm,
  onCancel,
}) => {
  const [value, setValue] = useState(initialAmount !== undefined ? String(initialAmount) : "");
  const [error, setError] = useState<string | null>(null);

  const submit = () => {
    const parsed = parseFloat(value);
    if (!value || isNaN(parsed) || parsed <= 0) {
      setError("Zadejte kladné číslo");
      return;
    }
    onConfirm(parsed);
  };

  return (
    <BottomSheet open onClose={onCancel} hasInput>
      <div
        aria-modal="true"
        aria-label={`Zadat množství – ${item.productName}`}
        className="space-y-4"
      >
        <div>
          <p className="font-semibold text-neutral-slate">{item.productName}</p>
          <p className="text-xs text-neutral-gray font-mono">
            {item.productCode}
            {item.lotNumber ? ` • Šarže: ${item.lotNumber}` : ""} • Sklad: {item.amount}
          </p>
        </div>
        <input
          type="number"
          inputMode="decimal"
          autoFocus
          value={value}
          onChange={(e) => {
            setValue(e.target.value);
            setError(null);
          }}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              submit();
            }
          }}
          step="0.01"
          min="0.01"
          placeholder="Množství"
          data-testid="amount-entry-input"
          className="w-full px-4 py-3 text-lg border border-border-light rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-blue"
        />
        {error && (
          <div role="alert" className="flex items-center gap-1 text-xs text-red-600">
            <AlertCircle className="h-3 w-3" /> {error}
          </div>
        )}
        <div className="flex gap-3">
          <button
            type="button"
            onClick={onCancel}
            disabled={isSubmitting}
            className="flex-1 py-3 text-base font-medium text-neutral-slate bg-gray-100 rounded-xl disabled:opacity-50"
          >
            Zrušit
          </button>
          <button
            type="button"
            onClick={submit}
            disabled={isSubmitting}
            data-testid="amount-entry-confirm"
            className="flex-1 py-3 text-base font-semibold text-white bg-primary-blue rounded-xl disabled:opacity-50"
          >
            Přidat
          </button>
        </div>
      </div>
    </BottomSheet>
  );
};

export default AmountEntrySheet;
