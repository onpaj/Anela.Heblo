import React from 'react'
import { render } from '@testing-library/react'
import { FinancialComparisonChart } from '../FinancialComparisonChart'
import type { YearComparisonSeriesDto } from '../../../../api/hooks/useFinancialComparison'

let captured: { labels: unknown; datasets: any[] } | null = null

jest.mock('../FinancialChart', () => ({
  FinancialChart: ({ chartData }: { chartData: { labels: unknown; datasets: any[] } }) => {
    captured = chartData as { labels: unknown; datasets: any[] }
    return <div data-testid="mock-chart" />
  },
}))

const series = (year: number, months: number[], balance: number): YearComparisonSeriesDto =>
  ({
    year,
    months: months.map((m) => ({
      year,
      month: m,
      monthYearDisplay: `${String(m).padStart(2, '0')}/${year}`,
      income: 0,
      expenses: 0,
      financialBalance: balance,
    })),
    ytdIncome: 0,
    ytdExpenses: 0,
    ytdFinancialBalance: 0,
  }) as YearComparisonSeriesDto

describe('FinancialComparisonChart', () => {
  beforeEach(() => {
    captured = null
  })

  it('renders one dataset per year over a 12-month axis with null gaps', () => {
    render(
      <FinancialComparisonChart
        series={[series(2026, [1, 2], 50), series(2025, [1, 2, 3], 30)]}
        metric="balance"
        title="test"
      />,
    )

    expect(captured).not.toBeNull()
    expect(captured!.labels).toHaveLength(12)
    expect(captured!.datasets).toHaveLength(2)

    const anchor = captured!.datasets[0]
    expect(anchor.label).toBe('2026')
    expect(anchor.data[0]).toBe(50)
    expect(anchor.data[2]).toBeNull() // month 3 missing for 2026
  })
})
