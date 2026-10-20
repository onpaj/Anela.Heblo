import React from 'react'
import { RefreshCw, AlertTriangle, Info } from 'lucide-react'
import { METRIC_DESCRIPTIONS, METRIC_LABELS, PERFORMANCE_METRICS, VIEW_MODE_DESCRIPTIONS, type PerformanceMetric } from './metrics'

export type PerformanceViewMode = 'trend' | 'comparison'
export type PerformancePeriod = 12 | 24 | 36

interface PerformanceToolbarProps {
  viewMode: PerformanceViewMode
  period: PerformancePeriod
  years: number
  metric: PerformanceMetric
  includeWholesale: boolean
  hideCurrentMonth: boolean
  canRecompute: boolean
  lastRefreshAt?: string | Date | null
  hasWarnings: boolean
  isRefetching: boolean
  onViewModeChange: (mode: PerformanceViewMode) => void
  onPeriodChange: (period: PerformancePeriod) => void
  onYearsChange: (years: number) => void
  onMetricChange: (metric: PerformanceMetric) => void
  onIncludeWholesaleChange: (value: boolean) => void
  onHideCurrentMonthChange: (value: boolean) => void
  onRecomputeClick: () => void
}

const select = 'block pl-3 pr-10 py-2 text-base border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md'
const label = 'block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1'
const labelRow = 'flex items-center gap-1 mb-1'
const labelInRow = 'block text-sm font-medium text-gray-700 dark:text-graphite-muted'

const formatDateTime = (value: string | Date): string =>
  new Intl.DateTimeFormat('cs-CZ', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value))

/**
 * Explanatory bubble for the readers who do not work in marketing. There is no shared Tooltip
 * component in this codebase; this follows the established group-hover pattern (see CountTile) and
 * adds focus-within so it also opens from the keyboard, which a native `title` never does. The text
 * stays in the DOM and is only visually hidden, so screen readers reach it through aria-describedby.
 */
const HelpTooltip: React.FC<{ id: string; label: string; text: string }> = ({ id, label, text }) => (
  <span className="group relative inline-flex items-center align-middle">
    <button
      type="button"
      aria-label={label}
      aria-describedby={id}
      className="text-gray-400 hover:text-gray-600 dark:text-graphite-muted dark:hover:text-graphite-text focus:outline-none focus:ring-2 focus:ring-indigo-500 rounded-full"
    >
      <Info className="h-3.5 w-3.5" />
    </button>
    <span
      id={id}
      role="tooltip"
      className="pointer-events-none absolute left-0 top-full z-20 mt-1 w-72 rounded-md bg-gray-900 dark:bg-graphite-surface-2 px-3 py-2 text-xs font-normal leading-relaxed text-white dark:text-graphite-text shadow-lg ring-1 ring-black/10 dark:ring-graphite-border opacity-0 group-hover:opacity-100 group-focus-within:opacity-100 transition-opacity"
    >
      {text}
    </span>
  </span>
)

export const PerformanceToolbar: React.FC<PerformanceToolbarProps> = (props) => {
  const {
    viewMode, period, years, metric, includeWholesale, hideCurrentMonth, canRecompute, lastRefreshAt, hasWarnings, isRefetching,
    onViewModeChange, onPeriodChange, onYearsChange, onMetricChange, onIncludeWholesaleChange,
    onHideCurrentMonthChange, onRecomputeClick,
  } = props

  return (
    <div className="bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg p-4 mb-4">
      <div className="flex flex-col lg:flex-row lg:items-end gap-4">
        <div>
          <div className={labelRow}>
            <label htmlFor="performance-view-mode" className={labelInRow}>Zobrazení</label>
            <HelpTooltip id="performance-view-mode-help" label="Co znamená toto zobrazení" text={VIEW_MODE_DESCRIPTIONS[viewMode]} />
          </div>
          <select id="performance-view-mode" className={`${select} w-52`} value={viewMode} onChange={(e) => onViewModeChange(e.target.value as PerformanceViewMode)}>
            <option value="trend">Vývoj</option>
            <option value="comparison">Meziroční srovnání</option>
          </select>
        </div>

        {viewMode === 'trend' ? (
          <div>
            <label htmlFor="performance-period" className={label}>Období</label>
            <select id="performance-period" className={`${select} w-44`} value={period} onChange={(e) => onPeriodChange(Number(e.target.value) as PerformancePeriod)}>
              <option value={12}>Posledních 12 měsíců</option>
              <option value={24}>Posledních 24 měsíců</option>
              <option value={36}>Posledních 36 měsíců</option>
            </select>
          </div>
        ) : (
          <div>
            <label htmlFor="performance-years" className={label}>Počet roků</label>
            <select id="performance-years" className={`${select} w-32`} value={years} onChange={(e) => onYearsChange(Number(e.target.value))}>
              <option value={2}>2 roky</option>
              <option value={3}>3 roky</option>
            </select>
          </div>
        )}

        <div>
          <div className={labelRow}>
            <label htmlFor="performance-metric" className={labelInRow}>Metrika</label>
            <HelpTooltip id="performance-metric-help" label="Co znamená tato metrika" text={METRIC_DESCRIPTIONS[metric]} />
          </div>
          <select id="performance-metric" className={`${select} w-52`} value={metric} onChange={(e) => onMetricChange(e.target.value as PerformanceMetric)}>
            {PERFORMANCE_METRICS.map((m) => (
              <option key={m} value={m}>{METRIC_LABELS[m]}</option>
            ))}
          </select>
        </div>

        <label className="inline-flex items-center gap-2 text-sm text-gray-700 dark:text-graphite-text pb-2 cursor-pointer">
          <input
            type="checkbox"
            className="h-4 w-4 rounded border-gray-300 dark:border-graphite-border text-indigo-600 focus:ring-indigo-500"
            checked={includeWholesale}
            onChange={(e) => onIncludeWholesaleChange(e.target.checked)}
          />
          včetně velkoobchodu
        </label>

        <label className="inline-flex items-center gap-2 text-sm text-gray-700 dark:text-graphite-text pb-2 cursor-pointer">
          <input
            type="checkbox"
            className="h-4 w-4 rounded border-gray-300 dark:border-graphite-border text-indigo-600 focus:ring-indigo-500"
            checked={hideCurrentMonth}
            onChange={(e) => onHideCurrentMonthChange(e.target.checked)}
          />
          skrýt probíhající měsíc
        </label>

        <div className="flex-1" />

        <div className="flex items-center gap-3 text-xs text-gray-500 dark:text-graphite-muted pb-2">
          {isRefetching && <RefreshCw className="h-3.5 w-3.5 animate-spin" aria-label="Načítání" />}
          {hasWarnings && (
            <span className="inline-flex items-center gap-1 text-amber-600 dark:text-amber-400">
              <AlertTriangle className="h-3.5 w-3.5" /> některé měsíce mají neúplná data
            </span>
          )}
          <span>Poslední aktualizace: {lastRefreshAt ? formatDateTime(lastRefreshAt) : '—'}</span>
        </div>

        {canRecompute && (
          <button
            type="button"
            onClick={onRecomputeClick}
            className="inline-flex items-center px-3 py-2 border border-gray-300 dark:border-graphite-border shadow-sm text-sm font-medium rounded-md text-gray-700 dark:text-graphite-text bg-white dark:bg-graphite-surface-2 hover:bg-gray-50 dark:hover:bg-graphite-surface"
          >
            Přepočítat
          </button>
        )}
      </div>
    </div>
  )
}
