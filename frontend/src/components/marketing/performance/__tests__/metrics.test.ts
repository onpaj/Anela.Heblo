import { formatCzk, formatMetric, formatPercent, formatYoy, getMetricValue, PERFORMANCE_METRICS } from '../metrics'
import type { MonthlyMarketingPerformanceDto } from '../../../../api/hooks/useMarketingPerformance'

const row = {
  year: 2026, month: 1, monthYearDisplay: '01/2026', hasData: true, isLocked: true, isPartial: false,
  orders: 2354, revenueWithVat: 2586556, revenueWithoutVat: 2137649.59, channelCosts: [], totalCost: 525082,
  pno: 24.56, roas: 407.11, profit: 1612567.59, avgOrderValue: 908.09, costPerOrder: 223.06,
  yoyCostPercent: 89.92, yoyRevenuePercent: null, yoyOrdersPercent: undefined, skippedEurInvoiceCount: 0,
} as unknown as MonthlyMarketingPerformanceDto

describe('metrics', () => {
  it('lists the seven selectable metrics in table order', () => {
    expect(PERFORMANCE_METRICS).toEqual(['totalCost', 'revenueWithoutVat', 'orders', 'pno', 'roas', 'avgOrderValue', 'costPerOrder'])
  })

  it('reads each metric from the DTO and returns null for missing ratios', () => {
    expect(getMetricValue(row, 'totalCost')).toBe(525082)
    expect(getMetricValue(row, 'pno')).toBe(24.56)
    expect(getMetricValue({ ...row, pno: null } as MonthlyMarketingPerformanceDto, 'pno')).toBeNull()
  })

  it('formats CZK without decimals using Czech locale', () => {
    expect(formatCzk(2586556).replace(/\u00A0/g, ' ')).toBe('2 586 556 Kč')
  })

  it('formats percent with one decimal and a dash for null', () => {
    expect(formatPercent(24.56).replace(/\u00A0/g, ' ')).toBe('24,6 %')
    expect(formatMetric(null, 'percent')).toBe('—')
    expect(formatMetric(2354, 'count').replace(/\u00A0/g, ' ')).toBe('2 354')
  })

  it('formats YoY as signed delta from 100 %', () => {
    expect(formatYoy(112.34).replace(/\u00A0/g, ' ')).toBe('+12,3 %')
    expect(formatYoy(96).replace(/\u00A0/g, ' ')).toBe('−4,0 %')
    expect(formatYoy(null)).toBe('—')
  })
})
