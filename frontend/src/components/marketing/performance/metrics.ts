import type { ChannelInfoDto, MonthlyMarketingPerformanceDto } from '../../../api/hooks/useMarketingPerformance'
import type { Rgb } from '../../charts/comparisonColors'

export type PerformanceMetric =
  | 'totalCost'
  | 'revenueWithoutVat'
  | 'orders'
  | 'pno'
  | 'roas'
  | 'avgOrderValue'
  | 'costPerOrder'

export type MetricUnit = 'czk' | 'percent' | 'count'

export const PERFORMANCE_METRICS: PerformanceMetric[] = [
  'totalCost', 'revenueWithoutVat', 'orders', 'pno', 'roas', 'avgOrderValue', 'costPerOrder',
]

export const METRIC_LABELS: Record<PerformanceMetric, string> = {
  totalCost: 'Náklady na reklamu',
  revenueWithoutVat: 'Tržby (bez DPH)',
  orders: 'Objednávky',
  pno: 'PNO',
  roas: 'ROAS',
  avgOrderValue: 'Průměrná objednávka',
  costPerOrder: 'Cena za nákup',
}

export const METRIC_UNITS: Record<PerformanceMetric, MetricUnit> = {
  totalCost: 'czk',
  revenueWithoutVat: 'czk',
  orders: 'count',
  pno: 'percent',
  roas: 'percent',
  avgOrderValue: 'czk',
  costPerOrder: 'czk',
}

export const METRIC_COLORS: Record<PerformanceMetric, Rgb> = {
  totalCost: [239, 68, 68],        // red-500
  revenueWithoutVat: [34, 197, 94], // green-500
  orders: [59, 130, 246],          // blue-500
  pno: [249, 115, 22],             // orange-500
  roas: [168, 85, 247],            // purple-500
  avgOrderValue: [20, 184, 166],   // teal-500
  costPerOrder: [245, 158, 11],    // amber-500
}

/** Stacked channel-cost bars; cycled by channel index (config order). */
export const CHANNEL_COLORS: Rgb[] = [
  [59, 130, 246],  // blue — FB/IG
  [234, 179, 8],   // yellow — Google
  [220, 38, 38],   // red — S-Klik
  [107, 114, 128], // gray — any further channel
]

/**
 * The channel list to draw a cost breakdown from.
 *
 * `channels` carries only the configured channels, but `totalCost` is the sum of every stored
 * channel cost — the server deliberately appends rows for codes that are no longer in config so
 * nothing disappears silently. Drawing the configured list alone would make the stacked columns
 * add up to less than the "Náklady" card and the "Náklady celkem" table column on the same page.
 * Configured channels keep their config order (and therefore their colour); orphans follow.
 */
export const resolveBreakdownChannels = (
  channels: ChannelInfoDto[],
  rows: MonthlyMarketingPerformanceDto[],
): ChannelInfoDto[] => {
  const seen = new Set(channels.map((c) => c.code))
  const orphans: ChannelInfoDto[] = []

  for (const row of rows) {
    for (const cost of row.channelCosts ?? []) {
      if (seen.has(cost.channelCode)) continue
      seen.add(cost.channelCode)
      orphans.push({ code: cost.channelCode, label: cost.label ?? cost.channelCode } as ChannelInfoDto)
    }
  }

  return orphans.length === 0 ? channels : [...channels, ...orphans]
}

export const MONTH_LABELS_SHORT = [
  'Led', 'Úno', 'Bře', 'Dub', 'Kvě', 'Čvn', 'Čvc', 'Srp', 'Zář', 'Říj', 'Lis', 'Pro',
] as const

export const getMetricValue = (row: MonthlyMarketingPerformanceDto, metric: PerformanceMetric): number | null => {
  const value = row[metric]
  return value === null || value === undefined ? null : Number(value)
}

const czk = new Intl.NumberFormat('cs-CZ', { style: 'currency', currency: 'CZK', minimumFractionDigits: 0, maximumFractionDigits: 0 })
const count = new Intl.NumberFormat('cs-CZ', { maximumFractionDigits: 0 })
const percent = new Intl.NumberFormat('cs-CZ', { minimumFractionDigits: 1, maximumFractionDigits: 1 })

export const EMPTY_VALUE = '—'

export const formatCzk = (value: number): string => czk.format(value)
export const formatCount = (value: number): string => count.format(value)
export const formatPercent = (value: number): string => `${percent.format(value)} %`

export const formatMetric = (value: number | null | undefined, unit: MetricUnit): string => {
  if (value === null || value === undefined || Number.isNaN(value)) return EMPTY_VALUE
  switch (unit) {
    case 'czk':
      return formatCzk(value)
    case 'percent':
      return formatPercent(value)
    case 'count':
      return formatCount(value)
    default: {
      const _exhaustive: never = unit
      throw new Error(`Unhandled unit: ${_exhaustive}`)
    }
  }
}

/** YoY ratio arrives as "this year / last year × 100"; show it as a signed delta ("+12,3 %"). */
export const formatYoy = (ratioPercent: number | null | undefined): string => {
  if (ratioPercent === null || ratioPercent === undefined || Number.isNaN(ratioPercent)) return EMPTY_VALUE
  const delta = ratioPercent - 100
  const sign = delta > 0 ? '+' : delta < 0 ? '−' : ''
  return `${sign}${percent.format(Math.abs(delta))} %`
}
