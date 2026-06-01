import React from 'react'
import { Chart } from 'react-chartjs-2'
import type { ChartOptions, ChartData } from 'chart.js'
import { useIsMobile } from '../../../hooks/useMediaQuery'
import { MONTH_SLOT_WIDTH } from './utils'

interface FinancialChartProps {
  chartData: ChartData<'bar'>
  chartOptions: ChartOptions<'bar'>
  title: string
}

export const FinancialChart: React.FC<FinancialChartProps> = ({ chartData, chartOptions, title }) => {
  const isMobile = useIsMobile()
  const monthCount = chartData.labels?.length ?? 0
  const innerMinWidth = isMobile ? monthCount * MONTH_SLOT_WIDTH : undefined

  return (
    <div className="bg-white shadow rounded-lg mb-8">
      <div className="px-4 sm:px-6 pt-6 pb-2">
        <h3 className="text-lg font-medium text-gray-900">{title}</h3>
      </div>
      <div className="relative w-full px-2 sm:px-4 lg:px-6 pb-6 overflow-x-auto">
        <div
          className="h-[350px] sm:h-[400px] lg:h-[450px]"
          data-testid="chart-inner"
          style={innerMinWidth !== undefined ? { minWidth: `${innerMinWidth}px` } : undefined}
        >
          <Chart type="bar" data={chartData} options={chartOptions} />
        </div>
      </div>
    </div>
  )
}
