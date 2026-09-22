import React, { useEffect, useRef, useState } from "react";
import { PricingEditField } from "../../api/generated/api-client";
import { PricingBulkEdit, PricingBulkEditField } from "./pricingBulkEdit";

export interface PricingBulkEditPopoverProps {
  field: PricingBulkEditField;
  /** How many products the change will land on — everything the grid lists. */
  productCount: number;
  /**
   * The control this popover hangs off. It is a sibling rather than a descendant, so
   * outside-dismissal has to be told about it explicitly — otherwise the trigger's
   * own mousedown reads as a click outside.
   */
  anchorRef?: React.RefObject<HTMLElement>;
  onApply: (edit: PricingBulkEdit) => void;
  /** Same contract as the single-cell editor: true only on a deliberate dismissal. */
  onClose: (restoreFocus: boolean) => void;
}

const FIELD_LABELS: Record<PricingBulkEditField, string> = {
  [PricingEditField.Price]: "Cena",
  [PricingEditField.MaterialCost]: "Materiál",
  [PricingEditField.ManufacturingCost]: "Výroba",
  [PricingEditField.ForecastQuantity]: "Prognóza ks",
};

// Czech needs three forms, and the boundary between the "2-4" and the "5 and up"
// form is a grammatical rule, not a tunable.
const PLURAL_MANY_THRESHOLD = 5;

const INVALID_PERCENT_MESSAGE = "Zadejte změnu v procentech, například 5 nebo -10.";

const productCountLabel = (count: number): string => {
  if (count === 1) return "1 produkt";
  if (count > 1 && count < PLURAL_MANY_THRESHOLD) return `${count} produkty`;
  return `${count} produktů`;
};

/**
 * The bulk counterpart of PricingValueEditorPopover: one percentage applied to every
 * product the grid lists at once. Percent is the only unit on offer — a flat amount
 * across products priced from 40 to 1 200 Kč would mean nothing — and it may be
 * negative for a cut or zero to put the column back to its catalogue values.
 */
const PricingBulkEditPopover: React.FC<PricingBulkEditPopoverProps> = ({
  field,
  productCount,
  anchorRef,
  onApply,
  onClose,
}) => {
  const [percentDraft, setPercentDraft] = useState("");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const amountInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    amountInputRef.current?.focus();
  }, []);

  // Dismiss whenever the interaction moves outside, by pointer or by keyboard --
  // discarding rather than committing, exactly like the single-cell editor.
  useEffect(() => {
    const dismissIfOutside = (event: Event) => {
      const target = event.target as Node;
      if (containerRef.current?.contains(target)) {
        return;
      }
      // The trigger closes the popover through its own click handler. Treating its
      // mousedown as an outside click closed the popover a beat before that click
      // reopened it, leaving the header unable to close what it had opened.
      if (anchorRef?.current?.contains(target)) {
        return;
      }
      onClose(false);
    };
    document.addEventListener("mousedown", dismissIfOutside);
    document.addEventListener("focusin", dismissIfOutside);
    return () => {
      document.removeEventListener("mousedown", dismissIfOutside);
      document.removeEventListener("focusin", dismissIfOutside);
    };
  }, [anchorRef, onClose]);

  const apply = () => {
    const percent = Number(percentDraft.trim().replace(",", "."));
    // An empty or unparseable draft keeps the editor open so the user can fix it --
    // and says so, because a button that silently does nothing reads as broken.
    // Zero is a perfectly good input: it puts the column back to its original values.
    if (percentDraft.trim() === "" || !Number.isFinite(percent)) {
      setErrorMessage(INVALID_PERCENT_MESSAGE);
      amountInputRef.current?.focus();
      return;
    }
    onApply({ field, percent });
    onClose(true);
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Enter") {
      event.preventDefault();
      apply();
    } else if (event.key === "Escape") {
      event.preventDefault();
      onClose(true);
    }
  };

  return (
    <div
      ref={containerRef}
      role="dialog"
      aria-label={`Hromadná úprava ${FIELD_LABELS[field]}`}
      data-testid="pricing-bulk-editor"
      onKeyDown={handleKeyDown}
      className="absolute right-0 top-full z-30 mt-1 w-60 rounded-lg border border-gray-200 bg-white p-3 text-left normal-case tracking-normal shadow-lg dark:border-graphite-border dark:bg-graphite-surface"
    >
      <div className="mb-1 text-xs font-medium uppercase tracking-wider text-gray-500 dark:text-graphite-muted">
        {FIELD_LABELS[field]}
      </div>
      <div className="mb-3 text-xs text-gray-400 dark:text-graphite-faint">
        Změna pro {productCountLabel(productCount)} v tabulce
      </div>

      <label className="mb-2 flex items-center justify-between gap-2">
        <span className="text-xs text-gray-600 dark:text-graphite-muted">Změna %</span>
        <div className="flex items-center gap-1">
          <input
            ref={amountInputRef}
            type="number"
            step="1"
            data-testid="pricing-bulk-editor-amount"
            aria-label="Změna v procentech"
            value={percentDraft}
            aria-invalid={errorMessage !== null}
            onChange={(e) => {
              setPercentDraft(e.target.value);
              setErrorMessage(null);
            }}
            className="w-24 rounded border border-gray-300 bg-white px-2 py-1 text-right text-sm text-gray-900 focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text"
          />
          <span className="text-xs text-gray-500 dark:text-graphite-muted">%</span>
        </div>
      </label>

      {errorMessage && (
        <div
          role="alert"
          data-testid="pricing-bulk-editor-error"
          className="mb-2 text-xs text-red-600 dark:text-red-400"
        >
          {errorMessage}
        </div>
      )}

      <div className="mb-3 text-xs text-gray-400 dark:text-graphite-faint">
        Počítá se z původní hodnoty produktu. 0 % vrátí sloupec na původní hodnoty.
      </div>

      <div className="flex justify-end gap-2">
        <button
          type="button"
          data-testid="pricing-bulk-editor-cancel"
          onClick={() => onClose(true)}
          className="rounded px-2 py-1 text-xs text-gray-600 hover:bg-gray-100 dark:text-graphite-muted dark:hover:bg-white/10"
        >
          Zrušit
        </button>
        <button
          type="button"
          data-testid="pricing-bulk-editor-apply"
          onClick={apply}
          className="rounded bg-indigo-600 px-2 py-1 text-xs font-medium text-white hover:bg-indigo-700"
        >
          Použít
        </button>
      </div>
    </div>
  );
};

export default PricingBulkEditPopover;
