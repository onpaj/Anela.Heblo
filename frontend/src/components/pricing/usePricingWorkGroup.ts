import { useMemo, useRef, useState } from "react";
import {
  applyWorkGroupSelection,
  loadWorkGroupProductCodes,
  saveWorkGroupProductCodes,
  toggleWorkGroupProductCode,
} from "./pricingWorkGroup";

export interface PricingWorkGroup {
  // Only the hand-picked codes: an edited row belongs to the work group through its
  // edit, not through this set (see isInWorkGroup).
  pinnedProductCodes: ReadonlySet<string>;
  toggleProductCode: (productCode: string) => void;
  // Pins or unpins a whole set at once (the grid's column header).
  setProductCodes: (productCodes: readonly string[], shouldPin: boolean) => void;
}

// The pinned part of the work group, kept in localStorage so it survives a reload.
// Read once on mount (lazy initial state) and written on every toggle -- there is no
// server copy to reconcile with.
export const usePricingWorkGroup = (): PricingWorkGroup => {
  const [pinnedProductCodes, setPinnedProductCodes] = useState<string[]>(
    loadWorkGroupProductCodes,
  );
  // Every change is resolved against this ref rather than against the value this
  // render closed over, so two changes landing before the next render still compose.
  // Reading the render value would make the second overwrite the first -- and because
  // the loser is also what gets written to localStorage, the lost change would
  // survive a reload.
  const latestProductCodesRef = useRef(pinnedProductCodes);

  const persist = (computeNext: (current: readonly string[]) => string[]) => {
    const next = computeNext(latestProductCodesRef.current);
    latestProductCodesRef.current = next;
    saveWorkGroupProductCodes(next);
    setPinnedProductCodes(next);
  };

  const toggleProductCode = (productCode: string) => {
    persist((current) => toggleWorkGroupProductCode(current, productCode));
  };

  const setProductCodes = (
    productCodes: readonly string[],
    shouldPin: boolean,
  ) => {
    persist((current) =>
      applyWorkGroupSelection(current, productCodes, shouldPin),
    );
  };

  const pinnedSet = useMemo(
    () => new Set(pinnedProductCodes),
    [pinnedProductCodes],
  );

  return { pinnedProductCodes: pinnedSet, toggleProductCode, setProductCodes };
};
