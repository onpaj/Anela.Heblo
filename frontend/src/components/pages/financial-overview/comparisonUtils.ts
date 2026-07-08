import type { MonthlyFinancialDataDto } from '../../../api/hooks/useFinancialOverview'
import type { YearComparisonSeriesDto } from '../../../api/hooks/useFinancialComparison'

export type ComparisonMetric = 'income' | 'expenses' | 'balance' | 'totalBalance'

export const MONTH_LABELS_SHORT = [
  'Led', 'Úno', 'Bře', 'Dub', 'Kvě', 'Čvn',
  'Čvc', 'Srp', 'Zář', 'Říj', 'Lis', 'Pro',
] as const

export const COMPARISON_METRIC_LABELS: Record<ComparisonMetric, string> = {
  income: 'Příjmy',
  expenses: 'Náklady',
  balance: 'Účetní bilance',
  totalBalance: 'Celková bilance (vč. skladu)',
}

// Distinct bar colors per year slot (anchor year first). Extend if N grows beyond 3.
export const YEAR_SERIES_COLORS = [
  'rgb(239, 68, 68)', // red-500    - anchor year
  'rgb(249, 115, 22)', // orange-500 - previous year
  'rgb(59, 130, 246)', // blue-500   - two years ago
] as const

export const getMetricValue = (cell: MonthlyFinancialDataDto, metric: ComparisonMetric): number => {
  switch (metric) {
    case 'income':
      return cell.income
    case 'expenses':
      return cell.expenses
    case 'balance':
      return cell.financialBalance
    case 'totalBalance':
      return cell.totalBalance ?? cell.financialBalance
    default: {
      const _exhaustive: never = metric
      throw new Error(`Unhandled metric: ${_exhaustive}`)
    }
  }
}

export const getYtdForMetric = (
  series: YearComparisonSeriesDto,
  metric: ComparisonMetric,
): number => {
  switch (metric) {
    case 'income':
      return series.ytdIncome
    case 'expenses':
      return series.ytdExpenses
    case 'balance':
      return series.ytdFinancialBalance
    case 'totalBalance':
      return series.ytdTotalBalance ?? series.ytdFinancialBalance
    default: {
      const _exhaustive: never = metric
      throw new Error(`Unhandled metric: ${_exhaustive}`)
    }
  }
}

export type ComparisonAxisMode = 'calendar' | 'rolling'

/**
 * The ordered calendar month numbers (1..12) that make up the x-axis.
 * - 'calendar': [1..12] — fixed January→December.
 * - 'rolling':  the 12 months ending at `currentMonth` inclusive, current month last
 *               (e.g. currentMonth = 5 → [6,7,8,9,10,11,12,1,2,3,4,5], so May is on the right).
 * Year series stay aligned by calendar month regardless of order.
 */
export const getMonthOrder = (
  mode: ComparisonAxisMode,
  currentMonth: number,
): number[] => {
  if (mode === 'calendar') {
    return Array.from({ length: 12 }, (_, i) => i + 1)
  }
  return Array.from({ length: 12 }, (_, i) => ((currentMonth + i) % 12) + 1)
}

/** Short Czech labels for a given month order. */
export const getMonthLabels = (order: number[]): string[] =>
  order.map((month) => MONTH_LABELS_SHORT[month - 1])

/**
 * Projects a year's month cells onto the given month order.
 * Missing months become null so a bar is absent (rather than drawn as zero).
 */
export const projectSeriesOntoOrder = (
  months: MonthlyFinancialDataDto[],
  metric: ComparisonMetric,
  order: number[],
): (number | null)[] => {
  const valueByMonth = new Map<number, number>()
  for (const cell of months) {
    if (cell.month >= 1 && cell.month <= 12) {
      valueByMonth.set(cell.month, getMetricValue(cell, metric))
    }
  }
  return order.map((month) => valueByMonth.get(month) ?? null)
}
