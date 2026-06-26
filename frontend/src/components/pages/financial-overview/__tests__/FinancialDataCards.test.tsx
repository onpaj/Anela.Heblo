import React from 'react'
import { render, screen } from '@testing-library/react'
import { FinancialDataCards } from '../FinancialDataCards'
import type { MonthlyFinancialDataDto } from '../../../../api/hooks/useFinancialOverview'

const makeRow = (overrides: Partial<MonthlyFinancialDataDto> = {}): MonthlyFinancialDataDto => ({
  year: 2024,
  month: 1,
  monthYearDisplay: 'Leden 2024',
  income: 100000,
  expenses: 80000,
  financialBalance: 20000,
  totalStockValueChange: 5000,
  totalBalance: 25000,
  ...overrides,
})

describe('FinancialDataCards', () => {
  it('renders one card heading per month', () => {
    const data = [
      makeRow({ monthYearDisplay: 'Leden 2024' }),
      makeRow({ month: 2, monthYearDisplay: 'Únor 2024' }),
    ]
    render(<FinancialDataCards data={data} includeStockData={false} />)
    expect(screen.getByText('Leden 2024')).toBeInTheDocument()
    expect(screen.getByText('Únor 2024')).toBeInTheDocument()
  })

  it('always shows Příjmy, Náklady, Účetní bilance labels', () => {
    render(<FinancialDataCards data={[makeRow()]} includeStockData={false} />)
    expect(screen.getByText('Příjmy')).toBeInTheDocument()
    expect(screen.getByText('Náklady')).toBeInTheDocument()
    expect(screen.getByText('Účetní bilance')).toBeInTheDocument()
  })

  it('shows stock rows when includeStockData is true', () => {
    render(<FinancialDataCards data={[makeRow()]} includeStockData={true} />)
    expect(screen.getByText('Změna skladu')).toBeInTheDocument()
    expect(screen.getByText('Celková bilance')).toBeInTheDocument()
  })

  it('hides stock rows when includeStockData is false', () => {
    render(<FinancialDataCards data={[makeRow()]} includeStockData={false} />)
    expect(screen.queryByText('Změna skladu')).not.toBeInTheDocument()
    expect(screen.queryByText('Celková bilance')).not.toBeInTheDocument()
  })

  it('applies red color for negative financialBalance', () => {
    const { container } = render(
      <FinancialDataCards data={[makeRow({ financialBalance: -500 })]} includeStockData={false} />
    )
    expect(container.querySelector('.text-red-600')).toBeInTheDocument()
  })

  it('applies green color for positive financialBalance', () => {
    const { container } = render(
      <FinancialDataCards data={[makeRow({ financialBalance: 500 })]} includeStockData={false} />
    )
    expect(container.querySelector('.text-emerald-600')).toBeInTheDocument()
  })

  it('renders nothing for empty data array', () => {
    const { container } = render(<FinancialDataCards data={[]} includeStockData={false} />)
    expect(container.firstChild).toBeEmptyDOMElement()
  })
})
