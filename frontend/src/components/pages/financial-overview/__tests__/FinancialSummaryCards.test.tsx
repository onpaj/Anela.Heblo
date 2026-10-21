import React from 'react'
import { render, screen } from '@testing-library/react'
import { FinancialSummaryCards } from '../FinancialSummaryCards'
import { formatCurrency } from '../utils'
import type { FinancialSummaryDto } from '../../../../api/hooks/useFinancialOverview'

const makeSummary = (overrides: Record<string, unknown> = {}): FinancialSummaryDto =>
  ({
    totalIncome: 500000,
    totalExpenses: 300000,
    totalBalance: 200000,
    averageMonthlyBalance: 33000,
    stockSummary: {
      totalStockValueChange: 15000,
      totalBalanceWithStock: 215000,
    },
    ...overrides,
  }) as unknown as FinancialSummaryDto

const valueClassOf = (label: string) =>
  screen.getByText(label).parentElement!.querySelector('dd')!.className

describe('FinancialSummaryCards', () => {
  it('renders the four base cards', () => {
    render(<FinancialSummaryCards summary={makeSummary()} includeStockData={false} />)
    expect(screen.getByText('Celkové příjmy')).toBeInTheDocument()
    expect(screen.getByText('Celkové náklady')).toBeInTheDocument()
    expect(screen.getByText('Účetní bilance')).toBeInTheDocument()
    expect(screen.getByText('Průměrná měsíční bilance')).toBeInTheDocument()
  })

  it('shows stock cards when includeStockData and stockSummary present', () => {
    render(<FinancialSummaryCards summary={makeSummary()} includeStockData={true} />)
    expect(screen.getByText('Změna hodnoty skladu')).toBeInTheDocument()
    expect(screen.getByText('Celková bilance vč. skladu')).toBeInTheDocument()
  })

  it('hides stock cards when includeStockData is false', () => {
    render(<FinancialSummaryCards summary={makeSummary()} includeStockData={false} />)
    expect(screen.queryByText('Změna hodnoty skladu')).not.toBeInTheDocument()
    expect(screen.queryByText('Celková bilance vč. skladu')).not.toBeInTheDocument()
  })

  it('hides stock cards when stockSummary is undefined', () => {
    render(
      <FinancialSummaryCards
        summary={makeSummary({ stockSummary: undefined })}
        includeStockData={true}
      />,
    )
    expect(screen.queryByText('Změna hodnoty skladu')).not.toBeInTheDocument()
  })

  it('colours positive balance emerald and negative balance red', () => {
    const { unmount } = render(
      <FinancialSummaryCards summary={makeSummary()} includeStockData={false} />,
    )
    expect(valueClassOf('Účetní bilance')).toContain('text-emerald-600')
    unmount()
    render(
      <FinancialSummaryCards
        summary={makeSummary({ totalBalance: -5000 })}
        includeStockData={false}
      />,
    )
    expect(valueClassOf('Účetní bilance')).toContain('text-red-600')
  })

  it('colours positive average balance blue', () => {
    render(<FinancialSummaryCards summary={makeSummary()} includeStockData={false} />)
    expect(valueClassOf('Průměrná měsíční bilance')).toContain('text-blue-600')
  })

  it('uses orange and renders 0 for negative, zero-less or undefined stock change', () => {
    const { unmount } = render(
      <FinancialSummaryCards
        summary={makeSummary({
          stockSummary: { totalStockValueChange: -100, totalBalanceWithStock: 1 },
        })}
        includeStockData={true}
      />,
    )
    expect(valueClassOf('Změna hodnoty skladu')).toContain('text-orange-600')
    unmount()
    render(
      <FinancialSummaryCards
        summary={makeSummary({
          stockSummary: { totalStockValueChange: undefined, totalBalanceWithStock: 1 },
        })}
        includeStockData={true}
      />,
    )
    expect(valueClassOf('Změna hodnoty skladu')).toContain('text-orange-600')
    expect(screen.getByText('Změna hodnoty skladu').parentElement!.querySelector('dd')!.textContent).toBe(
      formatCurrency(0),
    )
  })

  it('uses 6-column grid with stock and 4-column grid without', () => {
    const { container, rerender } = render(
      <FinancialSummaryCards summary={makeSummary()} includeStockData={true} />,
    )
    expect((container.firstChild as HTMLElement).className).toContain('xl:grid-cols-6')
    rerender(<FinancialSummaryCards summary={makeSummary()} includeStockData={false} />)
    expect((container.firstChild as HTMLElement).className).toContain('lg:grid-cols-4')
  })
})
