import { JournalEntryDto } from '../../../../api/generated/api-client'
import { getJournalDateRange, getJournalPointProps } from '../journal'

describe('getJournalDateRange', () => {
  it('returns null when there are no months', () => {
    expect(getJournalDateRange([])).toBeNull()
  })

  it('spans the first day of the earliest month to the last day of the latest, in UTC', () => {
    const range = getJournalDateRange([
      { year: 2026, month: 8 },
      { year: 2025, month: 11 },
      { year: 2026, month: 2 },
    ])

    expect(range?.dateFrom.toISOString()).toBe('2025-11-01T00:00:00.000Z')
    expect(range?.dateTo.toISOString()).toBe('2026-08-31T00:00:00.000Z')
  })

  it('handles a December end month and a leap-year February', () => {
    expect(getJournalDateRange([{ year: 2025, month: 12 }])?.dateTo.toISOString()).toBe(
      '2025-12-31T00:00:00.000Z',
    )
    expect(getJournalDateRange([{ year: 2028, month: 2 }])?.dateTo.toISOString()).toBe(
      '2028-02-29T00:00:00.000Z',
    )
  })
})

describe('getJournalPointProps', () => {
  it('maps the styling onto Chart.js point props, marking months with entries', () => {
    const entries = [new JournalEntryDto({ id: 1, entryDate: new Date(2026, 7, 3), title: 'Inventura' })]
    const props = getJournalPointProps(
      [
        { year: 2026, month: 7 },
        { year: 2026, month: 8 },
      ],
      entries,
      'rgb(59, 130, 246)',
    )

    expect(props.pointBackgroundColor).toEqual(['rgb(59, 130, 246)', '#F97316'])
    expect(props.pointBorderColor).toEqual(props.pointBackgroundColor)
    expect(props.pointRadius).toEqual([3, 6])
    expect(props.pointHoverRadius).toEqual([5, 8])
  })
})
