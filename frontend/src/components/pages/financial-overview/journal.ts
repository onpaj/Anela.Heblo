import type { JournalEntryDto } from '../../../api/generated/api-client'
import { useSearchJournalEntries } from '../../../api/hooks/useJournal'
import { buildJournalPointStyling, type YearMonth } from '../../charts/journalMarkers'

// Company-wide entries are rare; one page comfortably covers the longest period (26 months).
const JOURNAL_PAGE_SIZE = 200

export interface JournalDateRange {
  dateFrom: Date
  dateTo: Date
}

/**
 * Inclusive range from the first day of the earliest month to the last day of the latest one.
 * Built in UTC because the API client serializes dates with toISOString(); a local midnight
 * would shift a day back in Prague and drop entries on the range's last day.
 */
export const getJournalDateRange = (months: YearMonth[]): JournalDateRange | null => {
  if (months.length === 0) return null
  const keys = months.map((m) => m.year * 12 + (m.month - 1))
  const first = Math.min(...keys)
  const last = Math.max(...keys)
  return {
    dateFrom: new Date(Date.UTC(Math.floor(first / 12), first % 12, 1)),
    dateTo: new Date(Date.UTC(Math.floor(last / 12), (last % 12) + 1, 0)),
  }
}

/** Point props for a line dataset, highlighting months with journal entries. */
export const getJournalPointProps = (
  months: YearMonth[],
  journalEntries: JournalEntryDto[],
  lineColor: string,
) => {
  const styling = buildJournalPointStyling(months, journalEntries, lineColor)
  return {
    pointBackgroundColor: styling.pointBackgroundColors,
    pointBorderColor: styling.pointBackgroundColors,
    pointRadius: styling.pointRadiuses,
    pointHoverRadius: styling.pointHoverRadiuses,
  }
}

/** Company-wide journal entries (no product association) within the charted months. */
export const useFinancialJournalEntries = (months: YearMonth[], enabled: boolean) => {
  const range = getJournalDateRange(months)
  return useSearchJournalEntries(
    {
      dateFrom: range?.dateFrom,
      dateTo: range?.dateTo,
      withoutProducts: true,
      pageNumber: 1,
      pageSize: JOURNAL_PAGE_SIZE,
      sortBy: 'EntryDate',
      sortDirection: 'DESC',
    },
    enabled && range !== null,
  )
}
