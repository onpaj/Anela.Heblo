import { PricingRowDto } from "../../api/generated/api-client";

// The work group is the handful of products the user is actually working on. It is
// deliberately a browser-local preference (no backend, not part of a saved scenario):
// it is a view over the grid, not part of the priced scenario itself.
export const PRICING_WORK_GROUP_STORAGE_KEY = "pricing.workGroup.productCodes";

// Membership is the union of two sources: every row that carries an edit is in the
// work group automatically (the user is evidently working on it), plus whatever the
// user pinned by hand. That is why an edited row's checkbox is not unpinnable --
// removing it would immediately be re-added by its own edit.
export const isInWorkGroup = (
  row: PricingRowDto,
  pinnedProductCodes: ReadonlySet<string>,
): boolean => (row.isEdited ?? false) || pinnedProductCodes.has(row.productCode ?? "");

export const filterWorkGroupRows = (
  rows: readonly PricingRowDto[],
  pinnedProductCodes: ReadonlySet<string>,
): PricingRowDto[] => rows.filter((row) => isInWorkGroup(row, pinnedProductCodes));

export const toggleWorkGroupProductCode = (
  pinnedProductCodes: readonly string[],
  productCode: string,
): string[] => {
  // A row with no product code cannot be pinned: the empty key would match every
  // other code-less row, so one checkbox would tick them all, and the override such
  // a pin leads to is one the server rejects outright. The two sibling helpers
  // (applyWorkGroupSelection, loadWorkGroupProductCodes) guard the same way.
  if (productCode.length === 0) {
    return [...pinnedProductCodes];
  }

  return pinnedProductCodes.includes(productCode)
    ? pinnedProductCodes.filter((code) => code !== productCode)
    : [...pinnedProductCodes, productCode];
};

// Bulk counterpart of toggleWorkGroupProductCode, used by the grid's column header:
// pinning keeps the existing order and appends what is new, unpinning simply drops
// the given codes. An edited row stays in the work group either way -- its membership
// comes from the edit, not from this list.
export const applyWorkGroupSelection = (
  pinnedProductCodes: readonly string[],
  productCodes: readonly string[],
  shouldPin: boolean,
): string[] => {
  if (!shouldPin) {
    return pinnedProductCodes.filter((code) => !productCodes.includes(code));
  }

  const added = productCodes.filter(
    (code) => code.length > 0 && !pinnedProductCodes.includes(code),
  );
  return [...pinnedProductCodes, ...Array.from(new Set(added))];
};

// localStorage is shared with the user, other tabs and older versions of this screen,
// so whatever comes back is treated as untrusted input: anything unreadable or of the
// wrong shape degrades to an empty work group rather than breaking the screen.
export const loadWorkGroupProductCodes = (): string[] => {
  try {
    const stored = window.localStorage.getItem(PRICING_WORK_GROUP_STORAGE_KEY);
    if (!stored) {
      return [];
    }

    const parsed: unknown = JSON.parse(stored);
    if (!Array.isArray(parsed)) {
      return [];
    }

    return parsed.filter(
      (code): code is string => typeof code === "string" && code.length > 0,
    );
  } catch {
    return [];
  }
};

export const saveWorkGroupProductCodes = (
  pinnedProductCodes: readonly string[],
): void => {
  try {
    window.localStorage.setItem(
      PRICING_WORK_GROUP_STORAGE_KEY,
      JSON.stringify(pinnedProductCodes),
    );
  } catch {
    // A full or blocked storage (private mode, quota) must not take the screen down:
    // the work group simply stops surviving a reload.
  }
};
