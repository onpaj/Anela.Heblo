import React from 'react'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useMarketingPerformanceMonthsQuery, useRecomputeMarketingPerformanceMutation } from '../useMarketingPerformance'

const mockGetMonths = jest.fn()
const mockRecompute = jest.fn()
jest.mock('../../client', () => ({
  getAuthenticatedApiClient: () => ({
    marketingPerformance_GetMonths: (...args: unknown[]) => mockGetMonths(...args),
    marketingPerformance_Recompute: (...args: unknown[]) => mockRecompute(...args),
  }),
  QUERY_KEYS: { marketingPerformanceMonths: ['marketing-performance', 'months'], marketingPerformanceComparison: ['marketing-performance', 'comparison'] },
}))

const wrapper = ({ children }: { children: React.ReactNode }) => (
  <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>{children}</QueryClientProvider>
)

describe('useMarketingPerformance hooks', () => {
  beforeEach(() => jest.clearAllMocks())

  it('passes from/to/includeWholesale to the generated client in order', async () => {
    mockGetMonths.mockResolvedValue({ success: true, months: [], channels: [] })

    const { result } = renderHook(() => useMarketingPerformanceMonthsQuery({ from: '2026-01', to: '2026-09', includeWholesale: true }), { wrapper })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(mockGetMonths).toHaveBeenCalledWith('2026-01', '2026-09', true)
  })

  it('recompute mutation posts the range body', async () => {
    mockRecompute.mockResolvedValue({ success: true, jobId: 'hf-1', monthCount: 2 })

    const { result } = renderHook(() => useRecomputeMarketingPerformanceMutation(), { wrapper })
    result.current.mutate({ from: '2026-01', to: '2026-02' })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(mockRecompute).toHaveBeenCalledWith(expect.objectContaining({ from: '2026-01', to: '2026-02' }))
  })
})
