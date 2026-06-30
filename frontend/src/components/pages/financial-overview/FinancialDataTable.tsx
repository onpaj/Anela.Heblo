import React from 'react'
import type { MonthlyFinancialDataDto } from '../../../api/hooks/useFinancialOverview'
import { formatCurrency } from './utils'

interface FinancialDataTableProps {
  data: MonthlyFinancialDataDto[]
  includeStockData: boolean
}

export const FinancialDataTable: React.FC<FinancialDataTableProps> = ({
  data,
  includeStockData,
}) => (
  <div className="overflow-auto" style={{ maxHeight: '400px' }}>
    <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
      <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0 z-10">
        <tr>
          <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
            Měsíc
          </th>
          <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
            Příjmy
          </th>
          <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
            Náklady
          </th>
          <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
            Účetní bilance
          </th>
          {includeStockData && (
            <>
              <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                Změna skladu
              </th>
              <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                Celková bilance
              </th>
            </>
          )}
        </tr>
      </thead>
      <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
        {data.map((item) => (
          <tr key={`${item.year}-${item.month}`} className="hover:bg-gray-50 dark:hover:bg-white/5">
            <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-graphite-text">
              {item.monthYearDisplay}
            </td>
            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-graphite-text text-right">
              {formatCurrency(item.income)}
            </td>
            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-graphite-text text-right">
              {formatCurrency(item.expenses)}
            </td>
            <td
              className={`px-6 py-4 whitespace-nowrap text-sm text-right font-medium ${
                item.financialBalance >= 0 ? 'text-emerald-600 dark:text-emerald-400' : 'text-red-600 dark:text-red-400'
              }`}
            >
              {formatCurrency(item.financialBalance)}
            </td>
            {includeStockData && (
              <>
                <td
                  className={`px-6 py-4 whitespace-nowrap text-sm text-right ${
                    (item.totalStockValueChange || 0) >= 0
                      ? 'text-purple-600 dark:text-purple-400'
                      : 'text-orange-600 dark:text-amber-400'
                  }`}
                >
                  {formatCurrency(item.totalStockValueChange || 0)}
                </td>
                <td
                  className={`px-6 py-4 whitespace-nowrap text-sm text-right font-medium ${
                    (item.totalBalance || item.financialBalance) >= 0
                      ? 'text-emerald-600 dark:text-emerald-400'
                      : 'text-red-600 dark:text-red-400'
                  }`}
                >
                  {formatCurrency(item.totalBalance || item.financialBalance)}
                </td>
              </>
            )}
          </tr>
        ))}
      </tbody>
    </table>
  </div>
)
