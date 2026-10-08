import type { JournalEntryDto } from "../../api/generated/api-client";

/** A calendar month slot on a chart's x-axis (month is 1-based). */
export interface YearMonth {
  year: number;
  month: number;
}

export interface JournalPointStyling {
  pointBackgroundColors: string[];
  pointRadiuses: number[];
  pointHoverRadiuses: number[];
}

interface TooltipContextItem {
  dataIndex: number;
}

export const JOURNAL_MARKER_COLOR = "#F97316";
const DEFAULT_POINT_RADIUS = 3;
const JOURNAL_POINT_RADIUS = 6;
const HOVER_RADIUS_INCREASE = 2;

/** Journal entries whose entry date falls into the given calendar month. */
export const getJournalEntriesForYearMonth = (
  journalEntries: JournalEntryDto[],
  { year, month }: YearMonth,
): JournalEntryDto[] => {
  if (!journalEntries || journalEntries.length === 0) return [];

  return journalEntries.filter((entry) => {
    if (!entry.entryDate) return false;
    const entryDate = new Date(entry.entryDate);
    return (
      entryDate.getFullYear() === year && entryDate.getMonth() + 1 === month
    );
  });
};

/** Point styling arrays that highlight months carrying journal entries. */
export const buildJournalPointStyling = (
  months: YearMonth[],
  journalEntries: JournalEntryDto[],
  defaultColor: string,
  journalColor: string = JOURNAL_MARKER_COLOR,
): JournalPointStyling => {
  const hasEntries = months.map(
    (yearMonth) =>
      getJournalEntriesForYearMonth(journalEntries, yearMonth).length > 0,
  );
  const pointRadiuses = hasEntries.map((has) =>
    has ? JOURNAL_POINT_RADIUS : DEFAULT_POINT_RADIUS,
  );

  return {
    pointBackgroundColors: hasEntries.map((has) =>
      has ? journalColor : defaultColor,
    ),
    pointRadiuses,
    pointHoverRadiuses: pointRadiuses.map((r) => r + HOVER_RADIUS_INCREASE),
  };
};

/** Tooltip lines listing the given entries ("Záznamy deníku:" block). */
export const formatJournalTooltipLines = (
  entries: JournalEntryDto[],
): string[] => {
  if (entries.length === 0) return [];

  return [
    "",
    "Záznamy deníku:",
    ...entries.map((entry) => {
      const date = entry.entryDate
        ? new Date(entry.entryDate).toLocaleDateString("cs-CZ", {
            day: "2-digit",
            month: "2-digit",
          })
        : "";
      return `• ${date}: ${entry.title || "Bez názvu"}`;
    }),
  ];
};

/** Chart.js tooltip callbacks appending the journal entries of the hovered month. */
export const buildJournalTooltipCallback = (
  months: YearMonth[],
  journalEntries: JournalEntryDto[],
) => ({
  afterBody: (context: TooltipContextItem[]): string[] => {
    if (context.length === 0) return [];
    const yearMonth = months[context[0].dataIndex];
    if (!yearMonth) return [];
    return formatJournalTooltipLines(
      getJournalEntriesForYearMonth(journalEntries, yearMonth),
    );
  },
});
