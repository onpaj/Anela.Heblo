import React, { useState } from "react";
import { PricingEditField, PricingRowDto } from "../../api/generated/api-client";
import {
  formatPricingValue,
  hasPricingChange,
  isIncreaseFavourable,
  PricingCellFacts,
  pricingCellFacts,
  roundToDisplay,
} from "./pricingDelta";
import PricingValueEditorPopover from "./PricingValueEditorPopover";

export interface PricingEditableCellProps {
  row: PricingRowDto;
  field: PricingEditField;
  onCommit: (productCode: string, field: PricingEditField, value: number) => void;
  /** Inline message for an edit the server rejected. */
  error?: string;
  /** Renders the value without an editor — scenario view, or a row with no usable baseline. */
  readOnly?: boolean;
}

// The "change against the real state" the grid reports, as a hover title. The unit
// follows the cell's kind: a ratio cell moves in percentage POINTS, everything else
// by a percentage of the real value (see pricingDelta).
const changeTitle = (facts: PricingCellFacts): string => {
  const original = `Původně: ${formatPricingValue(facts.kind, facts.baseline)}`;
  if (facts.relativeChange === null) {
    // No relative change is expressible (a real state of zero), so report the plain
    // difference rather than saying nothing about a cell that clearly moved.
    return `${original} · ${withSign(roundToDisplay(facts.delta ?? 0))}`;
  }
  const unit = facts.kind === "percentage" ? " p.b." : " %";
  return `${original} · ${withSign(roundToDisplay(facts.relativeChange))}${unit}`;
};

const withSign = (value: number): string =>
  `${value > 0 ? "+" : ""}${value.toLocaleString("cs-CZ")}`;

/**
 * One pricing cell: its value, the change against the real state as a hover title,
 * and — unless read-only — a click target that opens the two-field value/percentage
 * editor. The editor is transient by design (see PricingValueEditorPopover), so this
 * cell holds no draft of its own and a recalculation elsewhere in the grid has
 * nothing here to clobber.
 */
const PricingEditableCell: React.FC<PricingEditableCellProps> = ({
  row,
  field,
  onCommit,
  error,
  readOnly = false,
}) => {
  const [isEditorOpen, setIsEditorOpen] = useState(false);
  const facts = pricingCellFacts(row, field);
  const productCode = row.productCode ?? "";
  const isChanged = hasPricingChange(facts);

  // Up is good on a price or a margin and bad on a cost, so the cue follows the
  // field rather than the sign: colouring a cost increase green would misread the row.
  const isIncrease = (facts.delta ?? 0) > 0;
  const favourable = isIncreaseFavourable(field) === isIncrease;
  const changeClassName = !isChanged
    ? ""
    : favourable
      ? " text-emerald-600 dark:text-emerald-400"
      : " text-rose-600 dark:text-rose-400";

  const handleApply = (value: number) => onCommit(productCode, field, value);

  const handleKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      setIsEditorOpen(true);
    }
  };

  const interactiveProps = readOnly
    ? {}
    : {
        role: "button" as const,
        tabIndex: 0,
        onClick: () => setIsEditorOpen(true),
        onKeyDown: handleKeyDown,
      };

  return (
    <div className="relative inline-block w-full">
      <div
        {...interactiveProps}
        aria-label={`${field}-${productCode}`}
        aria-invalid={error ? true : undefined}
        title={isChanged ? changeTitle(facts) : undefined}
        data-testid={`pricing-cell-${productCode}-${field}`}
        className={`w-full rounded px-2 py-1 text-right text-sm dark:text-graphite-text${
          readOnly
            ? ""
            : " cursor-pointer hover:bg-indigo-50 focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:hover:bg-indigo-500/20"
        }${error ? " ring-1 ring-red-500" : ""}${changeClassName}`}
      >
        {formatPricingValue(facts.kind, facts.effective)}
      </div>

      {isEditorOpen && !readOnly && (
        <PricingValueEditorPopover
          productCode={productCode}
          field={field}
          facts={facts}
          onApply={handleApply}
          onClose={() => setIsEditorOpen(false)}
        />
      )}

      {error && (
        <div
          role="alert"
          data-testid={`pricing-cell-error-${productCode}-${field}`}
          className="absolute left-0 top-full z-20 mt-1 whitespace-nowrap rounded bg-red-600 px-2 py-1 text-xs text-white shadow-lg"
        >
          {error}
        </div>
      )}
    </div>
  );
};

export default PricingEditableCell;
