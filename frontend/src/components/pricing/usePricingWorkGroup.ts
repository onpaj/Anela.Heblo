import { useMemo, useState } from "react";
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

  const persist = (next: string[]) => {
    setPinnedProductCodes(next);
    saveWorkGroupProductCodes(next);
  };

  const toggleProductCode = (productCode: string) => {
    persist(toggleWorkGroupProductCode(pinnedProductCodes, productCode));
  };

  const setProductCodes = (
    productCodes: readonly string[],
    shouldPin: boolean,
  ) => {
    persist(applyWorkGroupSelection(pinnedProductCodes, productCodes, shouldPin));
  };

  const pinnedSet = useMemo(
    () => new Set(pinnedProductCodes),
    [pinnedProductCodes],
  );

  return { pinnedProductCodes: pinnedSet, toggleProductCode, setProductCodes };
};
