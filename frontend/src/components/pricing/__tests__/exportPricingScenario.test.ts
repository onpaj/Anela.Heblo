import { exportPricingScenario } from "../exportPricingScenario";
import { exportToXlsx } from "../../../utils/exportToXlsx";
import { PricingRowDto } from "../../../api/generated/api-client";

jest.mock("../../../utils/exportToXlsx", () => ({
  exportToXlsx: jest.fn().mockResolvedValue(undefined),
}));

const mockExportToXlsx = exportToXlsx as jest.MockedFunction<typeof exportToXlsx>;

// Plain object literals, not `new PricingRowDto(...)` -- see PriceAnalysis.test.tsx
// for why (Babel class-field re-initialization drops constructor-assigned fields on
// the generated Response/Dto classes).
const buildRow = (overrides: Partial<PricingRowDto> = {}): PricingRowDto =>
  ({
    productCode: "PROD001",
    productName: "Test Product 1",
    baselinePrice: 150,
    baselineMaterialCost: 30,
    baselineManufacturingCost: 20,
    baselineQuantity: 100,
    price: 150,
    materialCost: 30,
    manufacturingCost: 20,
    forecastQuantity: 100,
    m0Amount: 120,
    m0Percentage: 80,
    m1Amount: 100,
    m1Percentage: 66.67,
    // Server-computed by PricingSimulationCalculator.BuildRow, alongside m0Amount/
    // m1Amount -- see that class's doc comment for why the row is the single source
    // of truth for both the "before" and "after" side of M0/M1.
    baselineM0Amount: 120,
    baselineM1Amount: 100,
    isEdited: false,
    isExcluded: false,
    baselineDrifted: false,
    ...overrides,
  }) as PricingRowDto;

describe("exportPricingScenario", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("delegates to exportToXlsx with the exact required column order", async () => {
    await exportPricingScenario([buildRow({ isEdited: true })], "Jarní akce");

    expect(mockExportToXlsx).toHaveBeenCalledTimes(1);
    const [, columns] = mockExportToXlsx.mock.calls[0];

    expect(columns.map((c) => c.header)).toEqual([
      "Kód",
      "Název",
      "Cena před",
      "Cena po",
      "M0 před Kč",
      "M0 po Kč",
      "M0 po %",
      "M1 před Kč",
      "M1 po Kč",
      "M1 po %",
      "Prodáno 12m",
      "Předpověď",
      "Δ obrat",
      "Δ M1",
    ]);
  });

  it("exports only edited rows -- the ceník draft is a list of changes, not a catalog dump", async () => {
    const editedRow = buildRow({ productCode: "PROD001", isEdited: true });
    const untouchedRow = buildRow({ productCode: "PROD002", isEdited: false });

    await exportPricingScenario([editedRow, untouchedRow], "Jarní akce");

    const [rows] = mockExportToXlsx.mock.calls[0];
    expect(rows).toEqual([editedRow]);
  });

  it("excludes every row when none are edited", async () => {
    await exportPricingScenario(
      [buildRow({ isEdited: false }), buildRow({ productCode: "PROD002", isEdited: false })],
      "Jarní akce",
    );

    const [rows] = mockExportToXlsx.mock.calls[0];
    expect(rows).toEqual([]);
  });

  it("includes the scenario name in the filename", async () => {
    await exportPricingScenario([buildRow({ isEdited: true })], "Jarní akce");

    const [, , filename] = mockExportToXlsx.mock.calls[0];
    expect(filename).toContain("Jarní akce");
    expect(filename).toMatch(/\.xlsx$/);
  });

  it("reads the server-computed baseline margins and emits every numeric column as a RAW number, computing only the revenue/M1 totals from numbers the row already carries", async () => {
    const row = buildRow({
      productCode: "PROD001",
      productName: "Test Product",
      baselinePrice: 150,
      baselineMaterialCost: 30,
      baselineManufacturingCost: 20,
      baselineQuantity: 100,
      baselineM0Amount: 120,
      baselineM1Amount: 100,
      price: 175,
      m0Amount: 145,
      m0Percentage: 82.86,
      m1Amount: 125,
      m1Percentage: 71.43,
      forecastQuantity: 110,
      isEdited: true,
    });

    await exportPricingScenario([row], "Jarní akce");

    const [, columns] = mockExportToXlsx.mock.calls[0];
    const valueFor = (header: string) =>
      columns.find((c) => c.header === header)!.value(row);

    expect(valueFor("Kód")).toBe("PROD001");
    expect(valueFor("Název")).toBe("Test Product");
    // Raw numbers, not formatted strings: a draft ceník is summed, sorted and
    // filtered in Excel, and "500,00 Kč" as text defeats all three.
    expect(valueFor("Cena před")).toBe(150);
    expect(valueFor("Cena po")).toBe(175);
    // M0 před Kč reads row.baselineM0Amount directly -- server-computed by
    // PricingSimulationCalculator, not re-derived here.
    expect(valueFor("M0 před Kč")).toBe(120);
    expect(valueFor("M0 po Kč")).toBe(145);
    expect(valueFor("M0 po %")).toBe(82.86);
    // M1 před Kč reads row.baselineM1Amount directly, same as above.
    expect(valueFor("M1 před Kč")).toBe(100);
    expect(valueFor("M1 po Kč")).toBe(125);
    expect(valueFor("M1 po %")).toBe(71.43);
    expect(valueFor("Prodáno 12m")).toBe(100);
    expect(valueFor("Předpověď")).toBe(110);
    // Δ obrat = (price * forecastQuantity) - (baselinePrice * baselineQuantity)
    //         = (175 * 110) - (150 * 100) = 19250 - 15000 = 4250.
    expect(valueFor("Δ obrat")).toBe(4250);
    // Δ M1 mirrors the backend's m1After - m1Before, per row:
    //   (m1Amount * forecastQuantity) - (baselineM1Amount * baselineQuantity)
    //   = (125 * 110) - (100 * 100) = 13750 - 10000 = 3750.
    // Both Δ columns are totals on the same basis; Δ M1 used to be per-unit (25).
    expect(valueFor("Δ M1")).toBe(3750);
  });

  it("emits no formatted string in any numeric column", async () => {
    const row = buildRow({ isEdited: true });

    await exportPricingScenario([row], "Jarní akce");

    const [, columns] = mockExportToXlsx.mock.calls[0];
    const textColumns = ["Kód", "Název"];

    columns
      .filter((c) => !textColumns.includes(c.header))
      .forEach((column) => {
        const value = column.value(row);
        // A string here means the cell lands in Excel as text and cannot be
        // summed or sorted -- the exact defect this column set exists to avoid.
        expect(typeof value).toBe("number");
      });
  });

  it("keeps Δ M1 on the same quantity-weighted basis as Δ obrat when the forecast quantity moves", async () => {
    // Quantity alone changes: per-unit M1 is identical before and after, so a
    // per-unit Δ M1 would report 0 while Δ obrat reports a real change.
    const row = buildRow({
      baselinePrice: 150,
      price: 150,
      baselineQuantity: 100,
      forecastQuantity: 120,
      baselineM1Amount: 100,
      m1Amount: 100,
      isEdited: true,
    });

    await exportPricingScenario([row], "Jarní akce");

    const [, columns] = mockExportToXlsx.mock.calls[0];
    const valueFor = (header: string) =>
      columns.find((c) => c.header === header)!.value(row);

    expect(valueFor("Δ obrat")).toBe(150 * 120 - 150 * 100);
    expect(valueFor("Δ M1")).toBe(100 * 120 - 100 * 100);
  });
});
