import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { PerformanceToolbar } from '../PerformanceToolbar'
import { METRIC_DESCRIPTIONS, VIEW_MODE_DESCRIPTIONS } from '../metrics'

const defaults = {
  viewMode: 'comparison' as const,
  period: 12 as const,
  years: 2,
  metric: 'pno' as const,
  includeWholesale: false,
  hideCurrentMonth: true,
  canRecompute: true,
  lastRefreshAt: '2026-09-22T10:12:00Z',
  hasWarnings: false,
  isRefetching: false,
  onViewModeChange: jest.fn(),
  onPeriodChange: jest.fn(),
  onYearsChange: jest.fn(),
  onMetricChange: jest.fn(),
  onIncludeWholesaleChange: jest.fn(),
  onHideCurrentMonthChange: jest.fn(),
  onRecomputeClick: jest.fn(),
}

const renderToolbar = (overrides: Partial<typeof defaults> = {}) =>
  render(<PerformanceToolbar {...defaults} {...overrides} />)

describe('PerformanceToolbar — hide the running month', () => {
  it('reflects the current state in the checkbox', () => {
    renderToolbar({ hideCurrentMonth: true })

    expect(screen.getByLabelText('skrýt probíhající měsíc')).toBeChecked()
  })

  it('reports unticking the checkbox', () => {
    // Arrange
    const onHideCurrentMonthChange = jest.fn()
    renderToolbar({ hideCurrentMonth: true, onHideCurrentMonthChange })

    // Act
    fireEvent.click(screen.getByLabelText('skrýt probíhající měsíc'))

    // Assert
    expect(onHideCurrentMonthChange).toHaveBeenCalledWith(false)
  })

  it('reports ticking it back on', () => {
    const onHideCurrentMonthChange = jest.fn()
    renderToolbar({ hideCurrentMonth: false, onHideCurrentMonthChange })

    fireEvent.click(screen.getByLabelText('skrýt probíhající měsíc'))

    expect(onHideCurrentMonthChange).toHaveBeenCalledWith(true)
  })
})

describe('PerformanceToolbar — explanatory tooltips', () => {
  it('explains the selected metric', () => {
    renderToolbar({ metric: 'roas' })

    expect(screen.getByText(METRIC_DESCRIPTIONS.roas)).toBeInTheDocument()
  })

  it('follows the metric selection rather than showing a fixed text', () => {
    const { rerender } = renderToolbar({ metric: 'pno' })
    expect(screen.getByText(METRIC_DESCRIPTIONS.pno)).toBeInTheDocument()

    rerender(<PerformanceToolbar {...defaults} metric="costPerOrder" />)

    expect(screen.queryByText(METRIC_DESCRIPTIONS.pno)).not.toBeInTheDocument()
    expect(screen.getByText(METRIC_DESCRIPTIONS.costPerOrder)).toBeInTheDocument()
  })

  it('explains the selected view mode', () => {
    renderToolbar({ viewMode: 'trend' })

    expect(screen.getByText(VIEW_MODE_DESCRIPTIONS.trend)).toBeInTheDocument()
  })

  it('points each trigger at its own tooltip, so screen readers announce it', () => {
    renderToolbar({ metric: 'pno' })

    const trigger = screen.getByRole('button', { name: 'Co znamená tato metrika' })
    const tooltip = screen.getByText(METRIC_DESCRIPTIONS.pno)

    expect(tooltip.id).toBeTruthy()
    expect(trigger).toHaveAttribute('aria-describedby', tooltip.id)
  })
})
