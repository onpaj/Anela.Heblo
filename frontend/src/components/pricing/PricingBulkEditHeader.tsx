import React, { useRef, useState } from "react";
import { Pencil } from "lucide-react";
import PricingBulkEditPopover from "./PricingBulkEditPopover";
import { PricingBulkEdit, PricingBulkEditField } from "./pricingBulkEdit";

export interface PricingBulkEditHeaderProps {
  field: PricingBulkEditField;
  label: string;
  disabled?: boolean;
  /** How many of the listed products the change can actually land on. */
  productCount: number;
  onApply: (edit: PricingBulkEdit) => void;
}

// The change lands on whatever the grid lists, so narrowing the grid IS how the user
// chooses what to change -- including filtering it down to the work group.
const BULK_EDIT_TITLE = "Hromadná úprava pro všechny zobrazené produkty";

/**
 * An editable column's header doubles as the entry point to the bulk editor: the
 * single-cell editor changes one product, this one every listed product at once.
 *
 * The trigger and the popover are one component so that the popover can tell a click
 * on its own trigger from a click outside it. While the two were siblings sharing
 * state one level up, the trigger's `mousedown` dismissed the popover a beat before
 * its own `click` reopened it, and the header could never close what it had opened.
 */
const PricingBulkEditHeader: React.FC<PricingBulkEditHeaderProps> = ({
  field,
  label,
  disabled = false,
  productCount,
  onApply,
}) => {
  const [isEditorOpen, setIsEditorOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);

  // Same contract as the single-cell editor: a keyboard user who dismisses the editor
  // is put back on the header they opened it from, because dropping them on the
  // document body loses their place in a grid of hundreds of rows. A click elsewhere
  // has already moved focus on purpose and must not have it stolen back.
  const closeEditor = (restoreFocus: boolean) => {
    setIsEditorOpen(false);
    if (restoreFocus) {
      triggerRef.current?.focus();
    }
  };

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        data-testid={`pricing-bulk-edit-${field}`}
        disabled={disabled}
        title={BULK_EDIT_TITLE}
        aria-haspopup="dialog"
        aria-expanded={isEditorOpen}
        onClick={() => setIsEditorOpen((current) => !current)}
        className="inline-flex items-center gap-1 uppercase tracking-wider hover:text-indigo-600 disabled:cursor-not-allowed disabled:hover:text-gray-500 dark:hover:text-indigo-400 dark:disabled:hover:text-graphite-muted"
      >
        {label}
        <Pencil className="h-3 w-3" aria-hidden="true" />
      </button>
      {isEditorOpen && (
        <PricingBulkEditPopover
          field={field}
          productCount={productCount}
          anchorRef={triggerRef}
          onApply={onApply}
          onClose={closeEditor}
        />
      )}
    </>
  );
};

export default PricingBulkEditHeader;
