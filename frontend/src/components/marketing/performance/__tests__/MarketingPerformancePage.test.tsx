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
jest.mock('../../../../auth/PermissionsContext', () => ({ usePermissionsContext: () => ({ hasPermission: (p: string) => p === 'marketing.performance.write' }) }))
jest.mock('../../../../telemetry/useScreenView', () => ({ useScreenView: jest.fn() }))
jest.mock('../../../../hooks/useMediaQuery', () => ({ useIsMobile: () => false }))
jest.mock('react-chartjs-2', () => ({ Chart: () => <canvas data-testid="chart-canvas" /> }))

const month = {
  year: 2026, month: 8, monthYearDisplay: '08/2026', hasData: true, isLocked: false, isPartial: false, orders: 1596,
  revenueWithVat: 1729641, revenueWithoutVat: 1429455, totalCost: 505565, pno: 35.4, roas: 282.7, profit: 923890,
  avgOrderValue: 895.6, costPerOrder: 316.8, skippedEurInvoiceCount: 1, revenueComputedAt: '2026-09-18T03:00:00Z', costsComputedAt: '2026-09-18T03:00:00Z',
  channelCosts: [{ channelCode: 'meta', label: 'FB/IG', costWithoutVat: 371564, invoiceCount: 2 }],
}

describe('MarketingPerformancePage', () => {
  beforeEach(() => {
    jest.clearAllMocks()
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

  it('shows the recompute button only with write permission and opens the dialog', () => {
    render(<MarketingPerformancePage />)
    fireEvent.click(screen.getByRole('button', { name: 'Přepočítat' }))
    expect(screen.getByRole('heading', { name: 'Přepočítat výkon reklamy' })).toBeInTheDocument()
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
