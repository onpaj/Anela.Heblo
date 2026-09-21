import React from 'react'
import { AlertTriangle, Lock } from 'lucide-react'
import type { ChannelInfoDto, MonthlyMarketingPerformanceDto } from '../../../api/hooks/useMarketingPerformance'
import { formatCount, formatCzk, formatMetric, formatYoy } from './metrics'

interface PerformanceTableProps {
  months: MonthlyMarketingPerformanceDto[]
  channels: ChannelInfoDto[]
}

const LOCK_TITLE = 'Měsíc je uzamčen — mění ho jen ruční přepočet'
const STALE_TITLE = 'Data zatím nebyla načtena'
const EMPTY_TEXT = 'Zatím nejsou k dispozici žádná data. Spusťte přepočet nebo počkejte na noční úlohu.'

const th = 'px-3 py-2 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider whitespace-nowrap'
const td = 'px-3 py-2 text-right text-sm text-gray-900 dark:text-graphite-text whitespace-nowrap tabular-nums'

const hasWarning = (m: MonthlyMarketingPerformanceDto): boolean =>
  m.hasData && (Boolean(m.lastError) || !m.revenueComputedAt || !m.costsComputedAt)

const channelCost = (m: MonthlyMarketingPerformanceDto, code: string): number =>
  m.channelCosts.find((c) => c.channelCode === code)?.costWithoutVat ?? 0

const profitClass = (v: number): string => (v >= 0 ? 'text-emerald-600 dark:text-emerald-400' : 'text-red-600 dark:text-red-400')

export const PerformanceTable: React.FC<PerformanceTableProps> = ({ months, channels }) => {
  const rows = React.useMemo(
    () => [...months].filter((m) => m.hasData).sort((a, b) => b.year * 100 + b.month - (a.year * 100 + a.month)),
    [months],
  )

  if (rows.length === 0) {
    return (
      <div className="p-6 text-sm text-gray-600 dark:text-graphite-muted text-center" data-testid="performance-table-empty">
        {EMPTY_TEXT}
      </div>
    )
  }

  return (
    <div className="overflow-x-auto">
      <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border" data-testid="performance-table">
        <thead className="bg-gray-50 dark:bg-graphite-surface-2">
          <tr>
            <th scope="col" className={`${th} text-left`}>Měsíc</th>
            {channels.map((c) => (
              <th key={c.code} scope="col" className={th}>{c.label}</th>
            ))}
            <th scope="col" className={th}>Náklady celkem</th>
            <th scope="col" className={th}>Tržby s DPH</th>
            <th scope="col" className={th}>Tržby bez DPH</th>
            <th scope="col" className={th}>PNO</th>
            <th scope="col" className={th}>ROAS</th>
            <th scope="col" className={th}>Rozdíl tržby − náklady</th>
            <th scope="col" className={th}>Objednávky</th>
            <th scope="col" className={th}>Prům. objednávka</th>
            <th scope="col" className={th}>Cena za nákup</th>
            <th scope="col" className={th}>Náklady r/r</th>
            <th scope="col" className={th}>Tržby r/r</th>
            <th scope="col" className={th}>Objednávky r/r</th>
          </tr>
        </thead>
        <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
          {rows.map((m) => (
            <tr key={`${m.year}-${m.month}`} className="hover:bg-gray-50 dark:hover:bg-graphite-surface-2">
              <td className={`${td} text-left font-medium`}>
                <span className="inline-flex items-center gap-1">
                  {m.monthYearDisplay}
                  {m.isPartial && <span className="text-xs text-gray-500 dark:text-graphite-muted">(probíhá)</span>}
                  {m.isLocked && (
                    <span title={LOCK_TITLE}>
                      <Lock className="h-3.5 w-3.5 text-gray-400" aria-hidden="true" />
                      <span className="sr-only">{LOCK_TITLE}</span>
                    </span>
                  )}
                  {hasWarning(m) && (
                    <span title={m.lastError ?? STALE_TITLE}>
                      <AlertTriangle className="h-3.5 w-3.5 text-amber-500" aria-hidden="true" />
                      <span className="sr-only">{m.lastError ?? STALE_TITLE}</span>
                    </span>
                  )}
                </span>
              </td>
              {channels.map((c) => (
                <td key={c.code} className={td}>{formatCzk(channelCost(m, c.code))}</td>
              ))}
              <td className={`${td} font-medium`}>{formatCzk(m.totalCost)}</td>
              <td className={td}>{formatCzk(m.revenueWithVat)}</td>
              <td className={td}>{formatCzk(m.revenueWithoutVat)}</td>
              <td className={td}>{formatMetric(m.pno, 'percent')}</td>
              <td className={td}>{formatMetric(m.roas, 'percent')}</td>
              <td className={`${td} ${profitClass(m.profit)}`}>{formatCzk(m.profit)}</td>
              <td className={td}>{formatCount(m.orders)}</td>
              <td className={td}>{formatMetric(m.avgOrderValue, 'czk')}</td>
              <td className={td}>{formatMetric(m.costPerOrder, 'czk')}</td>
              <td className={td}>{formatYoy(m.yoyCostPercent)}</td>
              <td className={td}>{formatYoy(m.yoyRevenuePercent)}</td>
              <td className={td}>{formatYoy(m.yoyOrdersPercent)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
