import { PricingRowDto } from "../../api/generated/api-client";
import { exportToXlsx } from "../../utils/exportToXlsx";

// PricingSimulationCalculator (the single source of truth for the M0/M1 formula --
// see its class doc comment) computes BaselineM0Amount/BaselineM1Amount server-side,
// right alongside the effective M0Amount/M1Amount, using the exact same rule. This
// file reads them rather than re-deriving a margin client-side, so the screen and
// this export can never disagree with the server about how M0/M1 are calculated.

// Mirrors the backend's own revenueBefore/revenueAfter totals formula
// (PricingSimulationCalculator.BuildTotals: Price * Quantity), just per row instead
// of summed across rows.
const baselineRevenue = (row: PricingRowDto): number =>
  (row.baselinePrice ?? 0) * (row.baselineQuantity ?? 0);

const effectiveRevenue = (row: PricingRowDto): number =>
  (row.price ?? 0) * (row.forecastQuantity ?? 0);

// Same mirror for M1 (BuildTotals: m1Before = baseline M1 per unit * BaselineQuantity,
// m1After = M1Amount * ForecastQuantity). Both delta columns are therefore totals on the
// same basis -- Δ M1 used to be a per-unit figure sitting next to a quantity-weighted
// Δ obrat, which invited summing two different things in the same sheet.
const baselineM1Total = (row: PricingRowDto): number =>
  (row.baselineM1Amount ?? 0) * (row.baselineQuantity ?? 0);

const effectiveM1Total = (row: PricingRowDto): number =>
  (row.m1Amount ?? 0) * (row.forecastQuantity ?? 0);

interface ExportColumn {
  header: string;
  value: (row: PricingRowDto) => unknown;
}

// Column order is a hard requirement (Task 10 brief): Kód, Název, Cena před, Cena po,
// M0 před Kč, M0 po Kč, M0 po %, M1 před Kč, M1 po Kč, M1 po %, Prodáno 12m,
// Předpověď, Δ obrat, Δ M1.
//
// Numeric columns carry RAW numbers, never formatted strings: this sheet is a draft
// ceník people total, sort and filter, and "500,00 Kč" as text defeats all three.
// Excel applies the locale display itself. Matches every other exportToXlsx caller in
// this repo (see PurchaseStockAnalysis.tsx).
const columns: ExportColumn[] = [
  { header: "Kód", value: (row) => row.productCode ?? "" },
  { header: "Název", value: (row) => row.productName ?? "" },
  { header: "Cena před", value: (row) => row.baselinePrice ?? null },
  { header: "Cena po", value: (row) => row.price ?? null },
  { header: "M0 před Kč", value: (row) => row.baselineM0Amount ?? null },
  { header: "M0 po Kč", value: (row) => row.m0Amount ?? null },
  { header: "M0 po %", value: (row) => row.m0Percentage ?? null },
  { header: "M1 před Kč", value: (row) => row.baselineM1Amount ?? null },
  { header: "M1 po Kč", value: (row) => row.m1Amount ?? null },
  { header: "M1 po %", value: (row) => row.m1Percentage ?? null },
  { header: "Prodáno 12m", value: (row) => row.baselineQuantity ?? null },
  { header: "Předpověď", value: (row) => row.forecastQuantity ?? null },
  {
    header: "Δ obrat",
    value: (row) => effectiveRevenue(row) - baselineRevenue(row),
  },
  {
    header: "Δ M1",
    value: (row) => effectiveM1Total(row) - baselineM1Total(row),
  },
];

/**
 * Exports the current ceník draft as an XLSX file: ONLY the rows the user actually
 * edited (isEdited), because this is a list of proposed changes, not a catalog dump.
 */
export async function exportPricingScenario(
  rows: PricingRowDto[],
  scenarioName: string,
): Promise<void> {
  const editedRows = rows.filter((row) => row.isEdited === true);
  const datePart = new Date().toISOString().slice(0, 10);
  const filename = `cenik-navrh-${scenarioName}-${datePart}.xlsx`;
  await exportToXlsx(editedRows, columns, filename);
}
