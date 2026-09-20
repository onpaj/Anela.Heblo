import React, { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { X } from 'lucide-react'
import { useRecomputeMarketingPerformanceMutation } from '../../../api/hooks/useMarketingPerformance'
import { extractErrorMessage } from '../../../utils/errorHandler'

interface RecomputeDialogProps {
  isOpen: boolean
  onClose: () => void
}

const MONTH_PATTERN = /^\d{4}-(0[1-9]|1[0-2])$/
const FORMAT_ERROR = 'Zadejte měsíce ve formátu RRRR-MM.'
const ORDER_ERROR = 'Počáteční měsíc nesmí být po koncovém.'

const input = 'mt-1 block w-full rounded-md border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm'

const monthKey = (d: Date): string => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`

// Anchored on day 1: setMonth(-1) on a 29th-31st overflows back into the
// current month (2026-03-31 -> 2026-03), which would silently pre-fill a
// one-month range instead of two.
const defaultFrom = (): string => {
  const now = new Date()
  return monthKey(new Date(now.getFullYear(), now.getMonth() - 1, 1))
}
const defaultTo = (): string => monthKey(new Date())

export const RecomputeDialog: React.FC<RecomputeDialogProps> = ({ isOpen, onClose }) => {
  const [from, setFrom] = useState(defaultFrom)
  const [to, setTo] = useState(defaultTo)
  const [validationError, setValidationError] = useState<string | null>(null)
  const mutation = useRecomputeMarketingPerformanceMutation()

  // Depend on reset (stable across renders) rather than the mutation object,
  // whose identity changes every render and would re-subscribe the listener.
  const { reset } = mutation
  const close = useCallback(() => {
    reset()
    onClose()
  }, [reset, onClose])

  // Hook must run before the isOpen early return.
  useEffect(() => {
    if (!isOpen) return
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') close()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [isOpen, close])

  if (!isOpen) return null

  const submit = () => {
    if (!MONTH_PATTERN.test(from) || !MONTH_PATTERN.test(to)) {
      setValidationError(FORMAT_ERROR)
      return
    }
    if (from > to) {
      setValidationError(ORDER_ERROR)
      return
    }
    setValidationError(null)
    mutation.mutate({ from, to })
  }

  // The generated client throws on non-2xx by throwing the parsed response DTO itself (it extends
  // BaseResponse: success/errorCode/params) rather than wrapping it in a SwaggerException with a
  // `.result` property. extractErrorMessage recognizes that shape and resolves the localized
  // Czech message (including parameter interpolation) from frontend/src/i18n.ts.
  const serverError = mutation.error ? extractErrorMessage(mutation.error) : null

  return (
    <div className="fixed inset-0 z-50 overflow-y-auto" role="dialog" aria-modal="true" aria-labelledby="recompute-dialog-title">
      <div className="fixed inset-0 bg-black bg-opacity-50 transition-opacity" onClick={close} />
      <div className="flex min-h-full items-center justify-center p-4">
        <div className="relative bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark max-w-md w-full p-6">
          <button type="button" onClick={close} aria-label="Zavřít" className="absolute top-4 right-4 text-gray-400 dark:text-graphite-faint hover:text-gray-600 dark:hover:text-graphite-muted">
            <X className="h-5 w-5" />
          </button>
          <h3 id="recompute-dialog-title" className="text-lg font-semibold text-gray-900 dark:text-graphite-text mb-2">Přepočítat výkon reklamy</h3>
          <p className="text-sm text-gray-600 dark:text-graphite-muted mb-4">
            Přepočet znovu načte náklady z ABRA Flexi a tržby z vydaných faktur pro každý měsíc v období, včetně uzamčených měsíců. Běží na pozadí.
          </p>

          {mutation.isSuccess && mutation.data ? (
            <div className="text-sm text-gray-700 dark:text-graphite-text space-y-2">
              <p>Přepočet {mutation.data.monthCount} měsíců byl zařazen do fronty (úloha {mutation.data.jobId}).</p>
              <p>
                Průběh sledujte na stránce <Link to="/recurring-jobs" className="text-indigo-600 dark:text-indigo-400 underline">Naplánované úlohy</Link>.
              </p>
              <div className="flex justify-end pt-2">
                <button type="button" onClick={close} className="px-4 py-2 text-sm font-medium rounded-md bg-indigo-600 text-white hover:bg-indigo-700">Zavřít</button>
              </div>
            </div>
          ) : (
            <form onSubmit={(e) => { e.preventDefault(); submit() }} className="space-y-4">
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label htmlFor="recompute-from" className="block text-sm font-medium text-gray-700 dark:text-graphite-muted">Od (RRRR-MM)</label>
                  <input id="recompute-from" className={input} value={from} onChange={(e) => setFrom(e.target.value.trim())} placeholder="2023-01" />
                </div>
                <div>
                  <label htmlFor="recompute-to" className="block text-sm font-medium text-gray-700 dark:text-graphite-muted">Do (RRRR-MM)</label>
                  <input id="recompute-to" className={input} value={to} onChange={(e) => setTo(e.target.value.trim())} placeholder="2026-09" />
                </div>
              </div>
              {(validationError || serverError) && (
                <p className="text-sm text-red-600 dark:text-red-400" role="alert">{validationError ?? serverError}</p>
              )}
              <div className="flex justify-end gap-2">
                <button type="button" onClick={close} className="px-4 py-2 text-sm font-medium rounded-md border border-gray-300 dark:border-graphite-border text-gray-700 dark:text-graphite-text">Zrušit</button>
                <button type="submit" disabled={mutation.isPending} className="px-4 py-2 text-sm font-medium rounded-md bg-indigo-600 text-white hover:bg-indigo-700 disabled:opacity-50">
                  Spustit přepočet
                </button>
              </div>
            </form>
          )}
        </div>
      </div>
    </div>
  )
}
