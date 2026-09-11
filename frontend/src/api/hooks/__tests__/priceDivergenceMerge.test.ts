import { countChangedRows, mergeSyncedRows } from "../priceDivergenceMerge";
import { PriceDivergenceKind, PriceDivergenceRowDto } from "../../generated/api-client";

const row = (
  productCode: string,
  kind: PriceDivergenceKind,
  shoptetPriceWithVat: number,
): PriceDivergenceRowDto =>
  PriceDivergenceRowDto.fromJS({
    productCode,
    productName: `Product ${productCode}`,
    shoptetPriceWithVat,
    flexiPriceWithVat: shoptetPriceWithVat,
    kind,
  });

const report = (...rows: PriceDivergenceRowDto[]) => ({ rows, summary: undefined });

test("replaces a row in place, keeping the order of the report it merges into", () => {
  // Arrange
  const current = report(
    row("A", PriceDivergenceKind.FlexiDiffers, 100),
    row("B", PriceDivergenceKind.InAgreement, 200),
    row("C", PriceDivergenceKind.FlexiDiffers, 300),
  );

  // Act
  const merged = mergeSyncedRows(current, [row("B", PriceDivergenceKind.FlexiDiffers, 250)]);

  // Assert
  expect(merged.rows.map((r) => r.productCode)).toEqual(["A", "B", "C"]);
  expect(merged.rows[1].shoptetPriceWithVat).toBe(250);
  expect(merged.rows[1].kind).toBe(PriceDivergenceKind.FlexiDiffers);
});

test("leaves rows outside the synced selection untouched", () => {
  // Arrange
  const untouched = row("A", PriceDivergenceKind.FlexiDiffers, 100);
  const current = report(untouched, row("B", PriceDivergenceKind.InAgreement, 200));

  // Act
  const merged = mergeSyncedRows(current, [row("B", PriceDivergenceKind.FlexiDiffers, 250)]);

  // Assert
  expect(merged.rows[0]).toBe(untouched);
});

test("does not mutate the report it was given", () => {
  // Arrange
  const current = report(row("A", PriceDivergenceKind.InAgreement, 100));

  // Act
  mergeSyncedRows(current, [row("A", PriceDivergenceKind.FlexiDiffers, 150)]);

  // Assert
  expect(current.rows[0].kind).toBe(PriceDivergenceKind.InAgreement);
  expect(current.rows[0].shoptetPriceWithVat).toBe(100);
});

test("matches product codes case-insensitively, as the backend does", () => {
  // Arrange
  const current = report(row("MAS001180", PriceDivergenceKind.InAgreement, 390));

  // Act
  const merged = mergeSyncedRows(current, [row("mas001180", PriceDivergenceKind.FlexiDiffers, 420)]);

  // Assert
  expect(merged.rows).toHaveLength(1);
  expect(merged.rows[0].shoptetPriceWithVat).toBe(420);
});

// The server-side summary counted the catalogue as it stood before the sync. Leaving it
// alone would let a tile contradict the very row the operator just refreshed.
test("re-tallies the summary over the merged rows", () => {
  // Arrange
  const current = {
    rows: [
      row("A", PriceDivergenceKind.InAgreement, 100),
      row("B", PriceDivergenceKind.InAgreement, 200),
      row("C", PriceDivergenceKind.MissingInFlexi, 300),
    ],
    summary: undefined,
  };

  // Act
  const merged = mergeSyncedRows(current, [row("B", PriceDivergenceKind.FlexiDiffers, 250)]);

  // Assert
  expect(merged.summary).toEqual(
    expect.objectContaining({
      totalInScope: 3,
      inAgreementCount: 1,
      flexiDiffersCount: 1,
      missingInFlexiCount: 1,
      missingInShoptetCount: 0,
      flexiPriceTypeUnknownCount: 0,
    }),
  );
});

test("ignores a synced row for a product the report does not hold", () => {
  // Arrange
  const current = report(row("A", PriceDivergenceKind.InAgreement, 100));

  // Act
  const merged = mergeSyncedRows(current, [row("GHOST", PriceDivergenceKind.FlexiDiffers, 999)]);

  // Assert
  expect(merged.rows.map((r) => r.productCode)).toEqual(["A"]);
});

describe("countChangedRows", () => {
  test("counts nothing when the sync returned exactly what the report already held", () => {
    // Arrange
    const previous = [row("A", PriceDivergenceKind.InAgreement, 100)];

    // Act
    const changed = countChangedRows(previous, [row("A", PriceDivergenceKind.InAgreement, 100)]);

    // Assert
    expect(changed).toBe(0);
  });

  test("counts a row whose Shoptet price moved", () => {
    // Arrange
    const previous = [row("A", PriceDivergenceKind.InAgreement, 100)];

    // Act
    const changed = countChangedRows(previous, [row("A", PriceDivergenceKind.InAgreement, 120)]);

    // Assert
    expect(changed).toBe(1);
  });

  // A row can keep both prices and still change what it tells the operator — a Flexi price
  // type that resolved turns FlexiPriceTypeUnknown into a real verdict.
  test("counts a row whose verdict changed even though its prices did not", () => {
    // Arrange
    const previous = [row("A", PriceDivergenceKind.FlexiPriceTypeUnknown, 100)];

    // Act
    const changed = countChangedRows(previous, [row("A", PriceDivergenceKind.InAgreement, 100)]);

    // Assert
    expect(changed).toBe(1);
  });

  test("matches product codes case-insensitively, as the merge does", () => {
    // Arrange
    const previous = [row("MAS001180", PriceDivergenceKind.InAgreement, 390)];

    // Act
    const changed = countChangedRows(previous, [row("mas001180", PriceDivergenceKind.FlexiDiffers, 420)]);

    // Assert
    expect(changed).toBe(1);
  });

  // mergeSyncedRows drops such a row, so counting it would report a change the operator
  // cannot see anywhere in the table.
  test("ignores a synced row for a product the report does not hold", () => {
    // Arrange
    const previous = [row("A", PriceDivergenceKind.InAgreement, 100)];

    // Act
    const changed = countChangedRows(previous, [row("GHOST", PriceDivergenceKind.FlexiDiffers, 999)]);

    // Assert
    expect(changed).toBe(0);
  });

  test("counts a product renamed in the catalogue, because the merge shows the new name", () => {
    // Arrange — same prices, same verdict, different name. The merge swaps the whole row, so
    // the Nazev cell visibly changes; reporting "beze zmen" over it would contradict the table.
    const previous = [row("A", PriceDivergenceKind.InAgreement, 100)];
    const renamed = PriceDivergenceRowDto.fromJS({
      productCode: "A",
      productName: "Maska pleťová (nový název)",
      shoptetPriceWithVat: 100,
      flexiPriceWithVat: 100,
      kind: PriceDivergenceKind.InAgreement,
    });

    // Act & Assert
    expect(countChangedRows(previous, [renamed])).toBe(1);
  });
});
