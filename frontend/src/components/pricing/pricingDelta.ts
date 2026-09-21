import { PricingEditField, PricingRowDto } from "../../api/generated/api-client";
import { formatCurrency, formatNumber, formatPercentage } from "../../utils/formatters";

/**
 * How a pricing cell's value behaves, which is what decides the unit of a RELATIVE
 * edit and of the "change against the real state" the grid reports:
 *
 * - currency / quantity: a relative change is a percentage OF the real value.
 * - percentage: the value already IS a ratio, so a relative change is expressed in
 *   percentage points. "5 % more of a 58,33 % margin" is ambiguous; "5 points more"
 *   is exactly what a pricing decision means.
 */
export type PricingValueKind = "currency" | "percentage" | "quantity";

export const pricingValueKind = (field: PricingEditField): PricingValueKind => {
  switch (field) {
    case PricingEditField.M0Percentage:
    case PricingEditField.M1Percentage:
      return "percentage";
    case PricingEditField.ForecastQuantity:
      return "quantity";
    default:
      return "currency";
  }
};

export interface PricingCellFacts {
  kind: PricingValueKind;
  /** The untouched catalogue value for this field — the "real state". */
  baseline: number | null;
  /** The value after any override, i.e. what the cell currently shows. */
  effective: number | null;
  /** effective - baseline, null when either side is unknown. */
  delta: number | null;
  /**
   * The change against the real state in the unit this cell's relative editor uses
   * (see PricingValueKind). Null when it cannot be expressed — a percentage of a
   * real state of zero has no meaning.
   */
  relativeChange: number | null;
}

// Everything this screen shows is rounded to two decimals, so a difference smaller
// than half of the last displayed digit is not a change anyone can see. Reporting it
// anyway would print "+0,00 %" next to values that read as identical.
const DISPLAY_DECIMALS = 2;
const VISIBLE_DELTA_THRESHOLD = 0.005;

const finite = (value: number | null | undefined): number | null =>
  typeof value === "number" && Number.isFinite(value) ? value : null;

/**
 * The baseline/effective pair behind one cell. Both sides come from the row the server
 * sent — PricingSimulationCalculator owns the M0/M1 rule and computes the baseline
 * margins itself, so nothing here re-derives a margin.
 */
const cellValues = (
  row: PricingRowDto,
  field: PricingEditField,
): { baseline: number | null; effective: number | null } => {
  switch (field) {
    case PricingEditField.Price:
      return { baseline: finite(row.baselinePrice), effective: finite(row.price) };
    case PricingEditField.MaterialCost:
      return {
        baseline: finite(row.baselineMaterialCost),
        effective: finite(row.materialCost),
      };
    case PricingEditField.ManufacturingCost:
      return {
        baseline: finite(row.baselineManufacturingCost),
        effective: finite(row.manufacturingCost),
      };
    case PricingEditField.M0Amount:
      return { baseline: finite(row.baselineM0Amount), effective: finite(row.m0Amount) };
    case PricingEditField.M0Percentage:
      return {
        baseline: finite(row.baselineM0Percentage),
        effective: finite(row.m0Percentage),
      };
    case PricingEditField.M1Amount:
      return { baseline: finite(row.baselineM1Amount), effective: finite(row.m1Amount) };
    case PricingEditField.M1Percentage:
      return {
        baseline: finite(row.baselineM1Percentage),
        effective: finite(row.m1Percentage),
      };
    case PricingEditField.ForecastQuantity:
      return {
        baseline: finite(row.baselineQuantity),
        effective: finite(row.forecastQuantity),
      };
    default:
      return { baseline: null, effective: null };
  }
};

export const pricingCellFacts = (
  row: PricingRowDto,
  field: PricingEditField,
): PricingCellFacts => {
  const kind = pricingValueKind(field);
  const { baseline, effective } = cellValues(row, field);
  const delta = baseline === null || effective === null ? null : effective - baseline;

  return {
    kind,
    baseline,
    effective,
    delta,
    relativeChange:
      baseline === null || effective === null
        ? null
        : relativeFromAbsolute(baseline, effective, kind),
  };
};

/** True when the cell differs from the real state by enough to be worth reporting. */
export const hasPricingChange = (facts: PricingCellFacts): boolean =>
  facts.delta !== null && Math.abs(facts.delta) >= VISIBLE_DELTA_THRESHOLD;

/**
 * The absolute value a relative change implies, always anchored to the REAL state
 * rather than to whatever the cell currently shows — so applying "+5 %" twice lands
 * on the same number instead of compounding.
 */
export const absoluteFromRelative = (
  baseline: number | null,
  relativeChange: number | null,
  kind: PricingValueKind,
): number | null => {
  if (baseline === null || relativeChange === null || !Number.isFinite(relativeChange)) {
    return null;
  }
  if (kind === "percentage") {
    return baseline + relativeChange;
  }
  // A percentage of nothing is nothing: there is no multiplier that moves 0 anywhere,
  // so the relative editor has nothing to offer on such a cell.
  if (baseline === 0) {
    return null;
  }
  return baseline * (1 + relativeChange / 100);
};

/** The inverse of absoluteFromRelative: what relative change produces this value. */
export const relativeFromAbsolute = (
  baseline: number | null,
  absolute: number | null,
  kind: PricingValueKind,
): number | null => {
  if (baseline === null || absolute === null || !Number.isFinite(absolute)) {
    return null;
  }
  if (kind === "percentage") {
    return absolute - baseline;
  }
  if (baseline === 0) {
    return null;
  }
  return ((absolute - baseline) / baseline) * 100;
};

/** Rounds to what the screen actually prints, so callers compare what the user sees. */
export const roundToDisplay = (value: number): number =>
  Number(value.toFixed(DISPLAY_DECIMALS));

/** Renders a value the way its column reads it. */
export const formatPricingValue = (
  kind: PricingValueKind,
  value: number | null,
): string => {
  switch (kind) {
    case "percentage":
      return formatPercentage(value);
    case "quantity":
      return formatNumber(value);
    default:
      return formatCurrency(value);
  }
};

/**
 * Whether a rise in this field is good news for the row. Price, margins and forecast
 * quantity improve as they grow; costs do the opposite. Without this the grid would
 * colour a material cost increase as a win.
 */
export const isIncreaseFavourable = (field: PricingEditField): boolean =>
  field !== PricingEditField.MaterialCost && field !== PricingEditField.ManufacturingCost;
