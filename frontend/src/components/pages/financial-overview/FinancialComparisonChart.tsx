import React from 'react'
import type { ChartData, ChartOptions } from 'chart.js'
import { FinancialChart } from './FinancialChart'
import { formatCurrency } from './utils'
import type { YearComparisonSeriesDto } from '../../../api/hooks/useFinancialComparison'
import {
  MONTH_LABELS_SHORT,
  YEAR_SERIES_COLORS,
  pivotSeriesToMonthly,
  type ComparisonMetric,
} from './comparisonUtils'

interface FinancialComparisonChartProps {
  series: YearComparisonSeriesDto[]
  metric: ComparisonMetric
  title: string
}

export const FinancialComparisonChart: React.FC<FinancialComparisonChartProps> = ({
  series,
  metric,
  title,
}) => {
  const chartData = React.useMemo<ChartData<'bar'>>(() => {
    const datasets = series.map((s, index) => {
      const color = YEAR_SERIES_COLORS[index % YEAR_SERIES_COLORS.length]
      return {
        label: String(s.year),
        type: 'line' as const,
        data: pivotSeriesToMonthly(s.months, metric),
        borderColor: color,
        backgroundColor: color,
        spanGaps: false,
        tension: 0.1,
        borderWidth: 3,
        pointRadius: 3,
      }
    })
    return { labels: [...MONTH_LABELS_SHORT], datasets } as unknown as ChartData<'bar'>
  }, [series, metric])

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
