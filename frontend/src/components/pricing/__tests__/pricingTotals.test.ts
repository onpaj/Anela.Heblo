import { PricingRowDto } from "../../../api/generated/api-client";
import { computePricingTotals } from "../pricingTotals";

const buildRow = (overrides: Partial<PricingRowDto> = {}): PricingRowDto =>
  ({
    productCode: "PROD001",
    productName: "Test Product 1",
    baselinePrice: 100,
    baselineMaterialCost: 30,
    baselineManufacturingCost: 20,
    baselineQuantity: 10,
    price: 100,
    materialCost: 30,
    manufacturingCost: 20,
    forecastQuantity: 10,
    m0Amount: 70,
    m1Amount: 50,
    isEdited: false,
    isExcluded: false,
    ...overrides,
  }) as PricingRowDto;

describe("computePricingTotals", () => {
  it("sums revenue, M0 and M1 over the given rows", () => {
    // Arrange
    const rows = [
      buildRow(),
      buildRow({ productCode: "PROD002", baselineQuantity: 5, forecastQuantity: 5 }),
    ];

    // Act
    const totals = computePricingTotals(rows);

    // Assert
    expect(totals.revenueBefore).toBe(1500);
    expect(totals.revenueAfter).toBe(1500);
    expect(totals.m0Before).toBe(1050);
    expect(totals.m1Before).toBe(750);
    expect(totals.revenueDelta).toBe(0);
    expect(totals.revenueDeltaPercentage).toBe(0);
  });

  it("reports the delta between the baseline and the edited state", () => {
    // Arrange: price raised from 100 to 120 on a single product selling 10 pieces.
    const rows = [
      buildRow({
        price: 120,
        m0Amount: 90,
        m1Amount: 70,
        isEdited: true,
      }),
    ];

    // Act
    const totals = computePricingTotals(rows);

    // Assert
    expect(totals.revenueBefore).toBe(1000);
    expect(totals.revenueAfter).toBe(1200);
    expect(totals.revenueDelta).toBe(200);
    expect(totals.revenueDeltaPercentage).toBe(20);
    expect(totals.m0Delta).toBe(200);
    expect(totals.m1Delta).toBe(200);
    expect(totals.editedProductCount).toBe(1);
  });

  it("leaves excluded rows out of every sum but still counts them", () => {
    // Arrange
    const rows = [
      buildRow(),
      buildRow({ productCode: "PROD002", isExcluded: true, isEdited: true }),
    ];

    // Act
    const totals = computePricingTotals(rows);

    // Assert
    expect(totals.revenueBefore).toBe(1000);
    expect(totals.excludedProductCount).toBe(1);
    // An excluded row contributes to no total, so counting its edit would print
    // "1 produkt upraven" next to six zero deltas -- same rule as the server.
    expect(totals.editedProductCount).toBe(0);
  });

  it("reports a zero percentage instead of infinity when the baseline is zero", () => {
    // Arrange
    const rows = [
      buildRow({
        baselineQuantity: 0,
        forecastQuantity: 10,
      }),
    ];

    // Act
    const totals = computePricingTotals(rows);

    // Assert
    expect(totals.revenueBefore).toBe(0);
    expect(totals.revenueDeltaPercentage).toBe(0);
  });

  it("returns all-zero totals for an empty row set", () => {
    // Act
    const totals = computePricingTotals([]);

    // Assert
    expect(totals.revenueBefore).toBe(0);
    expect(totals.revenueAfter).toBe(0);
    expect(totals.m1Delta).toBe(0);
    expect(totals.editedProductCount).toBe(0);
    expect(totals.excludedProductCount).toBe(0);
  });

  it("treats missing row values as zero rather than producing NaN", () => {
    // Arrange: the generated DTO marks every field optional, so a partially
    // populated row must not poison the whole summary with NaN.
    const rows = [{ productCode: "PROD001" } as PricingRowDto];

    // Act
    const totals = computePricingTotals(rows);

    // Assert
    expect(totals.revenueBefore).toBe(0);
    expect(totals.revenueAfter).toBe(0);
    expect(totals.m0After).toBe(0);
  });
});
