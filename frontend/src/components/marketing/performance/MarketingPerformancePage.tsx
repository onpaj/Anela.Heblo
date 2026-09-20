import React, { useMemo, useState } from 'react'
import { AlertTriangle } from 'lucide-react'
import { PAGE_CONTAINER_HEIGHT } from '../../../constants/layout'
import { usePermissionsContext } from '../../../auth/PermissionsContext'
import { useScreenView } from '../../../telemetry/useScreenView'
import {
  useMarketingPerformanceComparisonQuery,
  useMarketingPerformanceMonthsQuery,
  type MonthlyMarketingPerformanceDto,
} from '../../../api/hooks/useMarketingPerformance'
import { PerformanceToolbar, type PerformancePeriod, type PerformanceViewMode } from './PerformanceToolbar'
import { PerformanceTrendChart } from './PerformanceTrendChart'
import { PerformanceComparisonChart } from './PerformanceComparisonChart'
import { PerformanceTable } from './PerformanceTable'
import { RecomputeDialog } from './RecomputeDialog'
import { formatCount, formatCzk, formatMetric, type PerformanceMetric } from './metrics'

const WRITE_PERMISSION = 'marketing.performance.write'
const DEFAULT_YEARS = 3

const monthKey = (d: Date): string => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`

const rangeForPeriod = (period: PerformancePeriod): { from: string; to: string } => {
  const to = new Date()
  const from = new Date(to.getFullYear(), to.getMonth() - (period - 1), 1)
  return { from: monthKey(from), to: monthKey(to) }
}

const isMonthStale = (m: MonthlyMarketingPerformanceDto): boolean =>
  m.hasData && (Boolean(m.lastError) || !m.revenueComputedAt || !m.costsComputedAt)

const MarketingPerformancePage: React.FC = () => {
  const [viewMode, setViewMode] = useState<PerformanceViewMode>('trend')
  const [period, setPeriod] = useState<PerformancePeriod>(12)
  const [years, setYears] = useState<number>(DEFAULT_YEARS)
  const [metric, setMetric] = useState<PerformanceMetric>('pno')
  const [includeWholesale, setIncludeWholesale] = useState(false)
  const [isRecomputeOpen, setRecomputeOpen] = useState(false)

  useScreenView('Marketing', 'MarketingPerformance')
  const { hasPermission } = usePermissionsContext()
  const canRecompute = hasPermission(WRITE_PERMISSION)

  const range = useMemo(() => rangeForPeriod(period), [period])
  const months = useMarketingPerformanceMonthsQuery({ ...range, includeWholesale }, viewMode === 'trend')
  const comparison = useMarketingPerformanceComparisonQuery({ years, includeWholesale }, viewMode === 'comparison')

  const active = viewMode === 'trend' ? months : comparison
  const monthRows = months.data?.months ?? []
  const channels = (viewMode === 'trend' ? months.data?.channels : comparison.data?.channels) ?? []
  const lastRefreshAt = viewMode === 'trend' ? months.data?.lastRefreshAt : comparison.data?.lastRefreshAt
  const hasWarnings =
    viewMode === 'trend'
      ? monthRows.some(isMonthStale)
      : (comparison.data?.series ?? []).some((s) => s.months.some(isMonthStale))

  return (
    <div className="flex flex-col w-full" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">Analýzy</h1>
        <p className="mt-1 text-gray-600 dark:text-graphite-muted">
          Náklady na reklamu (FB/IG, Google, S-Klik) proti objednávkám a tržbám z e-shopu, po měsících
        </p>
      </div>

      <div className="flex-1 overflow-auto">
        <PerformanceToolbar
          viewMode={viewMode}
          period={period}
          years={years}
          metric={metric}
          includeWholesale={includeWholesale}
          canRecompute={canRecompute}
          lastRefreshAt={lastRefreshAt}
          hasWarnings={hasWarnings}
          isRefetching={Boolean(months.isRefetching)}
          onViewModeChange={setViewMode}
          onPeriodChange={setPeriod}
          onYearsChange={setYears}
          onMetricChange={setMetric}
          onIncludeWholesaleChange={setIncludeWholesale}
          onRecomputeClick={() => setRecomputeOpen(true)}
        />

        {active.isLoading && (
          <div className="flex items-center justify-center py-12">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
            <span className="ml-2 text-gray-600 dark:text-graphite-muted">Načítám data…</span>
          </div>
        )}

        {active.error && (
          <div className="mb-8 p-4 bg-red-50 dark:bg-red-900/30 border border-red-200 dark:border-red-900/50 rounded-lg">
            <div className="flex items-center">
              <AlertTriangle className="w-5 h-5 text-red-500 mr-2" />
              <h3 className="text-red-800 dark:text-red-300 font-medium">Chyba při načítání dat</h3>
            </div>
            <p className="mt-1 text-red-700 dark:text-red-300 text-sm">{active.error.message || 'Neznámá chyba'}</p>
          </div>
        )}

        {viewMode === 'trend' && months.data && (
          <>
            <PerformanceTrendChart months={monthRows} channels={channels} metric={metric} />
            <div className="bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark sm:rounded-md mb-8">
              <div className="px-4 py-5 sm:px-6 border-b border-gray-200 dark:border-graphite-border">
                <h3 className="text-lg leading-6 font-medium text-gray-900 dark:text-graphite-text">Měsíční přehled</h3>
              </div>
              <PerformanceTable months={monthRows} channels={channels} />
            </div>
          </>
        )}

        {viewMode === 'comparison' && comparison.data && (
          <>
            <div className="grid grid-cols-2 md:grid-cols-3 gap-4 mb-6">
              {comparison.data.series.map((s) => (
                <div key={s.year} className="bg-white dark:bg-graphite-surface overflow-hidden shadow dark:shadow-soft-dark rounded-lg p-3">
                  <div className="text-xs font-semibold text-gray-700 dark:text-graphite-text mb-1">{s.year} (YTD)</div>
                  <dl className="space-y-0.5 text-xs">
                    <div className="flex justify-between gap-2"><dt className="text-gray-500 dark:text-graphite-muted">Náklady</dt><dd className="font-medium">{formatCzk(s.ytdTotalCost)}</dd></div>
                    <div className="flex justify-between gap-2"><dt className="text-gray-500 dark:text-graphite-muted">Tržby bez DPH</dt><dd className="font-medium">{formatCzk(s.ytdRevenueWithoutVat)}</dd></div>
                    <div className="flex justify-between gap-2"><dt className="text-gray-500 dark:text-graphite-muted">Objednávky</dt><dd className="font-medium">{formatCount(s.ytdOrders)}</dd></div>
                    <div className="flex justify-between gap-2"><dt className="text-gray-500 dark:text-graphite-muted">PNO</dt><dd className="font-medium">{formatMetric(s.ytdPno, 'percent')}</dd></div>
                  </dl>
                </div>
              ))}
            </div>
            <PerformanceComparisonChart series={comparison.data.series} metric={metric} currentMonth={comparison.data.currentMonth} />
            <div className="bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark sm:rounded-md mb-8">
              <div className="px-4 py-5 sm:px-6 border-b border-gray-200 dark:border-graphite-border">
                <h3 className="text-lg leading-6 font-medium text-gray-900 dark:text-graphite-text">Měsíce podle roku</h3>
              </div>
              <PerformanceTable months={comparison.data.series.flatMap((s) => s.months)} channels={channels} />
            </div>
          </>
        )}
      </div>

      <RecomputeDialog isOpen={isRecomputeOpen} onClose={() => setRecomputeOpen(false)} />
    </div>
  )
}

export default MarketingPerformancePage
