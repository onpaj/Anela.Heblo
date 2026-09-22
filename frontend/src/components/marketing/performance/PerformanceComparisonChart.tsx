import React from 'react'
import type { ChartData, ChartOptions } from 'chart.js'
import { FinancialChart } from '../../pages/financial-overview/FinancialChart'
import type { ChannelInfoDto, MarketingYearSeriesDto } from '../../../api/hooks/useMarketingPerformance'
import { withYearAlpha } from '../../charts/comparisonColors'
import { CHANNEL_COLORS, formatMetric, getMetricValue, METRIC_COLORS, METRIC_LABELS, METRIC_UNITS, MONTH_LABELS_SHORT, resolveBreakdownChannels, type PerformanceMetric } from './metrics'

interface PerformanceComparisonChartProps {
  /** Newest year first, as returned by the API. */
  series: MarketingYearSeriesDto[]
  metric: PerformanceMetric
  channels: ChannelInfoDto[]
  /** Current (partial) month 1..12; used only for the tooltip hint. */
  currentMonth: number
  anchorYear: number
}

/**
 * "Náklady na reklamu" means the per-channel breakdown here, exactly as it does in the trend
 * chart. Each year gets its own stack id, so Chart.js draws the years as adjacent stacked
 * columns within one month rather than stacking every year on top of each other.
 */
const buildCostBreakdownDatasets = (series: MarketingYearSeriesDto[], channels: ChannelInfoDto[]) => {
  const breakdown = resolveBreakdownChannels(channels, series.flatMap((s) => s.months))

  return series.flatMap((s, yearIndex) =>
    breakdown.map((channel, channelIndex) => {
      const color = withYearAlpha(CHANNEL_COLORS[channelIndex % CHANNEL_COLORS.length], yearIndex)
      return {
        type: 'bar' as const,
        label: `${channel.label} ${s.year}`,
        stack: `year-${s.year}`,
        data: Array.from({ length: 12 }, (_, i) => {
          const cell = s.months.find((m) => m.month === i + 1)
          // null, not 0: a month with no data yet must read as "—" in the tooltip rather than
          // claiming we spent nothing. A channel missing from a month that *does* have data is a
          // genuine zero.
          if (!cell || !cell.hasData) return null
          return cell.channelCosts.find((c) => c.channelCode === channel.code)?.costWithoutVat ?? 0
        }),
        backgroundColor: color,
        borderColor: color,
        borderWidth: 1,
      }
    }),
  )
}

export const buildComparisonChartData = (
  series: MarketingYearSeriesDto[],
  metric: PerformanceMetric,
  channels: ChannelInfoDto[] = [],
): ChartData<'bar'> => {
  if (metric === 'totalCost') {
    return { labels: [...MONTH_LABELS_SHORT], datasets: buildCostBreakdownDatasets(series, channels) } as ChartData<'bar'>
  }

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

export const PerformanceComparisonChart: React.FC<PerformanceComparisonChartProps> = ({ series, metric, channels, currentMonth, anchorYear }) => {
  const chartData = React.useMemo(() => buildComparisonChartData(series, metric, channels), [series, metric, channels])
  const unit = METRIC_UNITS[metric]
  const isCostBreakdown = metric === 'totalCost'

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
        x: { stacked: isCostBreakdown },
        y: { stacked: isCostBreakdown, beginAtZero: true, ticks: { callback: (v) => formatMetric(Number(v), unit) } },
      },
      interaction: { intersect: false, mode: 'index' },
    }),
    [unit, currentMonth, anchorYear, isCostBreakdown],
  )

  return <FinancialChart chartData={chartData} chartOptions={chartOptions} title={`Meziroční srovnání — ${METRIC_LABELS[metric]}`} />
}
