import React, { useEffect, useRef, useState } from "react";
import { PricingEditField } from "../../api/generated/api-client";
import {
  absoluteFromRelative,
  PricingCellFacts,
  relativeFromAbsolute,
  roundToDisplay,
} from "./pricingDelta";

export interface PricingValueEditorPopoverProps {
  productCode: string;
  field: PricingEditField;
  facts: PricingCellFacts;
  /** Called with the absolute value to commit. Never called for a no-op edit. */
  onApply: (value: number) => void;
  /**
   * Called after an apply and on every way of dismissing the editor.
   * `restoreFocus` is true only for a deliberate dismissal (Enter, Escape, the
   * buttons) — the cases where the user's focus would otherwise be dropped on the
   * document body. A dismissal caused by focus or a click going somewhere else must
   * not yank it back.
   */
  onClose: (restoreFocus: boolean) => void;
}

// Tolerates a Czech decimal comma ("45,5") and an explicit sign ("+5", "-3") in
// addition to a plain dot. Returns null for anything that is not a finite number,
// which callers treat as "nothing to compute, nothing to commit" -- the server owns
// the actual business validation (negative costs, zero price).
const parseInput = (raw: string): number | null => {
  const normalized = raw.trim().replace(",", ".");
  if (normalized === "" || normalized === "+" || normalized === "-") return null;
  const parsed = Number(normalized);
  return Number.isFinite(parsed) ? parsed : null;
};

// Number() drops the trailing zeros toFixed adds, so 500 stays "500", not "500.00".
const formatInput = (value: number | null): string =>
  value === null ? "" : String(roundToDisplay(value));

const FIELD_LABELS: Record<PricingEditField, string> = {
  [PricingEditField.Price]: "Cena",
  [PricingEditField.MaterialCost]: "Materiál",
  [PricingEditField.ManufacturingCost]: "Výroba",
  [PricingEditField.M0Amount]: "M0 Kč",
  [PricingEditField.M0Percentage]: "M0 %",
  [PricingEditField.M1Amount]: "M1 Kč",
  [PricingEditField.M1Percentage]: "M1 %",
  [PricingEditField.ForecastQuantity]: "Prognóza ks",
};

const ZERO_BASELINE_HINT = "Proti nule nelze počítat procentní změnu";

/**
 * The two-field editor behind one pricing cell: an absolute **Hodnota** and a
 * **Změna** relative to the real (catalogue) state, kept in sync in both directions.
 *
 * The relative side is always anchored to the BASELINE, never to the value the editor
 * currently shows, so retyping "+5 %" lands on the same number instead of compounding.
 * Being transient is the point: it holds its own draft only while open and discards it
 * on close, so a recalculation landing elsewhere in the grid can never clobber it and
 * it needs none of the resync machinery a permanently mounted input would.
 */
const PricingValueEditorPopover: React.FC<PricingValueEditorPopoverProps> = ({
  productCode,
  field,
  facts,
  onApply,
  onClose,
}) => {
  const { kind, baseline, effective } = facts;
  const [valueDraft, setValueDraft] = useState(() => formatInput(effective));
  const [relativeDraft, setRelativeDraft] = useState(() =>
    formatInput(facts.relativeChange),
  );
  const containerRef = useRef<HTMLDivElement>(null);
  const valueInputRef = useRef<HTMLInputElement>(null);

  // A percentage of zero is zero whatever the multiplier, so the relative field would
  // be a control that silently does nothing. Percentage-point cells survive a zero
  // baseline -- adding points to a 0 % margin is perfectly meaningful -- but not an
  // unknown one: with no real state to anchor to, every keystroke would blank the
  // value it is supposed to drive.
  const isRelativeDisabled = baseline === null || (kind !== "percentage" && baseline === 0);

  useEffect(() => {
    valueInputRef.current?.focus();
    valueInputRef.current?.select();
    // The grid body is a scroll container, so an editor opened on a row near its
    // bottom edge would otherwise be clipped out of sight. Ask the container to bring
    // the whole editor into view rather than trying to guess a flip-up threshold.
    // Optional call: this is pure enhancement and the method is absent in jsdom.
    containerRef.current?.scrollIntoView?.({ block: "nearest", inline: "nearest" });
  }, []);

  // Dismiss whenever the interaction moves outside, by pointer or by keyboard.
  // Discarding rather than committing is the safe default: a stray click elsewhere in
  // the grid must not post an edit. The focus half also keeps a Tab out of the editor
  // from leaving a second one open behind the user, each holding its own stale draft.
  useEffect(() => {
    const dismissIfOutside = (event: Event) => {
      if (!containerRef.current?.contains(event.target as Node)) {
        onClose(false);
      }
    };
    document.addEventListener("mousedown", dismissIfOutside);
    document.addEventListener("focusin", dismissIfOutside);
    return () => {
      document.removeEventListener("mousedown", dismissIfOutside);
      document.removeEventListener("focusin", dismissIfOutside);
    };
  }, [onClose]);

  const handleValueChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const raw = event.target.value;
    setValueDraft(raw);
    setRelativeDraft(formatInput(relativeFromAbsolute(baseline, parseInput(raw), kind)));
  };

  const handleRelativeChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const raw = event.target.value;
    setRelativeDraft(raw);
    setValueDraft(formatInput(absoluteFromRelative(baseline, parseInput(raw), kind)));
  };

  const apply = () => {
    const parsed = parseInput(valueDraft);
    // An unparseable draft keeps the editor open so the user can fix it; there is
    // nothing to send and nothing to say beyond what the empty field already shows.
    if (parsed === null) {
      return;
    }
    // Committing a value identical to the one already displayed would flag the row as
    // edited and pull it into the ceník export from a gesture that changed nothing.
    if (effective !== null && parsed === roundToDisplay(effective)) {
      onClose(true);
      return;
    }
    onApply(parsed);
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

  const inputClassName =
    "w-28 rounded border border-gray-300 bg-white px-2 py-1 text-right text-sm text-gray-900 focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500 disabled:cursor-not-allowed disabled:bg-gray-100 disabled:text-gray-400 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:disabled:bg-white/5";

  return (
    <div
      ref={containerRef}
      role="dialog"
      aria-label={`Úprava ${FIELD_LABELS[field]} — ${productCode}`}
      data-testid={`pricing-editor-${productCode}-${field}`}
      onKeyDown={handleKeyDown}
      className="absolute right-0 top-full z-30 mt-1 w-56 rounded-lg border border-gray-200 bg-white p-3 text-left shadow-lg dark:border-graphite-border dark:bg-graphite-surface"
    >
      <div className="mb-2 text-xs font-medium uppercase tracking-wider text-gray-500 dark:text-graphite-muted">
        {FIELD_LABELS[field]}
      </div>

      <label className="mb-2 flex items-center justify-between gap-2">
        <span className="text-xs text-gray-600 dark:text-graphite-muted">Hodnota</span>
        <input
          ref={valueInputRef}
          type="text"
          inputMode="decimal"
          data-testid={`pricing-editor-value-${productCode}-${field}`}
          value={valueDraft}
          onChange={handleValueChange}
          className={inputClassName}
        />
      </label>

      <label className="mb-2 flex items-center justify-between gap-2">
        <span className="text-xs text-gray-600 dark:text-graphite-muted">
          {kind === "percentage" ? "Změna (p.b.)" : "Změna %"}
        </span>
        <input
          type="text"
          inputMode="decimal"
          disabled={isRelativeDisabled}
          title={isRelativeDisabled ? ZERO_BASELINE_HINT : undefined}
          data-testid={`pricing-editor-relative-${productCode}-${field}`}
          value={relativeDraft}
          onChange={handleRelativeChange}
          className={inputClassName}
        />
      </label>

      <div
        data-testid={`pricing-editor-baseline-${productCode}-${field}`}
        className="mb-3 text-xs text-gray-400 dark:text-graphite-faint"
      >
        Původně: {formatInput(baseline) || "—"}
      </div>

      <div className="flex justify-end gap-2">
        <button
          type="button"
          data-testid={`pricing-editor-cancel-${productCode}-${field}`}
          onClick={() => onClose(true)}
          className="rounded px-2 py-1 text-xs text-gray-600 hover:bg-gray-100 dark:text-graphite-muted dark:hover:bg-white/10"
        >
          Zrušit
        </button>
        <button
          type="button"
          data-testid={`pricing-editor-apply-${productCode}-${field}`}
          onClick={apply}
          className="rounded bg-indigo-600 px-2 py-1 text-xs font-medium text-white hover:bg-indigo-700"
        >
          Použít
        </button>
      </div>
    </div>
  );
};

export default PricingValueEditorPopover;
