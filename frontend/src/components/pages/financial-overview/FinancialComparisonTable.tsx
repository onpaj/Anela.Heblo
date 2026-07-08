import React from 'react'
import type { YearComparisonSeriesDto } from '../../../api/hooks/useFinancialComparison'
import { formatCurrency } from './utils'
import {
  COMPARISON_METRIC_LABELS,
  getMetricValue,
  getMonthOrder,
  orderMetrics,
  type ComparisonAxisMode,
  type ComparisonMetric,
} from './comparisonUtils'

interface FinancialComparisonTableProps {
  series: YearComparisonSeriesDto[]
  metrics: ComparisonMetric[]
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

const GROUP_BORDER = 'border-l border-gray-200 dark:border-graphite-border'

export const FinancialComparisonTable: React.FC<FinancialComparisonTableProps> = ({
  series,
  metrics,
  axisMode,
  currentMonth,
}) => {
  // Table lists months most-recent-first (top), reversed from the chart's chronological axis.
  const monthOrder = [...getMonthOrder(axisMode, currentMonth)].reverse()
  // Columns run right-to-left ending at the current year: metric groups and the
  // years within them are reversed, so the current year's first metric is rightmost.
  const displayMetrics = [...orderMetrics(metrics)].reverse()
  const displayYears = [...series].reverse()
  // Delta is always current vs previous year, independent of display order.
  const anchor = series[0]
  const previous = series[1]
  const hasDelta = series.length >= 2

  const cellFor = (s: YearComparisonSeriesDto | undefined, month: number) =>
    s?.months.find((m) => m.month === month)

  const valueFor = (s: YearComparisonSeriesDto | undefined, month: number, metric: ComparisonMetric) => {
    const c = cellFor(s, month)
    return c ? getMetricValue(c, metric) : null
  }

  const anchorHasPartial = anchor?.months.some((m) => m.isPartial) ?? false
  const partialDay = anchor?.months.find((m) => m.isPartial)?.partialDayOfMonth

  return (
    <div className="overflow-auto" style={{ maxHeight: '400px' }}>
      <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
        <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0 z-10">
          <tr>
            <th
              rowSpan={2}
              className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider align-bottom"
            >
              Měsíc
            </th>
            {displayMetrics.map((metric) => (
              <th
                key={metric}
                colSpan={displayYears.length + (hasDelta ? 1 : 0)}
                className={`px-4 py-2 text-center text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider ${GROUP_BORDER}`}
              >
                {COMPARISON_METRIC_LABELS[metric]}
              </th>
            ))}
          </tr>
          <tr>
            {displayMetrics.map((metric) => (
              <React.Fragment key={metric}>
                {hasDelta && (
                  <th
                    className={`px-4 py-2 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider ${GROUP_BORDER}`}
                  >
                    Δ
                  </th>
                )}
                {displayYears.map((s, i) => (
                  <th
                    key={s.year}
                    className={`px-4 py-2 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider ${
                      !hasDelta && i === 0 ? GROUP_BORDER : ''
                    }`}
                  >
                    {s.year}
                  </th>
                ))}
              </React.Fragment>
            ))}
          </tr>
        </thead>
        <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
          {monthOrder.map((month) => {
            const isPartialRow = cellFor(anchor, month)?.isPartial === true
            return (
              <tr key={month} className="hover:bg-gray-50 dark:hover:bg-white/5">
                <td className="px-4 py-3 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-graphite-text">
                  {MONTH_NAMES_FULL[month - 1]}
                  {isPartialRow && <span className="text-amber-500"> *</span>}
                </td>
                {displayMetrics.map((metric) => {
                  const anchorValue = valueFor(anchor, month, metric)
                  const previousValue = valueFor(previous, month, metric)
                  const delta =
                    anchorValue !== null && previousValue !== null ? anchorValue - previousValue : null
                  return (
                    <React.Fragment key={metric}>
                      {hasDelta && (
                        <td
                          className={`px-4 py-3 whitespace-nowrap text-sm text-right font-medium ${GROUP_BORDER} ${
                            delta === null ? 'text-gray-400 dark:text-graphite-faint' : valueColor(delta)
                          }`}
                        >
                          {delta === null ? '—' : formatCurrency(delta)}
                        </td>
                      )}
                      {displayYears.map((s, i) => {
                        const v = valueFor(s, month, metric)
                        return (
                          <td
                            key={s.year}
                            className={`px-4 py-3 whitespace-nowrap text-sm text-right font-medium ${
                              !hasDelta && i === 0 ? GROUP_BORDER : ''
                            } ${v === null ? 'text-gray-400 dark:text-graphite-faint' : valueColor(v)}`}
                          >
                            {v === null ? '—' : formatCurrency(v)}
                          </td>
                        )
                      })}
                    </React.Fragment>
                  )
                })}
              </tr>
            )
          })}
        </tbody>
      </table>
      {anchorHasPartial && partialDay !== undefined && (
        <p className="px-4 py-3 text-xs text-gray-500 dark:text-graphite-muted">
          * částečný měsíc – data k {partialDay}. dni měsíce (stejné oříznutí pro všechny roky).
        </p>
      )}
    </div>
  )
}
