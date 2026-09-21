import { PricingEditField, PricingRowDto } from "../../../api/generated/api-client";
import {
  absoluteFromRelative,
  hasPricingChange,
  pricingCellFacts,
  pricingValueKind,
  relativeFromAbsolute,
} from "../pricingDelta";

// Baseline: price 420, material 175, manufacturing 70, sold 1000.
// => baseline M0 = 245 (58.333%), M1 = 175 (41.667%)
const row = (overrides: Partial<PricingRowDto> = {}): PricingRowDto =>
  ({
    productCode: "P1",
    productName: "Product P1",
    baselinePrice: 420,
    baselineMaterialCost: 175,
    baselineManufacturingCost: 70,
    baselineQuantity: 1000,
    baselineM0Amount: 245,
    baselineM1Amount: 175,
    baselineM0Percentage: 58.33333333,
    baselineM1Percentage: 41.66666667,
    price: 420,
    materialCost: 175,
    manufacturingCost: 70,
    forecastQuantity: 1000,
    m0Amount: 245,
    m1Amount: 175,
    m0Percentage: 58.33333333,
    m1Percentage: 41.66666667,
    ...overrides,
  }) as PricingRowDto;

describe("pricingValueKind", () => {
  test("treats money columns as currency", () => {
    expect(pricingValueKind(PricingEditField.Price)).toBe("currency");
    expect(pricingValueKind(PricingEditField.MaterialCost)).toBe("currency");
    expect(pricingValueKind(PricingEditField.ManufacturingCost)).toBe("currency");
    expect(pricingValueKind(PricingEditField.M0Amount)).toBe("currency");
    expect(pricingValueKind(PricingEditField.M1Amount)).toBe("currency");
  });

  test("treats the margin ratio columns as percentage and the forecast as quantity", () => {
    expect(pricingValueKind(PricingEditField.M0Percentage)).toBe("percentage");
    expect(pricingValueKind(PricingEditField.M1Percentage)).toBe("percentage");
    expect(pricingValueKind(PricingEditField.ForecastQuantity)).toBe("quantity");
  });
});

describe("pricingCellFacts", () => {
  test("pairs each field with its own baseline from the same row", () => {
    const edited = row({ price: 500, m0Amount: 325, m0Percentage: 65 });

    expect(pricingCellFacts(edited, PricingEditField.Price)).toMatchObject({
      baseline: 420,
      effective: 500,
      delta: 80,
    });
    expect(pricingCellFacts(edited, PricingEditField.M0Amount)).toMatchObject({
      baseline: 245,
      effective: 325,
    });
    expect(pricingCellFacts(edited, PricingEditField.ManufacturingCost)).toMatchObject({
      baseline: 70,
      effective: 70,
      delta: 0,
    });
  });

  test("reports money and quantity changes as a percentage of the real state", () => {
    const facts = pricingCellFacts(row({ price: 504 }), PricingEditField.Price);

    // 504 vs a real 420 is +20 %.
    expect(facts.relativeChange).toBeCloseTo(20, 6);
  });

  test("reports margin ratio changes as percentage points, not a percentage of a percentage", () => {
    // 58.33 % -> 65 % is +6.67 percentage points. As a percentage OF the baseline it
    // would read +11.43 %, which is the confusing reading this rule exists to avoid.
    const facts = pricingCellFacts(row({ m0Percentage: 65 }), PricingEditField.M0Percentage);

    expect(facts.relativeChange).toBeCloseTo(6.6667, 3);
  });

  test("has no relative change to report when the real state is zero", () => {
    const facts = pricingCellFacts(
      row({ baselineManufacturingCost: 0, manufacturingCost: 25 }),
      PricingEditField.ManufacturingCost,
    );

    expect(facts.delta).toBe(25);
    expect(facts.relativeChange).toBeNull();
  });

  test("returns nulls rather than guessing when the row omits a value", () => {
    const facts = pricingCellFacts(row({ price: undefined }), PricingEditField.Price);

    expect(facts.effective).toBeNull();
    expect(facts.delta).toBeNull();
    expect(facts.relativeChange).toBeNull();
  });
});

describe("hasPricingChange", () => {
  test("is false for an untouched cell", () => {
    expect(hasPricingChange(pricingCellFacts(row(), PricingEditField.Price))).toBe(false);
  });

  test("is false for a change too small to be displayed", () => {
    // The tooltip prints two decimals, so a 0.001 Kč drift would render as "+0,00 %"
    // -- noise on a cell nobody meaningfully changed.
    const facts = pricingCellFacts(row({ price: 420.001 }), PricingEditField.Price);

    expect(hasPricingChange(facts)).toBe(false);
  });

  test("is true once the change is visible at two decimals", () => {
    expect(
      hasPricingChange(pricingCellFacts(row({ price: 420.01 }), PricingEditField.Price)),
    ).toBe(true);
  });
});

describe("relative <-> absolute conversion", () => {
  test("a percentage change is applied to the real state, so it is idempotent", () => {
    // +5 % from a baseline of 420 is 441 whatever the cell currently shows -- typing
    // +5 % twice must not compound to 463.05.
    expect(absoluteFromRelative(420, 5, "currency")).toBeCloseTo(441, 6);
    expect(absoluteFromRelative(420, -3, "currency")).toBeCloseTo(407.4, 6);
  });

  test("percentage points are added to the real state for margin ratio cells", () => {
    expect(absoluteFromRelative(58.33, 5, "percentage")).toBeCloseTo(63.33, 6);
  });

  test("round-trips an absolute value back to the relative change that produces it", () => {
    expect(relativeFromAbsolute(420, 441, "currency")).toBeCloseTo(5, 6);
    expect(relativeFromAbsolute(58.33, 63.33, "percentage")).toBeCloseTo(5, 6);
  });

  test("cannot express a relative change against a zero real state", () => {
    expect(relativeFromAbsolute(0, 25, "currency")).toBeNull();
    expect(absoluteFromRelative(0, 25, "currency")).toBeNull();
  });

  test("percentage points still work against a zero baseline margin", () => {
    // Unlike a ratio, adding points to 0 % is perfectly meaningful.
    expect(absoluteFromRelative(0, 12, "percentage")).toBeCloseTo(12, 6);
    expect(relativeFromAbsolute(0, 12, "percentage")).toBeCloseTo(12, 6);
  });
});
