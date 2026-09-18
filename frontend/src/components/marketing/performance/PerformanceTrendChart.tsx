import React from 'react'
import type { ChartData, ChartOptions } from 'chart.js'
import { FinancialChart } from '../../pages/financial-overview/FinancialChart'
import type { ChannelInfoDto, MonthlyMarketingPerformanceDto } from '../../../api/hooks/useMarketingPerformance'
import { withYearAlpha } from '../../charts/comparisonColors'
import { CHANNEL_COLORS, formatCzk, formatMetric, getMetricValue, METRIC_COLORS, METRIC_LABELS, METRIC_UNITS, type PerformanceMetric } from './metrics'

interface PerformanceTrendChartProps {
  months: MonthlyMarketingPerformanceDto[]
  channels: ChannelInfoDto[]
  metric: PerformanceMetric
}

const COST_STACK = 'costs'
const COST_AXIS = 'y'
const METRIC_AXIS = 'y1'

const byMonthAsc = (a: MonthlyMarketingPerformanceDto, b: MonthlyMarketingPerformanceDto) =>
  a.year * 100 + a.month - (b.year * 100 + b.month)

export const buildTrendChartData = (
  months: MonthlyMarketingPerformanceDto[],
  channels: ChannelInfoDto[],
  metric: PerformanceMetric,
): ChartData<'bar'> => {
  const sorted = [...months].sort(byMonthAsc)
  const labels = sorted.map((m) => m.monthYearDisplay)

  const channelDatasets = channels.map((channel, index) => {
    const color = withYearAlpha(CHANNEL_COLORS[index % CHANNEL_COLORS.length], 0)
    return {
      type: 'bar' as const,
      label: channel.label,
      stack: COST_STACK,
      yAxisID: COST_AXIS,
      data: sorted.map((m) => (m.hasData ? m.channelCosts.find((c) => c.channelCode === channel.code)?.costWithoutVat ?? 0 : 0)),
      backgroundColor: color,
      borderColor: color,
      borderWidth: 1,
    }
  })

  if (metric === 'totalCost') {
    return { labels, datasets: channelDatasets } as ChartData<'bar'>
  }

  const metricColor = withYearAlpha(METRIC_COLORS[metric], 0)
  const metricDataset = {
    type: 'line' as const,
    label: METRIC_LABELS[metric],
    yAxisID: METRIC_AXIS,
    data: sorted.map((m) => (m.hasData ? getMetricValue(m, metric) : null)),
    borderColor: metricColor,
    backgroundColor: metricColor,
    fill: false,
    tension: 0.1,
    borderWidth: 3,
    spanGaps: false,
  }

  return { labels, datasets: [...channelDatasets, metricDataset] } as unknown as ChartData<'bar'>
}

export const PerformanceTrendChart: React.FC<PerformanceTrendChartProps> = ({ months, channels, metric }) => {
  const chartData = React.useMemo(() => buildTrendChartData(months, channels, metric), [months, channels, metric])
  const unit = METRIC_UNITS[metric]

  const chartOptions = React.useMemo<ChartOptions<'bar'>>(
    () => ({
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { position: 'top' as const },
        title: { display: false },
        tooltip: {
          callbacks: {
            label: (context) => {
              const value = context.parsed.y
              if (value === null || value === undefined) return `${context.dataset.label}: —`
              const isMetricLine = context.dataset.yAxisID === METRIC_AXIS
              return `${context.dataset.label}: ${isMetricLine ? formatMetric(value, unit) : formatCzk(value)}`
            },
          },
        },
      },
      scales: {
        x: { stacked: true },
        [COST_AXIS]: { stacked: true, position: 'left' as const, beginAtZero: true, ticks: { callback: (v) => formatCzk(Number(v)) } },
        [METRIC_AXIS]: {
          display: metric !== 'totalCost',
          position: 'right' as const,
          beginAtZero: true,
          grid: { drawOnChartArea: false },
          ticks: { callback: (v) => formatMetric(Number(v), unit) },
        },
      },
      interaction: { intersect: false, mode: 'index' },
    }),
    [metric, unit],
  )

  return <FinancialChart chartData={chartData} chartOptions={chartOptions} title={`Vývoj — ${METRIC_LABELS[metric]}`} />
}
