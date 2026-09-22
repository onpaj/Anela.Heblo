import {
  IPricingOverrideDto,
  PricingEditField,
  PricingRowDto,
} from "../../../api/generated/api-client";
import { applyPricingBulkEdit } from "../pricingBulkEdit";

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
    isEdited: false,
    isExcluded: false,
    ...overrides,
  }) as PricingRowDto;

const rows = [
  buildRow(),
  buildRow({ productCode: "PROD002", baselinePrice: 200, price: 200 }),
  buildRow({ productCode: "PROD003", baselinePrice: 300, price: 300 }),
];

const findOverride = (
  overrides: readonly IPricingOverrideDto[],
  productCode: string,
): IPricingOverrideDto | undefined =>
  overrides.find((override) => override.productCode === productCode);

describe("applyPricingBulkEdit", () => {
  it("raises the price of every given product by the percentage", () => {
    // Arrange: the rows handed in are the ones the grid lists -- PROD003 is not.
    const listed = rows.slice(0, 2);

    // Act
    const result = applyPricingBulkEdit(listed, [], {
      field: PricingEditField.Price,
      percent: 10,
    });

    // Assert
    expect(result.appliedCount).toBe(2);
    expect(findOverride(result.overrides, "PROD001")?.price).toBe(110);
    expect(findOverride(result.overrides, "PROD002")?.price).toBe(220);
    expect(findOverride(result.overrides, "PROD003")).toBeUndefined();
  });

  it("lowers the price on a negative percentage", () => {
    // Act
    const result = applyPricingBulkEdit(rows, [], {
      field: PricingEditField.Price,
      percent: -25,
    });

    // Assert
    expect(findOverride(result.overrides, "PROD001")?.price).toBe(75);
  });

  it("anchors to the catalogue value instead of compounding on the last edit", () => {
    // Arrange: PROD001 already sits at +10 % from an earlier bulk edit.
    const existing: IPricingOverrideDto[] = [{ productCode: "PROD001", price: 110 }];

    // Act
    const result = applyPricingBulkEdit(rows, existing, {
      field: PricingEditField.Price,
      percent: 20,
    });

    // Assert: 20 % of the catalogue's 100, not of the pinned 110.
    expect(findOverride(result.overrides, "PROD001")?.price).toBe(120);
  });

  it("keeps the other pinned fields of an existing override", () => {
    // Arrange
    const existing: IPricingOverrideDto[] = [
      { productCode: "PROD001", materialCost: 25, forecastQuantity: 50 },
    ];

    // Act
    const result = applyPricingBulkEdit(rows, existing, {
      field: PricingEditField.Price,
      percent: 10,
    });

    // Assert
    const override = findOverride(result.overrides, "PROD001");
    expect(override?.price).toBe(110);
    expect(override?.materialCost).toBe(25);
    expect(override?.forecastQuantity).toBe(50);
    // The input must not be mutated on the way.
    expect(existing[0].price).toBeUndefined();
  });

  it("edits the material cost, the manufacturing cost and the forecast quantity too", () => {
    // Act
    const material = applyPricingBulkEdit(rows, [], {
      field: PricingEditField.MaterialCost,
      percent: 10,
    });
    const manufacturing = applyPricingBulkEdit(rows, [], {
      field: PricingEditField.ManufacturingCost,
      percent: 25,
    });
    const quantity = applyPricingBulkEdit(rows, [], {
      field: PricingEditField.ForecastQuantity,
      percent: 50,
    });

    // Assert
    expect(findOverride(material.overrides, "PROD001")?.materialCost).toBe(33);
    expect(findOverride(manufacturing.overrides, "PROD001")?.manufacturingCost).toBe(25);
    expect(findOverride(quantity.overrides, "PROD001")?.forecastQuantity).toBe(15);
  });

  it("skips a product whose new price would not be positive", () => {
    // Act: -100 % wipes the price out entirely.
    const result = applyPricingBulkEdit(rows, [], {
      field: PricingEditField.Price,
      percent: -100,
    });

    // Assert
    expect(result.appliedCount).toBe(0);
    expect(result.skippedCount).toBe(3);
    expect(result.overrides).toEqual([]);
  });

  it("skips a product whose new cost would be negative", () => {
    // Act: a cut of more than 100 % takes the cost below zero.
    const result = applyPricingBulkEdit(rows, [], {
      field: PricingEditField.MaterialCost,
      percent: -150,
    });

    // Assert
    expect(result.appliedCount).toBe(0);
    expect(result.skippedCount).toBe(3);
  });

  it("puts the column back to the catalogue values on zero percent", () => {
    // Arrange
    const existing: IPricingOverrideDto[] = [
      { productCode: "PROD001", price: 110 },
      { productCode: "PROD002", price: 220 },
    ];

    // Act
    const result = applyPricingBulkEdit(rows, existing, {
      field: PricingEditField.Price,
      percent: 0,
    });

    // Assert: no pinned price left, so the rows ride the live catalogue value again
    // and stop counting as edited.
    expect(result.appliedCount).toBe(2);
    expect(result.overrides).toEqual([]);
  });

  it("leaves the other pinned fields alone when a column is reset", () => {
    // Arrange
    const existing: IPricingOverrideDto[] = [
      { productCode: "PROD001", price: 110, materialCost: 25 },
    ];

    // Act
    const result = applyPricingBulkEdit(rows, existing, {
      field: PricingEditField.Price,
      percent: 0,
    });

    // Assert
    const override = findOverride(result.overrides, "PROD001");
    expect(override?.price).toBeUndefined();
    expect(override?.materialCost).toBe(25);
  });

  it("reports nothing applied when a reset finds nothing to clear", () => {
    // Act
    const result = applyPricingBulkEdit(rows, [], {
      field: PricingEditField.Price,
      percent: 0,
    });

    // Assert: a reset over untouched rows is not a failure, it is simply a no-op.
    expect(result.appliedCount).toBe(0);
    expect(result.skippedCount).toBe(0);
  });

  it("skips an excluded product, which has no usable baseline", () => {
    // Arrange
    const excludedRows = [buildRow({ isExcluded: true })];

    // Act
    const result = applyPricingBulkEdit(excludedRows, [], {
      field: PricingEditField.Price,
      percent: 10,
    });

    // Assert
    expect(result.appliedCount).toBe(0);
    expect(result.skippedCount).toBe(1);
  });

  it("skips a percentage change against a catalogue value of zero", () => {
    // Arrange: there is no multiplier that moves zero anywhere.
    const zeroRows = [
      buildRow({ baselineManufacturingCost: 0, manufacturingCost: 0 }),
    ];

    // Act
    const result = applyPricingBulkEdit(zeroRows, [], {
      field: PricingEditField.ManufacturingCost,
      percent: 10,
    });

    // Assert
    expect(result.appliedCount).toBe(0);
    expect(result.skippedCount).toBe(1);
  });

  it("rounds to the two decimals the grid prints", () => {
    // Arrange
    const oddRows = [buildRow({ baselinePrice: 206.61, price: 206.61 })];

    // Act
    const result = applyPricingBulkEdit(oddRows, [], {
      field: PricingEditField.Price,
      percent: 7,
    });

    // Assert
    expect(findOverride(result.overrides, "PROD001")?.price).toBe(221.07);
  });

  it("returns the overrides unchanged when no product is listed", () => {
    // Arrange
    const existing: IPricingOverrideDto[] = [{ productCode: "PROD001", price: 110 }];

    // Act
    const result = applyPricingBulkEdit([], existing, {
      field: PricingEditField.Price,
      percent: 10,
    });

    // Assert
    expect(result.appliedCount).toBe(0);
    expect(result.overrides).toEqual(existing);
  });
});
