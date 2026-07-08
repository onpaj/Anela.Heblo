import React from 'react'
import { render, screen, within } from '@testing-library/react'
import { FinancialComparisonTable } from '../FinancialComparisonTable'
import type { YearComparisonSeriesDto } from '../../../../api/hooks/useFinancialComparison'

const series = (year: number, month: number, balance: number, isPartial = false): YearComparisonSeriesDto =>
  ({
    year,
    months: [
      {
        year,
        month,
        monthYearDisplay: `${String(month).padStart(2, '0')}/${year}`,
        income: 0,
        expenses: 0,
        financialBalance: balance,
        isPartial: isPartial ? true : undefined,
        partialDayOfMonth: isPartial ? 3 : undefined,
      },
    ],
    ytdIncome: 0,
    ytdExpenses: 0,
    ytdFinancialBalance: balance,
  }) as YearComparisonSeriesDto

describe('FinancialComparisonTable', () => {
  it('renders a column header per year', () => {
    render(
      <FinancialComparisonTable
        series={[series(2026, 7, 100), series(2025, 7, 80)]}
        metrics={['balance']}
        axisMode="calendar"
        currentMonth={7}
      />,
    )
    expect(screen.getByText('2026')).toBeInTheDocument()
    expect(screen.getByText('2025')).toBeInTheDocument()
    // Delta = anchor - previous = 100 - 80 = 20 => rendered as currency somewhere in the July row
    expect(screen.getByText(/červenec/i)).toBeInTheDocument()
  })

  it('lists months most-recent-first (reversed order)', () => {
    render(
      <FinancialComparisonTable
        series={[series(2026, 12, 100), series(2025, 12, 80)]}
        metrics={['balance']}
        axisMode="calendar"
        currentMonth={12}
      />,
    )
    const rows = screen.getAllByRole('row')
    // rows[0] and rows[1] are the two header rows; data rows start at rows[2].
    // First data row is the most recent month (December), last is January.
    expect(within(rows[2]).getByText('Prosinec')).toBeInTheDocument()
    expect(within(rows[13]).getByText('Leden')).toBeInTheDocument()
  })

  it('marks the partial month with an asterisk footnote', () => {
    render(
      <FinancialComparisonTable
        series={[series(2026, 7, 100, true), series(2025, 7, 80)]}
        metrics={['balance']}
        axisMode="calendar"
        currentMonth={7}
      />,
    )
    expect(screen.getByText(/částečný měsíc/i)).toBeInTheDocument()
  })
})
