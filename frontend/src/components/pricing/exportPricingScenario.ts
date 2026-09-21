import { PricingRowDto } from "../../api/generated/api-client";
import { exportToXlsx } from "../../utils/exportToXlsx";
import { formatCurrency, formatNumber, formatPercentage } from "../../utils/formatters";

// PricingRowDto carries the EFFECTIVE (post-edit) margin in m0Amount/m1Amount, but no
// baselineM0Amount/baselineM1Amount counterpart -- only the raw baseline* inputs
// (price, material cost, manufacturing cost) are on the row. The backend's
// PricingSimulationCalculator.BuildRow computes M0 = Price - MaterialCost and
// M1 = M0 - ManufacturingCost; the two helpers below apply that exact same formula
// to the row's baseline* fields so the "před" column is computed identically to how
// the server computes "po". This is the only arithmetic this file does beyond
// formatting -- it exists solely because the row never carries a pre-computed
// baseline margin to read directly.
const baselineM0 = (row: PricingRowDto): number =>
  (row.baselinePrice ?? 0) - (row.baselineMaterialCost ?? 0);

const baselineM1 = (row: PricingRowDto): number =>
  baselineM0(row) - (row.baselineManufacturingCost ?? 0);

// Mirrors the backend's own revenueBefore/revenueAfter totals formula
// (PricingSimulationCalculator.BuildTotals: Price * Quantity), just per row instead
// of summed across rows.
const baselineRevenue = (row: PricingRowDto): number =>
  (row.baselinePrice ?? 0) * (row.baselineQuantity ?? 0);

const effectiveRevenue = (row: PricingRowDto): number =>
  (row.price ?? 0) * (row.forecastQuantity ?? 0);

interface ExportColumn {
  header: string;
  value: (row: PricingRowDto) => unknown;
}

// Column order is a hard requirement (Task 10 brief): Kód, Název, Cena před, Cena po,
// M0 před Kč, M0 po Kč, M0 po %, M1 před Kč, M1 po Kč, M1 po %, Prodáno 12m,
// Předpověď, Δ obrat, Δ M1.
const columns: ExportColumn[] = [
  { header: "Kód", value: (row) => row.productCode ?? "" },
  { header: "Název", value: (row) => row.productName ?? "" },
  { header: "Cena před", value: (row) => formatCurrency(row.baselinePrice ?? null) },
  { header: "Cena po", value: (row) => formatCurrency(row.price ?? null) },
  { header: "M0 před Kč", value: (row) => formatCurrency(baselineM0(row)) },
  { header: "M0 po Kč", value: (row) => formatCurrency(row.m0Amount ?? null) },
  { header: "M0 po %", value: (row) => formatPercentage(row.m0Percentage ?? null) },
  { header: "M1 před Kč", value: (row) => formatCurrency(baselineM1(row)) },
  { header: "M1 po Kč", value: (row) => formatCurrency(row.m1Amount ?? null) },
  { header: "M1 po %", value: (row) => formatPercentage(row.m1Percentage ?? null) },
  { header: "Prodáno 12m", value: (row) => formatNumber(row.baselineQuantity ?? null) },
  { header: "Předpověď", value: (row) => formatNumber(row.forecastQuantity ?? null) },
  {
    header: "Δ obrat",
    value: (row) => formatCurrency(effectiveRevenue(row) - baselineRevenue(row)),
  },
  {
    header: "Δ M1",
    value: (row) => formatCurrency((row.m1Amount ?? 0) - baselineM1(row)),
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
