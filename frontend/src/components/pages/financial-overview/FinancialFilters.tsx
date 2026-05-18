import React, { useState } from 'react'
import { SlidersHorizontal, ChevronDown, ChevronUp, Package, Calendar } from 'lucide-react'
import { useIsMobile } from '../../../hooks/useMediaQuery'
import type { Department } from '../../../api/hooks/useDepartments'
import { getPeriodLabel, type PeriodType } from './utils'

interface FinancialFiltersProps {
  selectedPeriod: PeriodType
  includeStockData: boolean
  includeCurrentMonth: boolean
  excludedDepartments: string[]
  departments: Department[] | undefined
  isRefetching: boolean
  onPeriodChange: (period: PeriodType) => void
  onIncludeStockDataChange: (value: boolean) => void
  onIncludeCurrentMonthChange: (value: boolean) => void
  onExcludedDepartmentsChange: (departments: string[]) => void
}

export const FinancialFilters: React.FC<FinancialFiltersProps> = ({
  selectedPeriod,
  includeStockData,
  includeCurrentMonth,
  excludedDepartments,
  departments,
  isRefetching,
  onPeriodChange,
  onIncludeStockDataChange,
  onIncludeCurrentMonthChange,
  onExcludedDepartmentsChange,
}) => {
  const isMobile = useIsMobile()
  const [isExpanded, setIsExpanded] = useState(false)

  const handleDepartmentChange = (id: string, checked: boolean) => {
    if (checked) {
      onExcludedDepartmentsChange(excludedDepartments.filter((d) => d !== id))
    } else {
      onExcludedDepartmentsChange([...excludedDepartments, id])
    }
  }

  const controlsBlock = (
    <div className="flex flex-col sm:flex-row gap-4">
      <div>
        <label
          htmlFor="period-select"
          className="block text-sm font-medium text-gray-700 mb-2"
        >
          Časové období:
        </label>
        <select
          id="period-select"
          value={selectedPeriod}
          onChange={(e) => onPeriodChange(e.target.value as PeriodType)}
          className="block w-60 pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"
        >
          <option value="current-year">Aktuální rok</option>
          <option value="current-and-previous-year">Aktuální + předchozí rok</option>
          <option value="last-6-months">Posledních 6 měsíců</option>
          <option value="last-13-months">Posledních 13 měsíců</option>
          <option value="last-26-months">Posledních 26 měsíců</option>
        </select>
      </div>

      <div>
        <label className="block text-sm font-medium text-gray-700 mb-2">
          Zobrazení dat:
        </label>
        <div className="flex flex-col gap-1">
          <div className="flex items-center">
            <input
              id="stock-toggle"
              type="checkbox"
              checked={includeStockData}
              onChange={(e) => onIncludeStockDataChange(e.target.checked)}
              className="h-4 w-4 text-indigo-600 focus:ring-indigo-500 border-gray-300 rounded"
            />
            <label
              htmlFor="stock-toggle"
              className="ml-2 block text-sm text-gray-900 flex items-center"
            >
              <Package className="w-4 h-4 mr-1" />
              Zahrnout skladová data
            </label>
          </div>
          <div className="flex items-center">
            <input
              id="current-month-toggle"
              type="checkbox"
              checked={includeCurrentMonth}
              onChange={(e) => onIncludeCurrentMonthChange(e.target.checked)}
              className="h-4 w-4 text-indigo-600 focus:ring-indigo-500 border-gray-300 rounded"
            />
            <label
              htmlFor="current-month-toggle"
              className="ml-2 block text-sm text-gray-900 flex items-center"
            >
              <Calendar className="w-4 h-4 mr-1" />
              Zobrazit aktuální měsíc
            </label>
          </div>
        </div>
      </div>

      {departments && departments.length > 0 && (
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-2">
            Střediska:
          </label>
          <div className="flex flex-wrap gap-x-4 gap-y-1">
            {departments.map((department) => (
              <div key={department.id} className="flex items-center">
                <input
                  id={`dept-${department.id}`}
                  type="checkbox"
                  checked={!excludedDepartments.includes(department.id)}
                  onChange={(e) => handleDepartmentChange(department.id, e.target.checked)}
                  className="h-4 w-4 text-indigo-600 focus:ring-indigo-500 border-gray-300 rounded"
                />
                <label
                  htmlFor={`dept-${department.id}`}
                  className="ml-2 block text-sm text-gray-900"
                >
                  {department.name}
                </label>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  )

  if (isMobile) {
    return (
      <div className="mb-6">
        <button
          type="button"
          onClick={() => setIsExpanded((prev) => !prev)}
          className="w-full flex items-center justify-between px-4 py-2 bg-white shadow rounded-lg text-left"
        >
          <span className="flex items-center gap-2 text-sm font-medium text-gray-700">
            <SlidersHorizontal className="w-4 h-4" />
            Filtry & období — {getPeriodLabel(selectedPeriod)}
          </span>
          {isExpanded ? (
            <ChevronUp className="w-4 h-4 text-gray-500" />
          ) : (
            <ChevronDown className="w-4 h-4 text-gray-500" />
          )}
        </button>
        {isExpanded && (
          <div className="mt-3 p-4 bg-white shadow rounded-lg">
            {controlsBlock}
          </div>
        )}
        {isRefetching && (
          <div className="flex items-center text-sm text-gray-500 mt-2">
            <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-indigo-600 mr-2" />
            Aktualizuji data...
          </div>
        )}
      </div>
    )
  }

  return (
    <div className="mb-6 flex flex-col lg:flex-row lg:items-end lg:justify-between gap-4">
      {controlsBlock}
      {isRefetching && (
        <div className="flex items-center text-sm text-gray-500">
          <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-indigo-600 mr-2" />
          Aktualizuji data...
        </div>
      )}
    </div>
  )
}
