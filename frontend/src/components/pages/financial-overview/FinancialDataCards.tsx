import React from 'react'
import type { MonthlyFinancialDataDto } from '../../../api/hooks/useFinancialOverview'
import { formatCurrency } from './utils'

interface FinancialDataCardsProps {
  data: MonthlyFinancialDataDto[]
  includeStockData: boolean
}

interface LabeledRowProps {
  label: string
  value: string
  valueClassName?: string
}

const LabeledRow: React.FC<LabeledRowProps> = ({ label, value, valueClassName = 'text-gray-900' }) => (
  <div className="flex justify-between py-1">
    <span className="text-sm text-gray-500">{label}</span>
    <span className={`text-sm font-medium ${valueClassName}`}>{value}</span>
  </div>
)

export const FinancialDataCards: React.FC<FinancialDataCardsProps> = ({ data, includeStockData }) => (
  <div className="space-y-3">
    {data.map((item) => (
      <div
        key={`${item.year}-${item.month}`}
        className="bg-white shadow rounded-lg px-4 py-3"
      >
        <h4 className="text-sm font-semibold text-gray-900 mb-2 border-b border-gray-100 pb-1">
          {item.monthYearDisplay}
        </h4>
        <LabeledRow label="Příjmy" value={formatCurrency(item.income)} />
        <LabeledRow label="Náklady" value={formatCurrency(item.expenses)} />
        <LabeledRow
          label="Účetní bilance"
          value={formatCurrency(item.financialBalance)}
          valueClassName={item.financialBalance >= 0 ? 'text-emerald-600' : 'text-red-600'}
        />
        {includeStockData && (
          <>
            <LabeledRow
              label="Změna skladu"
              value={formatCurrency(item.totalStockValueChange || 0)}
              valueClassName={
                (item.totalStockValueChange || 0) >= 0 ? 'text-purple-600' : 'text-orange-600'
              }
            />
            <LabeledRow
              label="Celková bilance"
              value={formatCurrency(item.totalBalance || item.financialBalance)}
              valueClassName={
                (item.totalBalance || item.financialBalance) >= 0
                  ? 'text-emerald-600'
                  : 'text-red-600'
              }
            />
          </>
        )}
      </div>
    ))}
  </div>
)
