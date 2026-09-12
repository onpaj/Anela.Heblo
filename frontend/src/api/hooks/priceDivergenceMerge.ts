import {
  PriceDivergenceKind,
  PriceDivergenceRowDto,
  PriceDivergenceSummaryDto,
} from "../generated/api-client";

/** The cached shape of the divergence report, as `usePriceDivergenceReport` returns it. */
export interface PriceDivergenceReportData {
  rows: PriceDivergenceRowDto[];
  summary: PriceDivergenceSummaryDto | undefined;
}

const normalizeCode = (productCode: string | undefined) => (productCode ?? "").toLowerCase();

const countOfKind = (rows: PriceDivergenceRowDto[], kind: PriceDivergenceKind) =>
  rows.filter((row) => row.kind === kind).length;

/**
 * Counts the rows by the `kind` the backend already classified — it never decides for itself
 * whether a row agrees, so the comparison rules stay in `PriceComparisonService` alone.
 */
export const tallySummary = (rows: PriceDivergenceRowDto[]): PriceDivergenceSummaryDto =>
  // `fromJS`, never `new PriceDivergenceSummaryDto(...)`: constructing the generated DTOs
  // directly is unreliable under this project's class-fields transform.
  PriceDivergenceSummaryDto.fromJS({
    totalInScope: rows.length,
    inAgreementCount: countOfKind(rows, PriceDivergenceKind.InAgreement),
    flexiDiffersCount: countOfKind(rows, PriceDivergenceKind.FlexiDiffers),
    missingInShoptetCount: countOfKind(rows, PriceDivergenceKind.MissingInShoptet),
    missingInFlexiCount: countOfKind(rows, PriceDivergenceKind.MissingInFlexi),
    flexiPriceTypeUnknownCount: countOfKind(rows, PriceDivergenceKind.FlexiPriceTypeUnknown),
    flexiVatRateUnknownCount: countOfKind(rows, PriceDivergenceKind.FlexiVatRateUnknown),
  });

/**
 * Everything a sync can move on a row: the two live prices, the Flexi price type the
 * with-VAT figure was derived from, the verdict the backend drew from them, and the product
 * name — the merge swaps the whole row, so a product renamed in the catalogue between the
 * report load and the sync visibly changes on screen, and reporting "beze změn" over a cell
 * the operator just watched change would read as a lie. The difference columns are computed
 * from the prices, so comparing them too would only ever agree with this.
 */
const hasSameComparison = (a: PriceDivergenceRowDto, b: PriceDivergenceRowDto) =>
  a.shoptetPriceWithVat === b.shoptetPriceWithVat &&
  a.flexiPriceWithVat === b.flexiPriceWithVat &&
  a.flexiPriceType === b.flexiPriceType &&
  a.productName === b.productName &&
  a.kind === b.kind;

/**
 * How many of the synced rows actually differ from what the report was showing. A sync that
 * re-reads two live systems and finds nothing changed leaves the table byte-for-byte
 * identical, so this count is the only thing that can tell the operator it ran at all.
 *
 * Counts only rows the report holds, matching `mergeSyncedRows`: a synced row for a product
 * that is not in the report is dropped by the merge, so counting it would claim a change the
 * operator cannot find anywhere in the table.
 */
export const countChangedRows = (
  previousRows: PriceDivergenceRowDto[],
  syncedRows: PriceDivergenceRowDto[],
): number => {
  const previousByCode = new Map(previousRows.map((row) => [normalizeCode(row.productCode), row]));

  return syncedRows.filter((synced) => {
    const previous = previousByCode.get(normalizeCode(synced.productCode));
    return previous !== undefined && !hasSameComparison(previous, synced);
  }).length;
};

/**
 * Folds freshly synced rows into the cached report: rows outside the synced selection keep
 * their identity, and the summary is re-tallied so no tile can contradict a row the operator
 * just refreshed. The report passed in is never modified.
 */
export const mergeSyncedRows = (
  current: PriceDivergenceReportData,
  syncedRows: PriceDivergenceRowDto[],
): PriceDivergenceReportData => {
  const syncedByCode = new Map(syncedRows.map((row) => [normalizeCode(row.productCode), row]));

  // A requested product the backend returned no row for keeps the values it already had.
  // That happens when it stopped being a priced catalogue product between the report load
  // and the sync — rare, and showing the last known comparison beats blanking the row.
  const rows = current.rows.map((row) => syncedByCode.get(normalizeCode(row.productCode)) ?? row);

  return { rows, summary: tallySummary(rows) };
};
