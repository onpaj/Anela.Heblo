import React from 'react'
import { render, screen } from '@testing-library/react'
import { buildTrendChartData, PerformanceTrendChart } from '../PerformanceTrendChart'
import type { ChannelInfoDto, MonthlyMarketingPerformanceDto } from '../../../../api/hooks/useMarketingPerformance'

jest.mock('react-chartjs-2', () => ({ Chart: () => <canvas data-testid="chart-canvas" /> }))
jest.mock('../../../../hooks/useMediaQuery', () => ({ useIsMobile: () => false }))

const channels = [{ code: 'meta', label: 'FB/IG' }, { code: 'google', label: 'Google' }] as ChannelInfoDto[]
const m = (month: number, hasData = true): MonthlyMarketingPerformanceDto =>
  ({
    year: 2026, month, monthYearDisplay: `0${month}/2026`, hasData, isLocked: false, isPartial: false,
    orders: 10 * month, revenueWithVat: 0, revenueWithoutVat: 1000 * month, totalCost: 100 * month, pno: month, roas: null,
    profit: 0, avgOrderValue: null, costPerOrder: null, skippedEurInvoiceCount: 0,
    channelCosts: [
      { channelCode: 'meta', label: 'FB/IG', costWithoutVat: 60 * month, invoiceCount: 1 },
      { channelCode: 'google', label: 'Google', costWithoutVat: 40 * month, invoiceCount: 1 },
    ],
  }) as unknown as MonthlyMarketingPerformanceDto

describe('buildTrendChartData', () => {
  it('stacks one bar dataset per channel and adds the selected metric as a line on the second axis', () => {
    const data = buildTrendChartData([m(1), m(2)], channels, 'pno')

    expect(data.labels).toEqual(['01/2026', '02/2026'])
    expect(data.datasets.map((d) => d.label)).toEqual(['FB/IG', 'Google', 'PNO'])
    expect(data.datasets[0]).toMatchObject({ type: 'bar', stack: 'costs', yAxisID: 'y', data: [60, 120] })
    expect(data.datasets[2]).toMatchObject({ type: 'line', yAxisID: 'y1', data: [1, 2] })
  })

  it('renders null for months without data so the line breaks instead of dropping to zero', () => {
    const data = buildTrendChartData([m(1), m(2, false)], channels, 'orders')
    expect(data.datasets[2].data).toEqual([10, null])
  })

  it('does not duplicate cost when the selected metric is totalCost', () => {
    const data = buildTrendChartData([m(1)], channels, 'totalCost')
    expect(data.datasets.map((d) => d.label)).toEqual(['FB/IG', 'Google'])
  })

  it('leaves the cost bars null for a month with no data instead of reporting zero spend', () => {
    // Arrange - February has not been computed yet.
    const rows = [m(1), m(2, false)]

    // Act
    const data = buildTrendChartData(rows, channels, 'totalCost')

    // Assert - null reads as "—" in the tooltip; 0 would claim we spent nothing that month.
    expect(data.datasets[0].data).toEqual([60, null])
    expect(data.datasets[1].data).toEqual([40, null])
  })

  it('appends a column for a stored channel that is no longer configured', () => {
    // Arrange - sklik was dropped from config but its invoices are still stored.
    const withOrphan = {
      ...m(1),
      totalCost: 130,
      channelCosts: [...m(1).channelCosts, { channelCode: 'sklik', label: 'S-Klik', costWithoutVat: 30, invoiceCount: 1 }],
    } as unknown as MonthlyMarketingPerformanceDto

    // Act
    const data = buildTrendChartData([withOrphan], channels, 'totalCost')

    // Assert - without the orphan the stack would read 100 against a "Náklady celkem" of 130.
    expect(data.datasets.map((d) => d.label)).toEqual(['FB/IG', 'Google', 'S-Klik'])
    const stacked = data.datasets.reduce((sum, d) => sum + Number(d.data[0] ?? 0), 0)
    expect(stacked).toBe(withOrphan.totalCost)
  })

  it('treats an orphan channel stored under different casing as one channel', () => {
    // Arrange - the backend emits each month's first-seen casing for an unconfigured code.
    const jan = { ...m(1), channelCosts: [{ channelCode: 'Sklik', label: 'S-Klik', costWithoutVat: 30, invoiceCount: 1 }] } as unknown as MonthlyMarketingPerformanceDto
    const feb = { ...m(2), channelCosts: [{ channelCode: 'sklik', label: 'S-Klik', costWithoutVat: 50, invoiceCount: 1 }] } as unknown as MonthlyMarketingPerformanceDto

    // Act
    const data = buildTrendChartData([jan, feb], channels, 'totalCost')

    // Assert - one legend entry carrying both months, not two half-empty ones.
    expect(data.datasets.map((d) => d.label)).toEqual(['FB/IG', 'Google', 'S-Klik'])
    expect(data.datasets[2].data).toEqual([30, 50])
  })
})

describe('PerformanceTrendChart', () => {
  it('renders with a title', () => {
    render(<PerformanceTrendChart months={[m(1)]} channels={channels} metric="pno" />)
    expect(screen.getByText('Vývoj — PNO')).toBeInTheDocument()
    expect(screen.getByTestId('chart-canvas')).toBeInTheDocument()
  })
})
