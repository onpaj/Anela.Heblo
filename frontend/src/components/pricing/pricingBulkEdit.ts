import {
  IPricingOverrideDto,
  PricingEditField,
  PricingRowDto,
} from "../../api/generated/api-client";
import {
  absoluteFromRelative,
  pricingCellFacts,
  pricingValueKind,
  roundToDisplay,
} from "./pricingDelta";

/**
 * A bulk edit is always a PERCENTAGE of each product's own catalogue value: a flat
 * amount across products priced from 40 to 1 200 Kč means nothing, while "everything
 * 5 % up" is exactly the decision this screen is for. Negative cuts, and zero puts
 * the column back to the catalogue value for every listed product.
 */
export interface PricingBulkEdit {
  field: PricingBulkEditField;
  percent: number;
}

// Only the four independent variables can be bulk-edited. M0/M1 are derived from
// them, and the algebra that turns a margin into the cost it implies lives on the
// server (PricingSimulationCalculator.ApplyEdit) -- reproducing it here to build
// overrides directly is exactly the duplication that would let the two drift apart.
export type PricingBulkEditField =
  | PricingEditField.Price
  | PricingEditField.MaterialCost
  | PricingEditField.ManufacturingCost
  | PricingEditField.ForecastQuantity;

export interface PricingBulkEditResult {
  overrides: IPricingOverrideDto[];
  // Products whose override this edit actually set or cleared.
  appliedCount: number;
  // Products that were candidates for the change but could not take it: no usable
  // baseline to scale from, or a value the server would reject anyway (a price at or
  // below zero, a negative cost). Excluded rows are not candidates and are not
  // counted here -- they render read-only and are left out of every total, so the
  // user was never promised them.
  skippedCount: number;
}

const OVERRIDE_FIELD: Record<PricingBulkEditField, keyof IPricingOverrideDto> = {
  [PricingEditField.Price]: "price",
  [PricingEditField.MaterialCost]: "materialCost",
  [PricingEditField.ManufacturingCost]: "manufacturingCost",
  [PricingEditField.ForecastQuantity]: "forecastQuantity",
};

// The same rule the server validates an edit against: a price has to be positive,
// everything else merely non-negative. Checking it here keeps a bulk edit from
// posting a request that can only come back rejected.
const isValidValue = (field: PricingBulkEditField, value: number): boolean =>
  field === PricingEditField.Price ? value > 0 : value >= 0;

const targetValue = (
  row: PricingRowDto,
  edit: PricingBulkEdit,
): number | null => {
  const { baseline } = pricingCellFacts(row, edit.field);
  if (baseline === null) {
    return null;
  }

  // Anchored to the catalogue value, never to what the cell currently shows -- the
  // same rule as the single-cell editor, so applying "+5 %" twice lands on the same
  // number instead of compounding.
  const target = absoluteFromRelative(
    baseline,
    edit.percent,
    pricingValueKind(edit.field),
  );

  return target === null ? null : roundToDisplay(target);
};

// Zero percent is a reset, not a value: the column's override is dropped so the row
// rides the live catalogue value again, and an override left with nothing pinned is
// removed outright so the row stops counting as edited.
const clearField = (
  nextByCode: Map<string, IPricingOverrideDto>,
  productCode: string,
  field: PricingBulkEditField,
): boolean => {
  const current = nextByCode.get(productCode);
  const property = OVERRIDE_FIELD[field];
  if (current === undefined || current[property] === undefined) {
    return false;
  }

  const cleared: IPricingOverrideDto = { ...current, [property]: undefined };
  const hasPinnedValue = Object.values(OVERRIDE_FIELD).some(
    (key) => cleared[key] !== undefined,
  );

  if (hasPinnedValue) {
    nextByCode.set(productCode, cleared);
  } else {
    nextByCode.delete(productCode);
  }
  return true;
};

/**
 * Builds the override set one bulk edit implies over the given rows -- the ones the
 * grid currently lists -- leaving every other product and every other pinned field
 * exactly as they were. The result is handed to the same recalculation the
 * single-cell editor uses, so the server still owns every margin.
 */
export const applyPricingBulkEdit = (
  rows: readonly PricingRowDto[],
  overrides: readonly IPricingOverrideDto[],
  edit: PricingBulkEdit,
): PricingBulkEditResult => {
  const nextByCode = new Map<string, IPricingOverrideDto>(
    overrides.map((override) => [override.productCode ?? "", { ...override }]),
  );

  let appliedCount = 0;
  let skippedCount = 0;

  for (const row of rows) {
    const productCode = row.productCode ?? "";
    // Without a code there is nothing to key an override on: every code-less row
    // would collide on the same empty key, and the server rejects an override whose
    // product code is empty anyway.
    if (productCode.length === 0) {
      continue;
    }

    if (edit.percent === 0) {
      if (clearField(nextByCode, productCode, edit.field)) {
        appliedCount += 1;
      }
      continue;
    }

    // An excluded row is left out of every total and renders read-only, so a bulk
    // edit must not quietly mark it edited either. It is passed over silently rather
    // than reported as a refusal: nothing about it was ever offered to the user.
    if (row.isExcluded ?? false) {
      continue;
    }

    const target = targetValue(row, edit);
    if (target === null || !isValidValue(edit.field, target)) {
      skippedCount += 1;
      continue;
    }

    nextByCode.set(productCode, {
      ...nextByCode.get(productCode),
      productCode,
      [OVERRIDE_FIELD[edit.field]]: target,
    });
    appliedCount += 1;
  }

  return {
    overrides: Array.from(nextByCode.values()),
    appliedCount,
    skippedCount,
  };
};
