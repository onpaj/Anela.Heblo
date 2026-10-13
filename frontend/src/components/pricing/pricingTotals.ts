import { IPricingTotalsDto, PricingRowDto } from "../../api/generated/api-client";

// Client-side twin of PricingSimulationCalculator.BuildTotals (backend). The server
// always sums the WHOLE filtered set; the summary can also be narrowed to the work
// group, which is a pure re-aggregation of rows the client already holds -- doing it
// here keeps that switch instant instead of costing a round trip per checkbox click.
// Only the aggregation lives here: every per-row value (price, costs, M0, M1) is
// still whatever the server derived, so the pricing rules stay in one place.

const sum = (values: readonly number[]): number =>
  values.reduce((total, value) => total + value, 0);

// A percentage of zero is reported as zero rather than infinity or NaN -- a total
// that starts at zero has no meaningful percentage change, and the UI must not
// print "∞". Same rule as the server's Percentage().
const percentage = (part: number, whole: number): number =>
  whole === 0 ? 0 : (part / whole) * 100;

const buildDelta = (before: number, after: number) => ({
  before,
  after,
  delta: after - before,
  deltaPercentage: percentage(after - before, before),
});

export const computePricingTotals = (
  rows: readonly PricingRowDto[],
): IPricingTotalsDto => {
  const counted = rows.filter((row) => !(row.isExcluded ?? false));

  const baselineQuantity = (row: PricingRowDto) => row.baselineQuantity ?? 0;
  const forecastQuantity = (row: PricingRowDto) => row.forecastQuantity ?? 0;
  const baselineM0 = (row: PricingRowDto) =>
    (row.baselinePrice ?? 0) - (row.baselineMaterialCost ?? 0);

  const revenue = buildDelta(
    sum(counted.map((row) => (row.baselinePrice ?? 0) * baselineQuantity(row))),
    sum(counted.map((row) => (row.price ?? 0) * forecastQuantity(row))),
  );

  const m0 = buildDelta(
    sum(counted.map((row) => baselineM0(row) * baselineQuantity(row))),
    sum(counted.map((row) => (row.m0Amount ?? 0) * forecastQuantity(row))),
  );

  const m1 = buildDelta(
    sum(
      counted.map(
        (row) =>
          (baselineM0(row) - (row.baselineManufacturingCost ?? 0)) *
          baselineQuantity(row),
      ),
    ),
    sum(counted.map((row) => (row.m1Amount ?? 0) * forecastQuantity(row))),
  );

  return {
    revenueBefore: revenue.before,
    revenueAfter: revenue.after,
    revenueDelta: revenue.delta,
    revenueDeltaPercentage: revenue.deltaPercentage,

    m0Before: m0.before,
    m0After: m0.after,
    m0Delta: m0.delta,
    m0DeltaPercentage: m0.deltaPercentage,

    m1Before: m1.before,
    m1After: m1.after,
    m1Delta: m1.delta,
    m1DeltaPercentage: m1.deltaPercentage,

    // Counted over the same population as every other total, i.e. rows with data.
    editedProductCount: counted.filter((row) => row.isEdited ?? false).length,
    excludedProductCount: rows.length - counted.length,
  };
};
