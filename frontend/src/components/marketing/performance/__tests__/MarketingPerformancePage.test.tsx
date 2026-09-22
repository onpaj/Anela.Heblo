import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import MarketingPerformancePage from '../MarketingPerformancePage'

const mockMonthsQuery = jest.fn()
const mockComparisonQuery = jest.fn()
jest.mock('../../../../api/hooks/useMarketingPerformance', () => ({
  useMarketingPerformanceMonthsQuery: (...args: unknown[]) => mockMonthsQuery(...args),
  useMarketingPerformanceComparisonQuery: (...args: unknown[]) => mockComparisonQuery(...args),
  useRecomputeMarketingPerformanceMutation: () => ({ mutate: jest.fn(), isPending: false, reset: jest.fn() }),
}))
let mockCanWrite = true
jest.mock('../../../../auth/PermissionsContext', () => ({
  usePermissionsContext: () => ({ hasPermission: (p: string) => mockCanWrite && p === 'marketing.performance.write' }),
}))
jest.mock('../../../../telemetry/useScreenView', () => ({ useScreenView: jest.fn() }))
jest.mock('../../../../hooks/useMediaQuery', () => ({ useIsMobile: () => false }))
const mockChart: { data?: { labels?: unknown[] } } = {}
jest.mock('react-chartjs-2', () => ({
  Chart: (props: { data?: { labels?: unknown[] } }) => {
    mockChart.data = props.data
    return <canvas data-testid="chart-canvas" />
  },
}))

const month = {
  year: 2026, month: 8, monthYearDisplay: '08/2026', hasData: true, isLocked: false, isPartial: false, orders: 1596,
  revenueWithVat: 1729641, revenueWithoutVat: 1429455, totalCost: 505565, pno: 35.4, roas: 282.7, profit: 923890,
  avgOrderValue: 895.6, costPerOrder: 316.8, skippedEurInvoiceCount: 1, revenueComputedAt: '2026-09-18T03:00:00Z', costsComputedAt: '2026-09-18T03:00:00Z',
  channelCosts: [{ channelCode: 'meta', label: 'FB/IG', costWithoutVat: 371564, invoiceCount: 2 }],
}

describe('MarketingPerformancePage', () => {
  beforeEach(() => {
    jest.clearAllMocks()
    mockCanWrite = true
    mockMonthsQuery.mockReturnValue({ data: { success: true, months: [month], channels: [{ code: 'meta', label: 'FB/IG' }], lastRefreshAt: '2026-09-18T03:00:00Z' }, isLoading: false, error: null, isRefetching: false })
    mockComparisonQuery.mockReturnValue({ data: undefined, isLoading: false, error: null })
  })

  it('renders heading, table and trend chart by default', () => {
    render(<MarketingPerformancePage />)
    expect(screen.getByRole('heading', { name: 'Analýzy' })).toBeInTheDocument()
    expect(screen.getByTestId('performance-table')).toBeInTheDocument()
    expect(screen.getByText(/Vývoj — /)).toBeInTheDocument()
  })

  it('wholesale switch re-queries with includeWholesale=true', () => {
    render(<MarketingPerformancePage />)
    fireEvent.click(screen.getByLabelText('včetně velkoobchodu'))
    expect(mockMonthsQuery).toHaveBeenLastCalledWith(expect.objectContaining({ includeWholesale: true }), true)
  })

  it('switching to comparison view enables the comparison query and disables the months query', () => {
    mockComparisonQuery.mockReturnValue({ data: { success: true, series: [], anchorYear: 2026, currentMonth: 9, channels: [] }, isLoading: false, error: null })
    render(<MarketingPerformancePage />)
    fireEvent.change(screen.getByLabelText('Zobrazení'), { target: { value: 'comparison' } })
    expect(mockComparisonQuery).toHaveBeenLastCalledWith(expect.objectContaining({ years: 3 }), true)
    expect(mockMonthsQuery).toHaveBeenLastCalledWith(expect.anything(), false)
  })

  it('opens the recompute dialog when the user has write permission', () => {
    render(<MarketingPerformancePage />)
    fireEvent.click(screen.getByRole('button', { name: 'Přepočítat' }))
    expect(screen.getByRole('heading', { name: 'Přepočítat výkon reklamy' })).toBeInTheDocument()
  })

  it('hides the recompute button without write permission', () => {
    // Arrange
    mockCanWrite = false

    // Act
    render(<MarketingPerformancePage />)

    // Assert
    expect(screen.queryByRole('button', { name: 'Přepočítat' })).not.toBeInTheDocument()
  })

  it('localizes a thrown API error DTO instead of showing "Neznámá chyba"', () => {
    // Arrange - the generated client throws the parsed response DTO, not an Error,
    // so the DTO carries errorCode and has no .message at all.
    mockMonthsQuery.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: { success: false, errorCode: 'MarketingPerformanceInvalidMonthRange', params: {} },
      isRefetching: false,
    })

    // Act
    render(<MarketingPerformancePage />)

    // Assert
    expect(screen.getByText(/Neplatné období: zadejte měsíce ve formátu RRRR-MM/)).toBeInTheDocument()
    expect(screen.queryByText('Neznámá chyba')).not.toBeInTheDocument()
  })

  it('shows the last refresh time in the status line', () => {
    render(<MarketingPerformancePage />)
    expect(screen.getByText(/Poslední aktualizace:/)).toBeInTheDocument()
  })

  it('shows the stale-data warning in comparison view too', () => {
    const staleMonth = {
      ...month,
      lastError: null,
      revenueComputedAt: '2026-09-18T03:00:00Z',
      costsComputedAt: null,
    }
    mockComparisonQuery.mockReturnValue({
      data: { success: true, series: [{ year: 2026, months: [staleMonth], ytdOrders: 0, ytdRevenueWithoutVat: 0, ytdTotalCost: 0 }], anchorYear: 2026, currentMonth: 9, channels: [] },
      isLoading: false,
      error: null,
    })
    render(<MarketingPerformancePage />)
    fireEvent.change(screen.getByLabelText('Zobrazení'), { target: { value: 'comparison' } })
    expect(screen.getByText('některé měsíce mají neúplná data')).toBeInTheDocument()
  })
})

describe('MarketingPerformancePage — hiding the running month', () => {
  const finished = { ...month, year: 2026, month: 8, monthYearDisplay: '08/2026', isPartial: false }
  // The running month: a credit note and almost no revenue yet, which is what wrecks the ratio metrics.
  const running = { ...month, year: 2026, month: 9, monthYearDisplay: '09/2026', isPartial: true, totalCost: -7350, roas: -14610.7 }

  beforeEach(() => {
    jest.clearAllMocks()
    mockCanWrite = true
    mockChart.data = undefined
    mockMonthsQuery.mockReturnValue({
      data: { success: true, months: [finished, running], channels: [{ code: 'meta', label: 'FB/IG' }], lastRefreshAt: '2026-09-22T10:12:00Z' },
      isLoading: false, error: null, isRefetching: false,
    })
    mockComparisonQuery.mockReturnValue({ data: undefined, isLoading: false, error: null })
  })

  it('keeps the running month out of the chart by default', () => {
    render(<MarketingPerformancePage />)

    expect(screen.getByLabelText('skrýt probíhající měsíc')).toBeChecked()
    expect(mockChart.data?.labels).toEqual(['08/2026'])
  })

  it('still lists the running month in the table, where the "(probíhá)" tag explains it', () => {
    render(<MarketingPerformancePage />)

    expect(screen.getByTestId('performance-table')).toHaveTextContent('09/2026')
  })

  it('puts the running month back in the chart when the checkbox is unticked', () => {
    // Arrange
    render(<MarketingPerformancePage />)
    expect(mockChart.data?.labels).toEqual(['08/2026'])

    // Act
    fireEvent.click(screen.getByLabelText('skrýt probíhající měsíc'))

    // Assert
    expect(mockChart.data?.labels).toEqual(['08/2026', '09/2026'])
  })

  it('drops only the anchor year\'s running month in the comparison view', () => {
    // Arrange - September exists in both years but is only partial in 2026.
    mockComparisonQuery.mockReturnValue({
      data: {
        success: true, anchorYear: 2026, currentMonth: 9, channels: [{ code: 'meta', label: 'FB/IG' }],
        series: [
          { year: 2026, months: [finished, running] },
          { year: 2025, months: [{ ...month, year: 2025, month: 8, isPartial: false }, { ...month, year: 2025, month: 9, isPartial: false }] },
        ],
      },
      isLoading: false, error: null,
    })
    render(<MarketingPerformancePage />)

    // Act
    fireEvent.change(screen.getByLabelText('Zobrazení'), { target: { value: 'comparison' } })

    // Assert - 2026 loses September, 2025 keeps it. The comparison chart always draws all 12 slots,
    // so the evidence is in the per-dataset values, not the labels.
    const datasets = (mockChart.data as unknown as { datasets: { label: string; data: (number | null)[] }[] }).datasets
    const sep = 8 // zero-based index of September
    // Asserted by value, not `not.toBeNull()`: optional chaining on a missing dataset yields
    // `undefined`, which would satisfy `not.toBeNull()` even if the 2025 series vanished entirely.
    expect(datasets.find((d) => d.label.endsWith('2026'))?.data[sep]).toBeNull()
    expect(datasets.find((d) => d.label.endsWith('2025'))?.data[sep]).toBe(35.4)
  })
})
