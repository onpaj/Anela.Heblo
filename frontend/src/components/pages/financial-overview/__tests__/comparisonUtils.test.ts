import {
  getMonthOrder,
  getMonthLabels,
  projectSeriesOntoOrder,
  getMetricValue,
  getYtdForMetric,
  getSeriesColor,
  orderMetrics,
  MONTH_LABELS_SHORT,
  type ComparisonMetric,
} from '../comparisonUtils'
import type { MonthlyFinancialDataDto } from '../../../../api/hooks/useFinancialOverview'
import type { YearComparisonSeriesDto } from '../../../../api/hooks/useFinancialComparison'

const cell = (month: number, over: Partial<MonthlyFinancialDataDto> = {}): MonthlyFinancialDataDto =>
  ({
    year: 2026,
    month,
    monthYearDisplay: `${String(month).padStart(2, '0')}/2026`,
    income: 0,
    expenses: 0,
    financialBalance: 0,
    ...over,
  }) as MonthlyFinancialDataDto

describe('comparisonUtils', () => {
  it('has 12 Czech short month labels', () => {
    expect(MONTH_LABELS_SHORT).toHaveLength(12)
    expect(MONTH_LABELS_SHORT[0]).toBe('Led')
    expect(MONTH_LABELS_SHORT[11]).toBe('Pro')
  })

  it('reads the requested metric from a cell', () => {
    const c = cell(7, { income: 100, expenses: 40, financialBalance: 60, totalBalance: 75 })
    expect(getMetricValue(c, 'income')).toBe(100)
    expect(getMetricValue(c, 'expenses')).toBe(40)
    expect(getMetricValue(c, 'balance')).toBe(60)
    expect(getMetricValue(c, 'totalBalance')).toBe(75)
  })

  it('falls back to financialBalance when totalBalance is absent', () => {
    const c = cell(7, { financialBalance: 60 })
    expect(getMetricValue(c, 'totalBalance')).toBe(60)
  })

  it('calendar month order is January..December', () => {
    expect(getMonthOrder('calendar', 5)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12])
  })

  it('rolling month order is the 12 months ending at the current month', () => {
    // current month = May (5) => Jun..Dec, Jan..May, with May on the right
    expect(getMonthOrder('rolling', 5)).toEqual([6, 7, 8, 9, 10, 11, 12, 1, 2, 3, 4, 5])
  })

  it('labels follow the given month order', () => {
    const labels = getMonthLabels(getMonthOrder('rolling', 5))
    expect(labels).toHaveLength(12)
    expect(labels[0]).toBe('Čvn') // June, 12 months back
    expect(labels[11]).toBe('Kvě') // May, current month on the right
  })

  it('projects months onto a calendar order with null gaps', () => {
    const metric: ComparisonMetric = 'balance'
    const order = getMonthOrder('calendar', 1)
    const values = projectSeriesOntoOrder(
      [cell(1, { financialBalance: 10 }), cell(3, { financialBalance: 30 })],
      metric,
      order,
    )
    expect(values).toHaveLength(12)
    expect(values[0]).toBe(10)
    expect(values[1]).toBeNull()
    expect(values[2]).toBe(30)
    expect(values[11]).toBeNull()
  })

  it('projects months onto a rolling order (current month rightmost)', () => {
    const metric: ComparisonMetric = 'balance'
    const order = getMonthOrder('rolling', 5)
    const values = projectSeriesOntoOrder(
      [cell(5, { financialBalance: 50 }), cell(6, { financialBalance: 60 })],
      metric,
      order,
    )
    // order = [6,7,8,9,10,11,12,1,2,3,4,5]
    expect(values[0]).toBe(60) // June is first slot
    expect(values[11]).toBe(50) // May (current month) is last slot
    expect(values[1]).toBeNull() // July has no data
  })

  it('colors by metric with the anchor year solid and older years lighter', () => {
    // same metric → same hue, decreasing opacity by year slot
    expect(getSeriesColor('income', 0)).toBe('rgba(34, 197, 94, 1)')
    expect(getSeriesColor('income', 1)).toBe('rgba(34, 197, 94, 0.55)')
    expect(getSeriesColor('income', 2)).toBe('rgba(34, 197, 94, 0.3)')
    // different metric → different hue
    expect(getSeriesColor('expenses', 0)).toBe('rgba(239, 68, 68, 1)')
  })

  it('orders selected metrics canonically regardless of input order', () => {
    expect(orderMetrics(['balance', 'income'])).toEqual(['income', 'balance'])
    expect(orderMetrics(['totalBalance', 'expenses', 'income'])).toEqual([
      'income',
      'expenses',
      'totalBalance',
    ])
  })

  describe('getYtdForMetric', () => {
    const series = {
      year: 2026,
      months: [],
      ytdIncome: 1000,
      ytdExpenses: 400,
      ytdFinancialBalance: 600,
      ytdTotalBalance: 750,
    } as YearComparisonSeriesDto

    it('reads ytdIncome for income metric', () => {
      expect(getYtdForMetric(series, 'income')).toBe(1000)
    })

    it('reads ytdExpenses for expenses metric', () => {
      expect(getYtdForMetric(series, 'expenses')).toBe(400)
    })

    it('reads ytdFinancialBalance for balance metric', () => {
      expect(getYtdForMetric(series, 'balance')).toBe(600)
    })

    it('reads ytdTotalBalance for totalBalance metric', () => {
      expect(getYtdForMetric(series, 'totalBalance')).toBe(750)
    })

    it('falls back to ytdFinancialBalance when ytdTotalBalance is undefined', () => {
      const seriesWithoutTotal = {
        ...series,
        ytdTotalBalance: undefined,
      } as YearComparisonSeriesDto
      expect(getYtdForMetric(seriesWithoutTotal, 'totalBalance')).toBe(600)
    })
  })
})
