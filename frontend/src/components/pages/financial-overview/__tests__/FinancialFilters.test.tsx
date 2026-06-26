import React from 'react'
import { render, screen, fireEvent } from '@testing-library/react'
import { FinancialFilters } from '../FinancialFilters'
import type { Department } from '../../../../api/hooks/useDepartments'

jest.mock('../../../../hooks/useMediaQuery', () => ({
  useIsMobile: jest.fn(),
}))

const { useIsMobile } = jest.requireMock('../../../../hooks/useMediaQuery') as {
  useIsMobile: jest.Mock
}

const mockDepartments: Department[] = [
  { id: 'dept-1', name: 'Výroba' },
  { id: 'dept-2', name: 'Buvol' },
]

const defaultProps = {
  selectedPeriod: 'last-6-months' as const,
  includeStockData: true,
  includeCurrentMonth: false,
  excludedDepartments: [],
  departments: mockDepartments,
  isRefetching: false,
  onPeriodChange: jest.fn(),
  onIncludeStockDataChange: jest.fn(),
  onIncludeCurrentMonthChange: jest.fn(),
  onExcludedDepartmentsChange: jest.fn(),
}

describe('FinancialFilters — mobile', () => {
  beforeEach(() => {
    jest.clearAllMocks()
    useIsMobile.mockReturnValue(true)
  })

  it('shows the toggle button with "Filtry & období" text', () => {
    render(<FinancialFilters {...defaultProps} />)
    expect(screen.getByText(/Filtry & období/)).toBeInTheDocument()
  })

  it('does not show the period select by default (collapsed)', () => {
    render(<FinancialFilters {...defaultProps} />)
    expect(screen.queryByLabelText('Časové období:')).not.toBeInTheDocument()
  })

  it('expands the panel when toggle button is clicked', () => {
    render(<FinancialFilters {...defaultProps} />)
    fireEvent.click(screen.getByText(/Filtry & období/))
    expect(screen.getByLabelText('Časové období:')).toBeInTheDocument()
  })

  it('collapses the panel on second click', () => {
    render(<FinancialFilters {...defaultProps} />)
    const btn = screen.getByText(/Filtry & období/).closest('button')!
    fireEvent.click(btn)
    fireEvent.click(btn)
    expect(screen.queryByLabelText('Časové období:')).not.toBeInTheDocument()
  })

  it('fires onPeriodChange when period is changed after expanding', () => {
    render(<FinancialFilters {...defaultProps} />)
    fireEvent.click(screen.getByText(/Filtry & období/))
    fireEvent.change(screen.getByLabelText('Časové období:'), {
      target: { value: 'last-13-months' },
    })
    expect(defaultProps.onPeriodChange).toHaveBeenCalledWith('last-13-months')
  })
})

describe('FinancialFilters — desktop', () => {
  beforeEach(() => {
    jest.clearAllMocks()
    useIsMobile.mockReturnValue(false)
  })

  it('renders the period select inline without a toggle button', () => {
    render(<FinancialFilters {...defaultProps} />)
    expect(screen.getByLabelText('Časové období:')).toBeInTheDocument()
    expect(screen.queryByText(/Filtry & období/)).not.toBeInTheDocument()
  })

  it('fires onIncludeStockDataChange when stock checkbox is toggled', () => {
    render(<FinancialFilters {...defaultProps} includeStockData={true} />)
    fireEvent.click(screen.getByLabelText(/Zahrnout skladová data/))
    expect(defaultProps.onIncludeStockDataChange).toHaveBeenCalledWith(false)
  })

  it('fires onIncludeCurrentMonthChange when current-month checkbox is toggled', () => {
    render(<FinancialFilters {...defaultProps} includeCurrentMonth={false} />)
    fireEvent.click(screen.getByLabelText(/Zobrazit aktuální měsíc/))
    expect(defaultProps.onIncludeCurrentMonthChange).toHaveBeenCalledWith(true)
  })

  it('shows refetching spinner when isRefetching is true', () => {
    render(<FinancialFilters {...defaultProps} isRefetching={true} />)
    expect(screen.getByText('Aktualizuji data...')).toBeInTheDocument()
  })

  it('renders department checkboxes', () => {
    render(<FinancialFilters {...defaultProps} />)
    expect(screen.getByLabelText('Výroba')).toBeInTheDocument()
    expect(screen.getByLabelText('Buvol')).toBeInTheDocument()
  })

  it('fires onExcludedDepartmentsChange when a department is unchecked', () => {
    render(<FinancialFilters {...defaultProps} excludedDepartments={[]} />)
    fireEvent.click(screen.getByLabelText('Buvol'))
    expect(defaultProps.onExcludedDepartmentsChange).toHaveBeenCalledWith(['dept-2'])
  })

  it('fires onExcludedDepartmentsChange when an excluded department is re-checked', () => {
    render(<FinancialFilters {...defaultProps} excludedDepartments={['dept-2']} />)
    fireEvent.click(screen.getByLabelText('Buvol'))
    expect(defaultProps.onExcludedDepartmentsChange).toHaveBeenCalledWith([])
  })
})
