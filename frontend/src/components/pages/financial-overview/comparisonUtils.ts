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

// Distinct line colors per year slot (anchor year first). Extend if N grows beyond 3.
export const YEAR_SERIES_COLORS = [
  'rgb(59, 130, 246)', // blue-500  - anchor year
  'rgb(168, 85, 247)', // purple-500 - previous year
  'rgb(245, 158, 11)', // amber-500 - two years ago
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

/**
 * Projects a year's month cells onto a fixed 12-element array (index 0 = January).
 * Missing months become null so a chart line stops rather than dropping to zero.
 */
export const pivotSeriesToMonthly = (
  months: MonthlyFinancialDataDto[],
  metric: ComparisonMetric,
): (number | null)[] => {
  const values: (number | null)[] = Array(12).fill(null)
  for (const cell of months) {
    if (cell.month >= 1 && cell.month <= 12) {
      values[cell.month - 1] = getMetricValue(cell, metric)
    }
  }
  return values
}
