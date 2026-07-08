import {
  pivotSeriesToMonthly,
  getMetricValue,
  MONTH_LABELS_SHORT,
  type ComparisonMetric,
} from '../comparisonUtils'
import type { MonthlyFinancialDataDto } from '../../../../api/hooks/useFinancialOverview'

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

  it('pivots months onto a fixed 12-slot array with null gaps', () => {
    const metric: ComparisonMetric = 'balance'
    const values = pivotSeriesToMonthly([cell(1, { financialBalance: 10 }), cell(3, { financialBalance: 30 })], metric)
    expect(values).toHaveLength(12)
    expect(values[0]).toBe(10)
    expect(values[1]).toBeNull()
    expect(values[2]).toBe(30)
    expect(values[11]).toBeNull()
  })
})
