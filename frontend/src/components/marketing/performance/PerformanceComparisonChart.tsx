import React from 'react'
import type { ChartData, ChartOptions } from 'chart.js'
import { FinancialChart } from '../../pages/financial-overview/FinancialChart'
import type { MarketingYearSeriesDto } from '../../../api/hooks/useMarketingPerformance'
import { withYearAlpha } from '../../charts/comparisonColors'
import { formatMetric, getMetricValue, METRIC_COLORS, METRIC_LABELS, METRIC_UNITS, MONTH_LABELS_SHORT, type PerformanceMetric } from './metrics'

interface PerformanceComparisonChartProps {
  /** Newest year first, as returned by the API. */
  series: MarketingYearSeriesDto[]
  metric: PerformanceMetric
  /** Current (partial) month 1..12; used only for the tooltip hint. */
  currentMonth: number
  anchorYear: number
}

export const buildComparisonChartData = (series: MarketingYearSeriesDto[], metric: PerformanceMetric): ChartData<'bar'> => {
  const datasets = series.map((s, yearIndex) => {
    const color = withYearAlpha(METRIC_COLORS[metric], yearIndex)
    return {
      type: 'line' as const,
      label: `${METRIC_LABELS[metric]} ${s.year}`,
      data: Array.from({ length: 12 }, (_, i) => {
        const cell = s.months.find((m) => m.month === i + 1)
        return cell && cell.hasData ? getMetricValue(cell, metric) : null
      }),
      borderColor: color,
      backgroundColor: color,
      borderWidth: yearIndex === 0 ? 3 : 2,
      tension: 0.1,
      fill: false,
      spanGaps: false,
    }
  })
  return { labels: [...MONTH_LABELS_SHORT], datasets } as unknown as ChartData<'bar'>
}

export const PerformanceComparisonChart: React.FC<PerformanceComparisonChartProps> = ({ series, metric, currentMonth, anchorYear }) => {
  const chartData = React.useMemo(() => buildComparisonChartData(series, metric), [series, metric])
  const unit = METRIC_UNITS[metric]

  const chartOptions = React.useMemo<ChartOptions<'bar'>>(
    () => ({
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { position: 'top' as const },
        title: { display: false },
        tooltip: {
          mode: 'index' as const,
          callbacks: {
            label: (context) => {
              const value = context.parsed.y
              if (value === null || value === undefined) return `${context.dataset.label}: —`
              return `${context.dataset.label}: ${formatMetric(value, unit)}`
            },
            // The tooltip is mode:'index', so it lists every year for the hovered month.
            // Only the anchor year's current month is partial — the same month in earlier
            // years is complete, so name the year rather than implying all of them.
            footer: (items) => (items[0]?.dataIndex === currentMonth - 1 ? `Aktuální měsíc (${anchorYear}) je neúplný` : ''),
          },
        },
      },
      scales: {
        y: { beginAtZero: true, ticks: { callback: (v) => formatMetric(Number(v), unit) } },
      },
      interaction: { intersect: false, mode: 'index' },
    }),
    [unit, currentMonth, anchorYear],
  )

  return <FinancialChart chartData={chartData} chartOptions={chartOptions} title={`Meziroční srovnání — ${METRIC_LABELS[metric]}`} />
}
