import React from 'react'
import { render, screen } from '@testing-library/react'
import { FinancialDataTable } from '../FinancialDataTable'
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

describe('FinancialDataTable', () => {
  it('renders month display name for each row', () => {
    const data = [
      makeRow({ monthYearDisplay: 'Leden 2024' }),
      makeRow({ month: 2, monthYearDisplay: 'Únor 2024' }),
    ]
    render(<FinancialDataTable data={data} includeStockData={false} />)
    expect(screen.getByText('Leden 2024')).toBeInTheDocument()
    expect(screen.getByText('Únor 2024')).toBeInTheDocument()
  })

  it('shows stock columns when includeStockData is true', () => {
    render(<FinancialDataTable data={[makeRow()]} includeStockData={true} />)
    expect(screen.getByText('Změna skladu')).toBeInTheDocument()
    expect(screen.getByText('Celková bilance')).toBeInTheDocument()
  })

  it('hides stock columns when includeStockData is false', () => {
    render(<FinancialDataTable data={[makeRow()]} includeStockData={false} />)
    expect(screen.queryByText('Změna skladu')).not.toBeInTheDocument()
    expect(screen.queryByText('Celková bilance')).not.toBeInTheDocument()
  })

  it('renders income, expenses, and balance columns always', () => {
    render(<FinancialDataTable data={[makeRow()]} includeStockData={false} />)
    expect(screen.getByText('Příjmy')).toBeInTheDocument()
    expect(screen.getByText('Náklady')).toBeInTheDocument()
    expect(screen.getByText('Účetní bilance')).toBeInTheDocument()
  })

  it('applies red color class for negative financialBalance', () => {
    const { container } = render(
      <FinancialDataTable data={[makeRow({ financialBalance: -1000 })]} includeStockData={false} />
    )
    const balanceCell = container.querySelector('.text-red-600')
    expect(balanceCell).toBeInTheDocument()
  })

  it('applies green color class for positive financialBalance', () => {
    const { container } = render(
      <FinancialDataTable data={[makeRow({ financialBalance: 1000 })]} includeStockData={false} />
    )
    const balanceCell = container.querySelector('.text-emerald-600')
    expect(balanceCell).toBeInTheDocument()
  })
})
