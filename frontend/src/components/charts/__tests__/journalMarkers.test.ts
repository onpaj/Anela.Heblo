import { JournalEntryDto } from "../../../api/generated/api-client";
import {
  JOURNAL_MARKER_COLOR,
  buildJournalPointStyling,
  buildJournalTooltipCallback,
  formatJournalTooltipLines,
  getJournalEntriesForYearMonth,
} from "../journalMarkers";

const entry = (id: number, date: string, title?: string): JournalEntryDto =>
  new JournalEntryDto({ id, entryDate: new Date(date), title });

const STOCKTAKE = entry(1, "2026-08-03T00:00:00", "Inventura etiket");
const SEPTEMBER = entry(2, "2026-09-15T00:00:00", "Září");
const UNTITLED = entry(3, "2026-08-20T00:00:00");
const ENTRIES = [STOCKTAKE, SEPTEMBER, UNTITLED];

const MONTHS = [
  { year: 2026, month: 7 },
  { year: 2026, month: 8 },
  { year: 2026, month: 9 },
];

describe("getJournalEntriesForYearMonth", () => {
  it("returns entries dated in the given calendar month", () => {
    expect(
      getJournalEntriesForYearMonth(ENTRIES, { year: 2026, month: 8 }),
    ).toEqual([STOCKTAKE, UNTITLED]);
  });

  it("does not match the same month of another year", () => {
    expect(
      getJournalEntriesForYearMonth(ENTRIES, { year: 2025, month: 8 }),
    ).toEqual([]);
  });

  it("skips entries without a date", () => {
    const undated = new JournalEntryDto({ id: 9, title: "No date" });
    expect(
      getJournalEntriesForYearMonth([undated], { year: 2026, month: 8 }),
    ).toEqual([]);
  });
});

describe("buildJournalPointStyling", () => {
  it("enlarges and recolors only months that carry entries", () => {
    const styling = buildJournalPointStyling(MONTHS, ENTRIES, "blue");

    expect(styling.pointBackgroundColors).toEqual([
      "blue",
      JOURNAL_MARKER_COLOR,
      JOURNAL_MARKER_COLOR,
    ]);
    expect(styling.pointRadiuses).toEqual([3, 6, 6]);
    expect(styling.pointHoverRadiuses).toEqual([5, 8, 8]);
  });

  it("uses default styling everywhere when there are no entries", () => {
    const styling = buildJournalPointStyling(MONTHS, [], "blue");

    expect(styling.pointBackgroundColors).toEqual(["blue", "blue", "blue"]);
    expect(styling.pointRadiuses).toEqual([3, 3, 3]);
  });
});

describe("formatJournalTooltipLines", () => {
  it("returns nothing for no entries", () => {
    expect(formatJournalTooltipLines([])).toEqual([]);
  });

  it("lists entries with date and a fallback title", () => {
    expect(formatJournalTooltipLines([STOCKTAKE, UNTITLED])).toEqual([
      "",
      "Záznamy deníku:",
      `• ${new Date(2026, 7, 3).toLocaleDateString("cs-CZ", { day: "2-digit", month: "2-digit" })}: Inventura etiket`,
      `• ${new Date(2026, 7, 20).toLocaleDateString("cs-CZ", { day: "2-digit", month: "2-digit" })}: Bez názvu`,
    ]);
  });
});

describe("buildJournalTooltipCallback", () => {
  const { afterBody } = buildJournalTooltipCallback(MONTHS, ENTRIES);

  it("appends the hovered month's entries", () => {
    const lines = afterBody([{ dataIndex: 2 }]);
    expect(lines.slice(0, 2)).toEqual(["", "Záznamy deníku:"]);
    expect(lines[2]).toContain("Září");
  });

  it("returns nothing for a month without entries", () => {
    expect(afterBody([{ dataIndex: 0 }])).toEqual([]);
  });

  it("returns nothing for an empty context or an index outside the axis", () => {
    expect(afterBody([])).toEqual([]);
    expect(afterBody([{ dataIndex: 7 }])).toEqual([]);
  });
});
