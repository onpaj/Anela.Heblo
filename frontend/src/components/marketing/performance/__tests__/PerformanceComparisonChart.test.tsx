import React from 'react'
import { render, screen } from '@testing-library/react'
import { buildComparisonChartData, PerformanceComparisonChart } from '../PerformanceComparisonChart'
import type { ChannelInfoDto, MarketingYearSeriesDto, MonthlyMarketingPerformanceDto } from '../../../../api/hooks/useMarketingPerformance'

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

describe('buildComparisonChartData for totalCost', () => {
  const channels = [
    { code: 'meta', label: 'FB/IG' },
    { code: 'google', label: 'Google' },
  ] as ChannelInfoDto[]

  const costCell = (year: number, month: number, meta: number | null, google: number): MonthlyMarketingPerformanceDto =>
    ({
      year,
      month,
      monthYearDisplay: `${month}/${year}`,
      hasData: meta !== null,
      channelCosts: [
        { channelCode: 'meta', costWithoutVat: meta ?? 0 },
        { channelCode: 'google', costWithoutVat: google },
      ],
    }) as unknown as MonthlyMarketingPerformanceDto

  const costSeries = (year: number, meta: number, google: number): MarketingYearSeriesDto =>
    ({ year, months: Array.from({ length: 12 }, (_, i) => costCell(year, i + 1, meta, google)) }) as unknown as MarketingYearSeriesDto

  it('gives each year its own stack so the years sit side by side', () => {
    // Arrange / Act
    const data = buildComparisonChartData([costSeries(2026, 100, 200), costSeries(2025, 10, 20)], 'totalCost', channels)

    // Assert - one dataset per year x channel, grouped by year
    expect(data.datasets.map((d) => d.label)).toEqual(['FB/IG 2026', 'Google 2026', 'FB/IG 2025', 'Google 2025'])
    expect(data.datasets.map((d) => (d as { stack: string }).stack))
      .toEqual(['year-2026', 'year-2026', 'year-2025', 'year-2025'])
    expect(data.datasets[0].data[0]).toBe(100)
    expect(data.datasets[1].data[0]).toBe(200)
    expect(data.datasets[2].data[0]).toBe(10)
  })

  it('keeps the channel hue and fades only by year, so a channel is recognisable across years', () => {
    const data = buildComparisonChartData([costSeries(2026, 1, 2), costSeries(2025, 1, 2)], 'totalCost', channels)

    // FB/IG is the same hue in both years; only the alpha differs.
    expect(String(data.datasets[0].backgroundColor)).toMatch(/^rgba\(59, 130, 246, 1\)$/)
    expect(String(data.datasets[2].backgroundColor)).toMatch(/^rgba\(59, 130, 246, 0\.55\)$/)
  })

  it('reads a month with no data as zero rather than dropping the column', () => {
    const data = buildComparisonChartData(
      [({ year: 2026, months: [costCell(2026, 1, null, 0)] }) as unknown as MarketingYearSeriesDto],
      'totalCost',
      channels,
    )
    expect(data.datasets[0].data[0]).toBe(0)
  })
})

describe('PerformanceComparisonChart', () => {
  it('renders with a title', () => {
    render(<PerformanceComparisonChart series={[series(2026, Array(12).fill(null))]} metric="pno" channels={[]} currentMonth={9} anchorYear={2026} />)
    expect(screen.getByText('Meziroční srovnání — PNO')).toBeInTheDocument()
  })

  it('titles the cost breakdown with the cost metric', () => {
    render(<PerformanceComparisonChart series={[series(2026, Array(12).fill(null))]} metric="totalCost" channels={[]} currentMonth={9} anchorYear={2026} />)
    expect(screen.getByText('Meziroční srovnání — Náklady na reklamu')).toBeInTheDocument()
  })
})
