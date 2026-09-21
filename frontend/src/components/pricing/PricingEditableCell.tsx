import React, { useEffect, useRef, useState } from "react";
import { PricingEditField } from "../../api/generated/api-client";

export interface PricingEditableCellProps {
  value: number | null | undefined;
  field: PricingEditField;
  productCode: string;
  onCommit: (productCode: string, field: PricingEditField, value: number) => void;
  error?: string;
}

// Tolerates a Czech decimal comma ("45,5") in addition to a plain dot. Returns
// null for anything that isn't a finite number, which callers treat as "discard,
// don't send" -- the server owns actual business validation (negative costs,
// zero price, etc.), this is only about not shipping NaN over the wire.
const parseDraftValue = (raw: string): number | null => {
  const normalized = raw.trim().replace(",", ".");
  if (normalized === "") return null;
  const parsed = Number(normalized);
  return Number.isFinite(parsed) ? parsed : null;
};

const formatDraftValue = (value: number | null | undefined): string =>
  value === null || value === undefined ? "" : String(value);

// Editable numeric cell for the pricing grid. Holds its own draft string so keystrokes
// never touch parent state or fire the mutation -- only a blur (or Enter) commits.
// Escape reverts locally without ever calling onCommit.
//
// Resyncing after a server round-trip is done via a `useEffect` on [value, error]
// rather than a parent-forced remount: whenever this cell's OWN `value` prop changes
// (a successful recalculate touched this field, directly or as a side effect of a
// sibling field in the same row, e.g. editing the M0 Kč amount recomputes M0 %) or
// its `error` prop changes (a rejected edit), the draft re-syncs -- but ONLY while
// the input is not focused, so a settle elsewhere in the grid can never clobber
// whatever the user is actively typing into a different cell.
const PricingEditableCell: React.FC<PricingEditableCellProps> = ({
  value,
  field,
  productCode,
  onCommit,
  error,
}) => {
  const [draft, setDraft] = useState(() => formatDraftValue(value));
  // Guards against a double-fire when Enter commits and then also blurs the
  // field: the second call sees the same parsed value already in flight and
  // skips instead of posting the identical edit twice. Also tells a later
  // non-dirty blur (see handleBlur) not to stomp the optimistic draft with a
  // stale `value` before the round trip has actually landed.
  const sentRef = useRef<number | null>(null);
  const isFocusedRef = useRef(false);
  // True only from a real keystroke (set in the input's onChange), never
  // inferred from a value comparison. This is what stops commitIfChanged from
  // treating "draft happens to differ from the current value" as a user edit
  // when it's really just a stale draft the resync effect skipped while this
  // cell was focused (e.g. editing the M0 Kč amount recomputes M0 % on the
  // same row while the user had merely clicked into -- not typed into -- the
  // M0 % cell). Without this guard, blurring an untouched cell after a
  // sibling's commit would silently re-post the cell's OLD value as if the
  // user had just typed it, overwriting the server's fresh one.
  const isDirtyRef = useRef(false);

  useEffect(() => {
    if (isFocusedRef.current) {
      return;
    }
    setDraft(formatDraftValue(value));
    sentRef.current = null;
    isDirtyRef.current = false;
  }, [value, error]);

  const commitIfChanged = () => {
    if (!isDirtyRef.current) {
      return;
    }

    const parsed = parseDraftValue(draft);
    isDirtyRef.current = false;
    if (parsed === null) {
      setDraft(formatDraftValue(value));
      return;
    }

    const baseline = value ?? null;
    if (parsed === baseline || parsed === sentRef.current) {
      return;
    }

    sentRef.current = parsed;
    onCommit(productCode, field, parsed);
  };

  const handleChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    isDirtyRef.current = true;
    setDraft(event.target.value);
  };

  const handleFocus = () => {
    isFocusedRef.current = true;
  };

  const handleBlur = () => {
    isFocusedRef.current = false;
    if (!isDirtyRef.current) {
      // Nothing was typed, so there is nothing to commit -- but the resync
      // effect above skipped this cell while it had focus, so its draft may
      // still be stale (a sibling commit could have changed `value` in the
      // meantime). Catch up now, unless a commit from THIS cell is still
      // awaiting its round trip (sentRef), in which case keep showing what
      // was just sent rather than snapping back to the pre-commit value.
      if (sentRef.current === null) {
        setDraft(formatDraftValue(value));
      }
      return;
    }
    commitIfChanged();
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Enter") {
      commitIfChanged();
      event.currentTarget.blur();
    } else if (event.key === "Escape") {
      isDirtyRef.current = false;
      setDraft(formatDraftValue(value));
    }
  };

  return (
    <div className="relative inline-block w-full">
      <input
        type="text"
        inputMode="decimal"
        aria-label={`${field}-${productCode}`}
        aria-invalid={!!error}
        data-testid={`pricing-cell-${productCode}-${field}`}
        value={draft}
        onChange={handleChange}
        onFocus={handleFocus}
        onBlur={handleBlur}
        onKeyDown={handleKeyDown}
        className={`w-full rounded border bg-transparent px-2 py-1 text-right text-sm focus:outline-none focus:ring-2 dark:text-graphite-text ${
          error
            ? "border-red-500 ring-1 ring-red-500 focus:ring-red-500"
            : "border-transparent focus:ring-indigo-500"
        }`}
      />
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
