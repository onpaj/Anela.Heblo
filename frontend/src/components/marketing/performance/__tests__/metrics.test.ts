import {
  formatCzk, formatMetric, formatPercent, formatYoy, getMetricValue, METRIC_DESCRIPTIONS,
  PERFORMANCE_METRICS, VIEW_MODE_DESCRIPTIONS, withoutPartialMonths, withoutPartialMonthsInSeries,
} from '../metrics'
import type { MarketingYearSeriesDto, MonthlyMarketingPerformanceDto } from '../../../../api/hooks/useMarketingPerformance'

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

describe('METRIC_DESCRIPTIONS', () => {
  it('explains every selectable metric with its own distinct text', () => {
    for (const metric of PERFORMANCE_METRICS) {
      expect(METRIC_DESCRIPTIONS[metric].length).toBeGreaterThan(20)
    }

    // Distinctness is the assertion with teeth: a length check alone passes when a description is
    // copy-pasted onto the wrong metric, which is the mistake actually worth catching here.
    const texts = PERFORMANCE_METRICS.map((m) => METRIC_DESCRIPTIONS[m])
    expect(new Set(texts).size).toBe(PERFORMANCE_METRICS.length)
  })

  it('says which direction is good for the ratio metrics', () => {
    expect(METRIC_DESCRIPTIONS.pno).toMatch(/Nižší je lepší/)
    expect(METRIC_DESCRIPTIONS.roas).toMatch(/Vyšší je lepší/)
    expect(METRIC_DESCRIPTIONS.costPerOrder).toMatch(/Nižší je lepší/)
  })

  it('explains both view modes differently', () => {
    expect(VIEW_MODE_DESCRIPTIONS.trend.length).toBeGreaterThan(20)
    expect(VIEW_MODE_DESCRIPTIONS.comparison.length).toBeGreaterThan(20)
    expect(VIEW_MODE_DESCRIPTIONS.trend).not.toEqual(VIEW_MODE_DESCRIPTIONS.comparison)
  })
})

describe('withoutPartialMonths', () => {
  const partial = { ...row, month: 9, isPartial: true } as MonthlyMarketingPerformanceDto
  const complete = { ...row, month: 8, isPartial: false } as MonthlyMarketingPerformanceDto

  it('drops the running month and keeps the completed ones', () => {
    expect(withoutPartialMonths([complete, partial]).map((m) => m.month)).toEqual([8])
  })

  it('returns the same array instance when nothing is partial, so memos do not churn', () => {
    const rows = [complete]
    expect(withoutPartialMonths(rows)).toBe(rows)
  })

  it('does not mutate the input', () => {
    // Asserted by contents, not length: an implementation that replaced elements in place would
    // keep the length identical and still corrupt the caller's array.
    const rows = [complete, partial]
    withoutPartialMonths(rows)
    expect(rows).toEqual([complete, partial])
  })
})

describe('withoutPartialMonthsInSeries', () => {
  // Only the anchor year's September is partial; 2025's September is a finished month and must survive.
  const series: MarketingYearSeriesDto[] = [
    ({ year: 2026, months: [{ ...row, month: 8, isPartial: false }, { ...row, month: 9, isPartial: true }] }) as unknown as MarketingYearSeriesDto,
    ({ year: 2025, months: [{ ...row, month: 8, isPartial: false }, { ...row, month: 9, isPartial: false }] }) as unknown as MarketingYearSeriesDto,
  ]

  it('drops only the partial month, leaving the same month in earlier years intact', () => {
    const filtered = withoutPartialMonthsInSeries(series)

    expect(filtered[0].months.map((m) => m.month)).toEqual([8])
    expect(filtered[1].months.map((m) => m.month)).toEqual([8, 9])
  })

  it('does not mutate the input series', () => {
    withoutPartialMonthsInSeries(series)
    expect(series[0].months.map((m) => m.month)).toEqual([8, 9])
    expect(series[1].months.map((m) => m.month)).toEqual([8, 9])
  })

  it('returns the same array instance when no month is partial', () => {
    const clean = [series[1]]
    expect(withoutPartialMonthsInSeries(clean)).toBe(clean)
  })
})
