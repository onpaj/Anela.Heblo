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
        metrics={['balance']}
        title="test"
        axisMode="calendar"
        currentMonth={1}
      />,
    )

    expect(captured).not.toBeNull()
    expect(captured!.labels).toHaveLength(12)
    // one metric × two years => two datasets
    expect(captured!.datasets).toHaveLength(2)

    const anchor = captured!.datasets.find((d) => d.label.includes('2026'))!
    expect(anchor.data[0]).toBe(50)
    expect(anchor.data[2]).toBeNull() // month 3 missing for 2026
  })

  it('renders one dataset per metric × year', () => {
    render(
      <FinancialComparisonChart
        series={[series(2026, [1], 50), series(2025, [1], 30)]}
        metrics={['income', 'expenses']}
        title="test"
        axisMode="calendar"
        currentMonth={1}
      />,
    )

    expect(captured).not.toBeNull()
    // two metrics × two years => four datasets, reversed so bars read
    // right-to-left as Příjmy 2026, Příjmy 2025, Náklady 2026, Náklady 2025
    expect(captured!.datasets).toHaveLength(4)
    const labels = captured!.datasets.map((d) => d.label)
    expect(labels).toEqual([
      'Náklady 2025',
      'Náklady 2026',
      'Příjmy 2025',
      'Příjmy 2026',
    ])
  })
})
