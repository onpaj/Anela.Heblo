import React from 'react'
import { render, screen } from '@testing-library/react'
import { buildComparisonChartData, PerformanceComparisonChart } from '../PerformanceComparisonChart'
import type { MarketingYearSeriesDto, MonthlyMarketingPerformanceDto } from '../../../../api/hooks/useMarketingPerformance'

jest.mock('react-chartjs-2', () => ({ Chart: () => <canvas data-testid="chart-canvas" /> }))
jest.mock('../../../../hooks/useMediaQuery', () => ({ useIsMobile: () => false }))

const cell = (year: number, month: number, orders: number | null): MonthlyMarketingPerformanceDto =>
  ({ year, month, monthYearDisplay: `${month}/${year}`, hasData: orders !== null, orders: orders ?? 0, channelCosts: [] }) as unknown as MonthlyMarketingPerformanceDto

const series = (year: number, values: (number | null)[]): MarketingYearSeriesDto =>
  ({ year, months: values.map((v, i) => cell(year, i + 1, v)), ytdOrders: 0, ytdRevenueWithoutVat: 0, ytdTotalCost: 0 }) as unknown as MarketingYearSeriesDto

describe('buildComparisonChartData', () => {
  it('produces Jan–Dec labels and one dataset per year, anchor year solid, older years faded', () => {
    const data = buildComparisonChartData(
      [series(2026, [5, 6, null, null, null, null, null, null, null, null, null, null]), series(2025, [3, 4, 7, null, null, null, null, null, null, null, null, null])],
      'orders',
    )
    expect(data.labels).toEqual(['Led', 'Úno', 'Bře', 'Dub', 'Kvě', 'Čvn', 'Čvc', 'Srp', 'Zář', 'Říj', 'Lis', 'Pro'])
    expect(data.datasets.map((d) => d.label)).toEqual(['Objednávky 2026', 'Objednávky 2025'])
    expect(data.datasets[0].data).toEqual([5, 6, null, null, null, null, null, null, null, null, null, null])
    expect(String(data.datasets[0].backgroundColor)).toMatch(/, 1\)$/)
    expect(String(data.datasets[1].backgroundColor)).toMatch(/, 0\.55\)$/)
  })
})

describe('PerformanceComparisonChart', () => {
  it('renders with a title', () => {
    render(<PerformanceComparisonChart series={[series(2026, Array(12).fill(null))]} metric="pno" currentMonth={9} anchorYear={2026} />)
    expect(screen.getByText('Meziroční srovnání — PNO')).toBeInTheDocument()
  })
})
