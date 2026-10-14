import { JournalEntryDto } from "../../../../api/generated/api-client";
import {
  type YearMonth,
  JOURNAL_MARKER_COLOR,
  buildJournalPointStyling,
  buildJournalTooltipCallback,
  getJournalEntriesForYearMonth,
} from "../../../charts/journalMarkers";
import {
  CatalogSalesRecordDto,
  CatalogConsumedRecordDto,
  CatalogPurchaseRecordDto,
  CatalogManufactureRecordDto,
} from "../../../../api/hooks/useCatalog";

// Generate last 13 months labels
export const generateMonthLabels = (): string[] => {
  const months = [];
  const now = new Date();

  for (let i = 12; i >= 0; i--) {
    const date = new Date(now.getFullYear(), now.getMonth() - i, 1);
    months.push(
      date.toLocaleDateString("cs-CZ", { month: "short", year: "numeric" }),
    );
  }

  return months;
};

// Calendar month of a slot in the 13-month window (index 12 = current month)
const getMonthForIndex = (monthIndex: number): YearMonth => {
  const now = new Date();
  const date = new Date(now.getFullYear(), now.getMonth() - (12 - monthIndex), 1);
  return { year: date.getFullYear(), month: date.getMonth() + 1 };
};

const getMonthsForLength = (dataLength: number): YearMonth[] =>
  Array.from({ length: dataLength }, (_, i) => getMonthForIndex(i));

// Helper function to get journal entries for a specific month
export const getJournalEntriesForMonth = (
  monthIndex: number,
  journalEntries: JournalEntryDto[],
): JournalEntryDto[] =>
  getJournalEntriesForYearMonth(journalEntries, getMonthForIndex(monthIndex));

// Map data to monthly array based on year/month
export const mapDataToMonthlyArray = (
  data:
    | CatalogSalesRecordDto[]
    | CatalogConsumedRecordDto[]
    | CatalogPurchaseRecordDto[]
    | CatalogManufactureRecordDto[],
  valueKey: "amountTotal" | "amount",
): number[] => {
  const monthlyData = new Array(13).fill(0);
  const now = new Date();
  const currentYear = now.getFullYear();
  const currentMonth = now.getMonth() + 1; // JavaScript months are 0-based, convert to 1-based

  // Create a map for quick lookup of data by year-month key
  const dataMap = new Map<string, number>();
  data.forEach((record) => {
    if (record.year && record.month) {
      const key = `${record.year}-${record.month}`;
      const value = (record as any)[valueKey] || 0;
      dataMap.set(key, value);
    }
  });

  // Fill the array with data for the last 13 months
  for (let i = 0; i < 13; i++) {
    const monthsBack = 12 - i; // 12 months back to current month
    let adjustedYear = currentYear;
    let adjustedMonth = currentMonth - monthsBack;

    // Handle year transitions
    if (adjustedMonth <= 0) {
      adjustedYear--;
      adjustedMonth += 12;
    }

    const key = `${adjustedYear}-${adjustedMonth}`;
    const value = dataMap.get(key) || 0;
    monthlyData[i] = value;
  }
  return monthlyData;
};

// Generate point styling arrays based on journal entries
export const generatePointStyling = (
  dataLength: number,
  journalEntries: JournalEntryDto[],
  defaultColor: string,
  journalColor: string = JOURNAL_MARKER_COLOR,
) =>
  buildJournalPointStyling(
    getMonthsForLength(dataLength),
    journalEntries,
    defaultColor,
    journalColor,
  );

// Generate tooltip callback for journal entries (13-month window)
export const generateTooltipCallback = (journalEntries: JournalEntryDto[]) =>
  buildJournalTooltipCallback(getMonthsForLength(13), journalEntries);
