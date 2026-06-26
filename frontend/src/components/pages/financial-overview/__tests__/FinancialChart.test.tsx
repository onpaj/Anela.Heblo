import React from 'react'
import { render, screen } from '@testing-library/react'
import { FinancialChart } from '../FinancialChart'
import { MONTH_SLOT_WIDTH } from '../utils'
import type { ChartOptions } from 'chart.js'

jest.mock('../../../../hooks/useMediaQuery', () => ({
  useIsMobile: jest.fn(),
}))

jest.mock('react-chartjs-2', () => ({
  Chart: () => <canvas data-testid="chart-canvas" />,
}))

const { useIsMobile } = jest.requireMock('../../../../hooks/useMediaQuery') as {
  useIsMobile: jest.Mock
}

const mockChartData = {
  labels: ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec', 'Jan 2'],
  datasets: [],
}

const mockOptions = {} as ChartOptions<'bar'>

describe('FinancialChart', () => {
  beforeEach(() => {
    jest.clearAllMocks()
  })

  it('renders the title', () => {
    useIsMobile.mockReturnValue(false)
    render(
      <FinancialChart chartData={mockChartData} chartOptions={mockOptions} title="Finanční přehled - Aktuální rok" />
    )
    expect(screen.getByText('Finanční přehled - Aktuální rok')).toBeInTheDocument()
  })

  it('renders the chart element', () => {
    useIsMobile.mockReturnValue(false)
    render(
      <FinancialChart chartData={mockChartData} chartOptions={mockOptions} title="Test" />
    )
    expect(screen.getByTestId('chart-canvas')).toBeInTheDocument()
  })

  it('sets minWidth on inner wrapper proportional to month count on mobile', () => {
    useIsMobile.mockReturnValue(true)
    const { getByTestId } = render(
      <FinancialChart chartData={mockChartData} chartOptions={mockOptions} title="Test" />
    )
    const inner = getByTestId('chart-inner')
    expect(inner.style.minWidth).toBe(`${mockChartData.labels.length * MONTH_SLOT_WIDTH}px`)
  })

  it('sets no minWidth on inner wrapper on desktop', () => {
    useIsMobile.mockReturnValue(false)
    const { getByTestId } = render(
      <FinancialChart chartData={mockChartData} chartOptions={mockOptions} title="Test" />
    )
    const inner = getByTestId('chart-inner')
    expect(inner.style.minWidth).toBe('')
  })
})
