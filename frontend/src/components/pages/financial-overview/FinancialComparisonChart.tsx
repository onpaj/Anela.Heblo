import React from 'react'
import type { ChartData, ChartOptions } from 'chart.js'
import { FinancialChart } from './FinancialChart'
import { formatCurrency } from './utils'
import type { YearComparisonSeriesDto } from '../../../api/hooks/useFinancialComparison'
import {
  COMPARISON_METRIC_LABELS,
  getMonthOrder,
  getMonthLabels,
  getSeriesColor,
  orderMetrics,
  projectSeriesOntoOrder,
  type ComparisonAxisMode,
  type ComparisonMetric,
} from './comparisonUtils'

interface FinancialComparisonChartProps {
  series: YearComparisonSeriesDto[]
  metrics: ComparisonMetric[]
  title: string
  axisMode: ComparisonAxisMode
  /** Current (partial) month 1..12 — anchors the rolling window's right edge. */
  currentMonth: number
}

export const FinancialComparisonChart: React.FC<FinancialComparisonChartProps> = ({
  series,
  metrics,
  title,
  axisMode,
  currentMonth,
}) => {
  const chartData = React.useMemo<ChartData<'bar'>>(() => {
    const order = getMonthOrder(axisMode, currentMonth)
    // One dataset per metric × year: color encodes the metric, opacity the year.
    // Reversed so bars read right-to-left within each month (current-year first metric on the right).
    const datasets = orderMetrics(metrics)
      .flatMap((metric) =>
        series.map((s, yearIndex) => {
          const color = getSeriesColor(metric, yearIndex)
          return {
            label: `${COMPARISON_METRIC_LABELS[metric]} ${s.year}`,
            data: projectSeriesOntoOrder(s.months, metric, order),
            backgroundColor: color,
            borderColor: color,
            borderWidth: 1,
          }
        }),
      )
      .reverse()
    return { labels: getMonthLabels(order), datasets } as ChartData<'bar'>
  }, [series, metrics, axisMode, currentMonth])

  const chartOptions = React.useMemo<ChartOptions<'bar'>>(
    () => ({
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { position: 'top' as const, align: 'center' as const },
        title: { display: false },
        tooltip: {
          callbacks: {
            label: (context) => `${context.dataset.label}: ${formatCurrency(context.parsed.y ?? 0)}`,
          },
        },
      },
      scales: {
        y: {
          beginAtZero: false,
          ticks: { callback: (value) => formatCurrency(Number(value)) },
          grid: {
            color: (context) => (context.tick.value === 0 ? '#374151' : '#e5e7eb'),
            lineWidth: (context) => (context.tick.value === 0 ? 3 : 1),
          },
        },
      },
      interaction: { intersect: false, mode: 'index' },
    }),
    [],
  )

  return <FinancialChart chartData={chartData} chartOptions={chartOptions} title={title} />
}
