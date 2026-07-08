import React from 'react'
import type { YearComparisonSeriesDto } from '../../../api/hooks/useFinancialComparison'
import { formatCurrency } from './utils'
import {
  getMetricValue,
  getMonthOrder,
  type ComparisonAxisMode,
  type ComparisonMetric,
} from './comparisonUtils'

interface FinancialComparisonTableProps {
  series: YearComparisonSeriesDto[]
  metric: ComparisonMetric
  axisMode: ComparisonAxisMode
  /** Current (partial) month 1..12 — anchors the rolling window's right edge. */
  currentMonth: number
}

const MONTH_NAMES_FULL = [
  'Leden', 'Únor', 'Březen', 'Duben', 'Květen', 'Červen',
  'Červenec', 'Srpen', 'Září', 'Říjen', 'Listopad', 'Prosinec',
]

const valueColor = (value: number): string =>
  value >= 0 ? 'text-emerald-600 dark:text-emerald-400' : 'text-red-600 dark:text-red-400'

export const FinancialComparisonTable: React.FC<FinancialComparisonTableProps> = ({
  series,
  metric,
  axisMode,
  currentMonth,
}) => {
  const monthOrder = getMonthOrder(axisMode, currentMonth)
  // series arrives descending by year (anchor first). Anchor = series[0], previous = series[1].
  const anchor = series[0]
  const previous = series[1]

  const cellFor = (s: YearComparisonSeriesDto | undefined, month: number) =>
    s?.months.find((m) => m.month === month)

  const anchorHasPartial = anchor?.months.some((m) => m.isPartial) ?? false
  const partialDay = anchor?.months.find((m) => m.isPartial)?.partialDayOfMonth

  return (
    <div className="overflow-auto" style={{ maxHeight: '400px' }}>
      <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
        <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0 z-10">
          <tr>
            <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
              Měsíc
            </th>
            {series.map((s) => (
              <th
                key={s.year}
                className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider"
              >
                {s.year}
              </th>
            ))}
            <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
              Δ ({anchor?.year} vs {previous?.year})
            </th>
          </tr>
        </thead>
        <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
          {monthOrder.map((month) => {
            const anchorCell = cellFor(anchor, month)
            const previousCell = cellFor(previous, month)
            const anchorValue = anchorCell ? getMetricValue(anchorCell, metric) : null
            const previousValue = previousCell ? getMetricValue(previousCell, metric) : null
            const delta =
              anchorValue !== null && previousValue !== null ? anchorValue - previousValue : null
            const isPartialRow = anchorCell?.isPartial === true

            return (
              <tr key={month} className="hover:bg-gray-50 dark:hover:bg-white/5">
                <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-graphite-text">
                  {MONTH_NAMES_FULL[month - 1]}
                  {isPartialRow && <span className="text-amber-500"> *</span>}
                </td>
                {series.map((s) => {
                  const c = cellFor(s, month)
                  const v = c ? getMetricValue(c, metric) : null
                  return (
                    <td
                      key={s.year}
                      className={`px-6 py-4 whitespace-nowrap text-sm text-right font-medium ${
                        v === null ? 'text-gray-400 dark:text-graphite-faint' : valueColor(v)
                      }`}
                    >
                      {v === null ? '—' : formatCurrency(v)}
                    </td>
                  )
                })}
                <td
                  className={`px-6 py-4 whitespace-nowrap text-sm text-right font-medium ${
                    delta === null ? 'text-gray-400 dark:text-graphite-faint' : valueColor(delta)
                  }`}
                >
                  {delta === null ? '—' : formatCurrency(delta)}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
      {anchorHasPartial && partialDay !== undefined && (
        <p className="px-6 py-3 text-xs text-gray-500 dark:text-graphite-muted">
          * částečný měsíc – data k {partialDay}. dni měsíce (stejné oříznutí pro všechny roky).
        </p>
      )}
    </div>
  )
}
