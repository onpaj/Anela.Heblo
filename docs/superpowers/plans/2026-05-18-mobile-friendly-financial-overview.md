# Mobile-Friendly Financial Overview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Financial Overview page (`/finance/overview`) genuinely usable on mobile (375px) by extracting render blocks into focused child components with responsive behaviour, leaving the desktop layout and data flow byte-for-byte unchanged.

**Architecture:** Extract the 700-line `FinancialOverview.tsx` into 5 child components + a `utils.ts` shared file. `FinancialOverview` keeps all state and data fetching; child components receive data through props. Mobile layout is driven by the existing `useIsMobile()` hook (`max-width: 767px`) and Tailwind `md:` classes. KPI grid becomes 2-column via a CSS class change only.

**Tech Stack:** React 18, TypeScript, Tailwind CSS, Chart.js / react-chartjs-2, Lucide React, Jest + React Testing Library (unit), Playwright (E2E).

---

## File layout

**New files**
| Path | Responsibility |
|------|----------------|
| `frontend/src/components/pages/financial-overview/utils.ts` | `PeriodType`, `formatCurrency`, `getPeriodLabel`, `MONTH_SLOT_WIDTH` |
| `frontend/src/components/pages/financial-overview/FinancialFilters.tsx` | Period select + checkboxes; collapsible on mobile |
| `frontend/src/components/pages/financial-overview/FinancialChart.tsx` | Chart card; horizontally scrollable on mobile |
| `frontend/src/components/pages/financial-overview/FinancialDataTable.tsx` | Existing monthly data `<table>` |
| `frontend/src/components/pages/financial-overview/FinancialDataCards.tsx` | Card-per-month layout for mobile |
| `frontend/src/components/pages/financial-overview/__tests__/utils.test.ts` | Unit tests for utils |
| `frontend/src/components/pages/financial-overview/__tests__/FinancialFilters.test.tsx` | Unit tests for FinancialFilters |
| `frontend/src/components/pages/financial-overview/__tests__/FinancialChart.test.tsx` | Unit tests for FinancialChart |
| `frontend/src/components/pages/financial-overview/__tests__/FinancialDataTable.test.tsx` | Unit tests for FinancialDataTable |
| `frontend/src/components/pages/financial-overview/__tests__/FinancialDataCards.test.tsx` | Unit tests for FinancialDataCards |
| `frontend/test/e2e/finance/financial-overview-mobile.spec.ts` | Playwright mobile-viewport E2E test |

**Modified files**
| Path | Change |
|------|--------|
| `frontend/src/components/pages/FinancialOverview.tsx` | Import child components; import shared utils; remove `windowWidth` state; KPI grid class; replace controls/chart/table blocks |
| `frontend/playwright.config.ts` | Add `finance` project pointing to `./test/e2e/finance` |

---

## Task 1: Create `utils.ts` with shared types and helpers

**Files:**
- Create: `frontend/src/components/pages/financial-overview/utils.ts`
- Create: `frontend/src/components/pages/financial-overview/__tests__/utils.test.ts`

- [ ] **Step 1.1: Write the failing test**

Create `frontend/src/components/pages/financial-overview/__tests__/utils.test.ts`:

```typescript
import { formatCurrency, getPeriodLabel, MONTH_SLOT_WIDTH } from '../utils'

describe('formatCurrency', () => {
  it('returns a string containing Kč', () => {
    expect(formatCurrency(1000)).toMatch(/Kč/)
  })

  it('formats zero', () => {
    expect(formatCurrency(0)).toMatch(/Kč/)
  })

  it('formats negative amount', () => {
    expect(formatCurrency(-5000)).toMatch(/Kč/)
  })
})

describe('getPeriodLabel', () => {
  it('returns label for current-year', () => {
    expect(getPeriodLabel('current-year')).toBe('Aktuální rok')
  })

  it('returns label for current-and-previous-year', () => {
    expect(getPeriodLabel('current-and-previous-year')).toBe('Aktuální + předchozí rok')
  })

  it('returns label for last-6-months', () => {
    expect(getPeriodLabel('last-6-months')).toBe('Posledních 6 měsíců')
  })

  it('returns label for last-13-months', () => {
    expect(getPeriodLabel('last-13-months')).toBe('Posledních 13 měsíců')
  })

  it('returns label for last-26-months', () => {
    expect(getPeriodLabel('last-26-months')).toBe('Posledních 26 měsíců')
  })
})

describe('MONTH_SLOT_WIDTH', () => {
  it('equals 48', () => {
    expect(MONTH_SLOT_WIDTH).toBe(48)
  })
})
```

- [ ] **Step 1.2: Run test to verify it fails**

```bash
cd frontend && npm test -- --testPathPattern="financial-overview/__tests__/utils" --watchAll=false
```

Expected: compilation error — `utils` does not exist yet.

- [ ] **Step 1.3: Create `utils.ts`**

Create `frontend/src/components/pages/financial-overview/utils.ts`:

```typescript
export type PeriodType =
  | 'current-year'
  | 'current-and-previous-year'
  | 'last-6-months'
  | 'last-13-months'
  | 'last-26-months'

export const MONTH_SLOT_WIDTH = 48

export const formatCurrency = (amount: number): string =>
  new Intl.NumberFormat('cs-CZ', {
    style: 'currency',
    currency: 'CZK',
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
  }).format(amount)

export const getPeriodLabel = (period: PeriodType): string => {
  switch (period) {
    case 'current-year':
      return 'Aktuální rok'
    case 'current-and-previous-year':
      return 'Aktuální + předchozí rok'
    case 'last-6-months':
      return 'Posledních 6 měsíců'
    case 'last-13-months':
      return 'Posledních 13 měsíců'
    case 'last-26-months':
      return 'Posledních 26 měsíců'
    default:
      return 'Posledních 6 měsíců'
  }
}
```

- [ ] **Step 1.4: Run test to verify it passes**

```bash
cd frontend && npm test -- --testPathPattern="financial-overview/__tests__/utils" --watchAll=false
```

Expected: all 7 tests PASS.

- [ ] **Step 1.5: Commit**

```bash
git add frontend/src/components/pages/financial-overview/utils.ts \
        frontend/src/components/pages/financial-overview/__tests__/utils.test.ts
git commit -m "feat(finance): add shared utils (formatCurrency, getPeriodLabel, MONTH_SLOT_WIDTH)"
```

---

## Task 2: Create `FinancialDataTable.tsx`

Extract the existing monthly `<table>` into a standalone component. No behaviour change — pure extraction.

**Files:**
- Create: `frontend/src/components/pages/financial-overview/FinancialDataTable.tsx`
- Create: `frontend/src/components/pages/financial-overview/__tests__/FinancialDataTable.test.tsx`

- [ ] **Step 2.1: Write the failing tests**

Create `frontend/src/components/pages/financial-overview/__tests__/FinancialDataTable.test.tsx`:

```typescript
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
```

- [ ] **Step 2.2: Run test to verify it fails**

```bash
cd frontend && npm test -- --testPathPattern="financial-overview/__tests__/FinancialDataTable" --watchAll=false
```

Expected: compilation error — `FinancialDataTable` does not exist yet.

- [ ] **Step 2.3: Create `FinancialDataTable.tsx`**

Create `frontend/src/components/pages/financial-overview/FinancialDataTable.tsx`:

```typescript
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
    <table className="min-w-full divide-y divide-gray-200">
      <thead className="bg-gray-50 sticky top-0 z-10">
        <tr>
          <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
            Měsíc
          </th>
          <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
            Příjmy
          </th>
          <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
            Náklady
          </th>
          <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
            Účetní bilance
          </th>
          {includeStockData && (
            <>
              <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                Změna skladu
              </th>
              <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                Celková bilance
              </th>
            </>
          )}
        </tr>
      </thead>
      <tbody className="bg-white divide-y divide-gray-200">
        {data.map((item) => (
          <tr key={`${item.year}-${item.month}`} className="hover:bg-gray-50">
            <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
              {item.monthYearDisplay}
            </td>
            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-right">
              {formatCurrency(item.income)}
            </td>
            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 text-right">
              {formatCurrency(item.expenses)}
            </td>
            <td
              className={`px-6 py-4 whitespace-nowrap text-sm text-right font-medium ${
                item.financialBalance >= 0 ? 'text-emerald-600' : 'text-red-600'
              }`}
            >
              {formatCurrency(item.financialBalance)}
            </td>
            {includeStockData && (
              <>
                <td
                  className={`px-6 py-4 whitespace-nowrap text-sm text-right ${
                    (item.totalStockValueChange || 0) >= 0
                      ? 'text-purple-600'
                      : 'text-orange-600'
                  }`}
                >
                  {formatCurrency(item.totalStockValueChange || 0)}
                </td>
                <td
                  className={`px-6 py-4 whitespace-nowrap text-sm text-right font-medium ${
                    (item.totalBalance || item.financialBalance) >= 0
                      ? 'text-emerald-600'
                      : 'text-red-600'
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
```

- [ ] **Step 2.4: Run test to verify it passes**

```bash
cd frontend && npm test -- --testPathPattern="financial-overview/__tests__/FinancialDataTable" --watchAll=false
```

Expected: all 6 tests PASS.

- [ ] **Step 2.5: Commit**

```bash
git add frontend/src/components/pages/financial-overview/FinancialDataTable.tsx \
        frontend/src/components/pages/financial-overview/__tests__/FinancialDataTable.test.tsx
git commit -m "feat(finance): extract FinancialDataTable component"
```

---

## Task 3: Create `FinancialDataCards.tsx`

Mobile-only card layout: one card per month, same data as the table.

**Files:**
- Create: `frontend/src/components/pages/financial-overview/FinancialDataCards.tsx`
- Create: `frontend/src/components/pages/financial-overview/__tests__/FinancialDataCards.test.tsx`

- [ ] **Step 3.1: Write the failing tests**

Create `frontend/src/components/pages/financial-overview/__tests__/FinancialDataCards.test.tsx`:

```typescript
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
```

- [ ] **Step 3.2: Run test to verify it fails**

```bash
cd frontend && npm test -- --testPathPattern="financial-overview/__tests__/FinancialDataCards" --watchAll=false
```

Expected: compilation error — `FinancialDataCards` does not exist yet.

- [ ] **Step 3.3: Create `FinancialDataCards.tsx`**

Create `frontend/src/components/pages/financial-overview/FinancialDataCards.tsx`:

```typescript
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
```

- [ ] **Step 3.4: Run test to verify it passes**

```bash
cd frontend && npm test -- --testPathPattern="financial-overview/__tests__/FinancialDataCards" --watchAll=false
```

Expected: all 7 tests PASS.

- [ ] **Step 3.5: Commit**

```bash
git add frontend/src/components/pages/financial-overview/FinancialDataCards.tsx \
        frontend/src/components/pages/financial-overview/__tests__/FinancialDataCards.test.tsx
git commit -m "feat(finance): add FinancialDataCards mobile card layout"
```

---

## Task 4: Create `FinancialChart.tsx`

Chart card with horizontal scrolling on mobile. `minWidth` of the canvas wrapper is `monthCount * MONTH_SLOT_WIDTH` on mobile; unconstrained on desktop.

**Files:**
- Create: `frontend/src/components/pages/financial-overview/FinancialChart.tsx`
- Create: `frontend/src/components/pages/financial-overview/__tests__/FinancialChart.test.tsx`

- [ ] **Step 4.1: Write the failing tests**

Create `frontend/src/components/pages/financial-overview/__tests__/FinancialChart.test.tsx`:

```typescript
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
```

- [ ] **Step 4.2: Run test to verify it fails**

```bash
cd frontend && npm test -- --testPathPattern="financial-overview/__tests__/FinancialChart" --watchAll=false
```

Expected: compilation error — `FinancialChart` does not exist yet.

- [ ] **Step 4.3: Create `FinancialChart.tsx`**

Create `frontend/src/components/pages/financial-overview/FinancialChart.tsx`:

```typescript
import React from 'react'
import { Chart } from 'react-chartjs-2'
import type { ChartOptions } from 'chart.js'
import { useIsMobile } from '../../../hooks/useMediaQuery'
import { MONTH_SLOT_WIDTH } from './utils'

interface FinancialChartProps {
  chartData: { labels: string[]; datasets: any[] }
  chartOptions: ChartOptions<'bar'>
  title: string
}

export const FinancialChart: React.FC<FinancialChartProps> = ({ chartData, chartOptions, title }) => {
  const isMobile = useIsMobile()
  const monthCount = chartData.labels.length
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
```

- [ ] **Step 4.4: Run test to verify it passes**

```bash
cd frontend && npm test -- --testPathPattern="financial-overview/__tests__/FinancialChart" --watchAll=false
```

Expected: all 4 tests PASS.

- [ ] **Step 4.5: Commit**

```bash
git add frontend/src/components/pages/financial-overview/FinancialChart.tsx \
        frontend/src/components/pages/financial-overview/__tests__/FinancialChart.test.tsx
git commit -m "feat(finance): add FinancialChart with mobile horizontal scroll"
```

---

## Task 5: Create `FinancialFilters.tsx`

Collapsible filter panel on mobile; inline on desktop.

**Files:**
- Create: `frontend/src/components/pages/financial-overview/FinancialFilters.tsx`
- Create: `frontend/src/components/pages/financial-overview/__tests__/FinancialFilters.test.tsx`

- [ ] **Step 5.1: Write the failing tests**

Create `frontend/src/components/pages/financial-overview/__tests__/FinancialFilters.test.tsx`:

```typescript
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
    useIsMobile.mockReturnValue(true)
    jest.clearAllMocks()
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
    useIsMobile.mockReturnValue(false)
    jest.clearAllMocks()
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
```

- [ ] **Step 5.2: Run test to verify it fails**

```bash
cd frontend && npm test -- --testPathPattern="financial-overview/__tests__/FinancialFilters" --watchAll=false
```

Expected: compilation error — `FinancialFilters` does not exist yet.

- [ ] **Step 5.3: Create `FinancialFilters.tsx`**

Create `frontend/src/components/pages/financial-overview/FinancialFilters.tsx`:

```typescript
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
```

- [ ] **Step 5.4: Run test to verify it passes**

```bash
cd frontend && npm test -- --testPathPattern="financial-overview/__tests__/FinancialFilters" --watchAll=false
```

Expected: all 10 tests PASS.

- [ ] **Step 5.5: Commit**

```bash
git add frontend/src/components/pages/financial-overview/FinancialFilters.tsx \
        frontend/src/components/pages/financial-overview/__tests__/FinancialFilters.test.tsx
git commit -m "feat(finance): add FinancialFilters with collapsible mobile panel"
```

---

## Task 6: Refactor `FinancialOverview.tsx`

Wire up all child components. Remove the inline `windowWidth` tracking, `formatCurrency`, and `getPeriodLabel` (now in utils). Change the KPI grid to 2-column on mobile. Add the mobile data expander.

**Files:**
- Modify: `frontend/src/components/pages/FinancialOverview.tsx`

- [ ] **Step 6.1: Replace the file content**

Replace `frontend/src/components/pages/FinancialOverview.tsx` with:

```typescript
import React, { useState } from "react";
import { ChartOptions } from "chart.js";
import {
  TrendingUp,
  TrendingDown,
  DollarSign,
  AlertTriangle,
  Calendar,
  Package,
  BarChart3,
  ChevronDown,
  ChevronUp,
} from "lucide-react";
import { useFinancialOverviewQuery } from "../../api/hooks/useFinancialOverview";
import { useDepartments } from "../../api/hooks/useDepartments";
import { useIsMobile } from "../../hooks/useMediaQuery";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import {
  type PeriodType,
  formatCurrency,
  getPeriodLabel,
} from "./financial-overview/utils";
import { FinancialFilters } from "./financial-overview/FinancialFilters";
import { FinancialChart } from "./financial-overview/FinancialChart";
import { FinancialDataTable } from "./financial-overview/FinancialDataTable";
import { FinancialDataCards } from "./financial-overview/FinancialDataCards";

const FinancialOverview: React.FC = () => {
  const [selectedPeriod, setSelectedPeriod] =
    useState<PeriodType>("current-year");
  const [includeStockData, setIncludeStockData] = useState<boolean>(true);
  const [includeCurrentMonth, setIncludeCurrentMonth] = useState<boolean>(false);
  const [excludedDepartments, setExcludedDepartments] = useState<string[]>([]);
  const [isDataExpanded, setIsDataExpanded] = useState(false);
  const isMobile = useIsMobile();
  const initialDefaultsSet = React.useRef(false);

  const { data: departments } = useDepartments();

  React.useEffect(() => {
    if (departments && !initialDefaultsSet.current) {
      initialDefaultsSet.current = true;
      const buvol = departments.find((d) => d.name === "Buvol");
      if (buvol) {
        setExcludedDepartments([buvol.id]);
      }
    }
  }, [departments]);

  // Convert period type to months for API call.
  // For "current-year" periods, we count completed months only (now.getMonth() = 0-indexed).
  // The +1 for includeCurrentMonth is handled by the backend via the includeCurrentMonth flag,
  // but we still need to pass the correct total month count.
  const getMonthsFromPeriod = (period: PeriodType): number => {
    const now = new Date();
    const currentMonthOffset = includeCurrentMonth ? 1 : 0;
    switch (period) {
      case "current-year":
        return now.getMonth() + currentMonthOffset;
      case "current-and-previous-year":
        return now.getMonth() + currentMonthOffset + 12;
      case "last-6-months":
        return 6;
      case "last-13-months":
        return 13;
      case "last-26-months":
        return 26;
      default:
        return 6;
    }
  };

  const months = getMonthsFromPeriod(selectedPeriod);
  const { data, isLoading, error, isRefetching } = useFinancialOverviewQuery(
    months,
    includeStockData,
    excludedDepartments,
    includeCurrentMonth,
  );

  const chartData = React.useMemo(() => {
    if (!data?.data) return null;

    const sortedData = [...data.data].sort((a, b) => {
      if (a.year !== b.year) return a.year - b.year;
      return a.month - b.month;
    });

    const labels = sortedData.map((item) => item.monthYearDisplay);
    const incomeData = sortedData.map((item) => item.income);
    const expensesData = sortedData.map((item) => item.expenses);
    const balanceData = sortedData.map((item) => item.financialBalance);
    const stockChangeData = sortedData.map(
      (item) => item.totalStockValueChange || 0,
    );
    const totalBalanceData = sortedData.map(
      (item) => item.totalBalance || item.financialBalance,
    );

    const datasets: any[] = [
      {
        label: "Příjmy",
        type: "bar" as const,
        data: incomeData,
        backgroundColor: "rgba(34, 197, 94, 0.6)",
        borderColor: "rgb(34, 197, 94)",
        borderWidth: 1,
      },
      {
        label: "Náklady",
        type: "bar" as const,
        data: expensesData,
        backgroundColor: "rgba(239, 68, 68, 0.6)",
        borderColor: "rgb(239, 68, 68)",
        borderWidth: 1,
      },
      {
        label: "Účetní bilance",
        type: "line" as const,
        data: balanceData,
        borderColor: "rgb(59, 130, 246)",
        backgroundColor: "rgba(59, 130, 246, 0.1)",
        fill: false,
        tension: 0.1,
        borderWidth: 3,
      },
    ];

    if (includeStockData) {
      datasets.push(
        {
          label: "Změna hodnoty skladu",
          type: "bar" as const,
          data: stockChangeData,
          backgroundColor: "rgba(168, 85, 247, 0.6)",
          borderColor: "rgb(168, 85, 247)",
          borderWidth: 1,
        },
        {
          label: "Celková bilance (vč. skladu)",
          type: "line" as const,
          data: totalBalanceData,
          borderColor: "rgb(245, 158, 11)",
          backgroundColor: "rgba(245, 158, 11, 0.1)",
          fill: false,
          tension: 0.1,
          borderWidth: 4,
        },
      );
    }

    return { labels, datasets };
  }, [data?.data, includeStockData]);

  const chartOptions: ChartOptions<"bar"> = React.useMemo(
    () => ({
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: {
          position: isMobile ? ("top" as const) : ("right" as const),
          align: "center" as const,
          labels: {
            boxWidth: 12,
            padding: isMobile ? 5 : 8,
            font: { size: isMobile ? 10 : 11 },
          },
        },
        title: { display: false },
        tooltip: {
          callbacks: {
            label: function (context) {
              return `${context.dataset.label}: ${formatCurrency(context.parsed.y ?? 0)}`;
            },
          },
        },
      },
      scales: {
        y: {
          beginAtZero: false,
          ticks: {
            callback: function (value) {
              return formatCurrency(Number(value));
            },
          },
          grid: {
            color: function (context) {
              if (context.tick.value === 0) return "#374151";
              return "#e5e7eb";
            },
            lineWidth: function (context) {
              if (context.tick.value === 0) return 3;
              return 1;
            },
          },
        },
      },
      interaction: { intersect: false, mode: "index" },
    }),
    [isMobile],
  );

  if (isLoading) {
    return (
      <div className="w-full max-w-none px-4 sm:px-6 lg:px-8">
        <div className="flex items-center justify-center py-12">
          <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
          <span className="ml-2 text-gray-600">Načítám finanční data...</span>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="w-full max-w-none px-4 sm:px-6 lg:px-8">
        <div className="mb-8 p-4 bg-red-50 border border-red-200 rounded-lg">
          <div className="flex items-center">
            <AlertTriangle className="w-5 h-5 text-red-500 mr-2" />
            <h3 className="text-red-800 font-medium">
              Chyba při načítání finančních dat
            </h3>
          </div>
          <p className="mt-1 text-red-700 text-sm">
            {error.message || "Neznámá chyba"}
          </p>
        </div>
      </div>
    );
  }

  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900">Finanční přehled</h1>
        <p className="mt-1 text-gray-600">
          Přehled příjmů, nákladů a celkové bilance firmy
        </p>
      </div>

      <div className="flex-1 overflow-auto">
        <FinancialFilters
          selectedPeriod={selectedPeriod}
          includeStockData={includeStockData}
          includeCurrentMonth={includeCurrentMonth}
          excludedDepartments={excludedDepartments}
          departments={departments}
          isRefetching={isRefetching}
          onPeriodChange={setSelectedPeriod}
          onIncludeStockDataChange={setIncludeStockData}
          onIncludeCurrentMonthChange={setIncludeCurrentMonth}
          onExcludedDepartmentsChange={setExcludedDepartments}
        />

        {/* Summary Cards */}
        {data?.summary && (
          <div
            className={`grid grid-cols-2 md:grid-cols-2 ${includeStockData ? "xl:grid-cols-6" : "lg:grid-cols-4"} gap-4 mb-6`}
          >
            <div className="bg-white overflow-hidden shadow rounded-lg">
              <div className="p-3">
                <div className="flex items-center">
                  <div className="flex-shrink-0">
                    <TrendingUp className="h-5 w-5 text-emerald-400" />
                  </div>
                  <div className="ml-3 w-0 flex-1">
                    <dl>
                      <dt className="text-xs font-medium text-gray-500 truncate">
                        Celkové příjmy
                      </dt>
                      <dd className="text-sm font-medium text-gray-900">
                        {formatCurrency(data.summary.totalIncome)}
                      </dd>
                    </dl>
                  </div>
                </div>
              </div>
            </div>

            <div className="bg-white overflow-hidden shadow rounded-lg">
              <div className="p-3">
                <div className="flex items-center">
                  <div className="flex-shrink-0">
                    <TrendingDown className="h-5 w-5 text-red-400" />
                  </div>
                  <div className="ml-3 w-0 flex-1">
                    <dl>
                      <dt className="text-xs font-medium text-gray-500 truncate">
                        Celkové náklady
                      </dt>
                      <dd className="text-sm font-medium text-gray-900">
                        {formatCurrency(data.summary.totalExpenses)}
                      </dd>
                    </dl>
                  </div>
                </div>
              </div>
            </div>

            <div className="bg-white overflow-hidden shadow rounded-lg">
              <div className="p-3">
                <div className="flex items-center">
                  <div className="flex-shrink-0">
                    <DollarSign
                      className={`h-5 w-5 ${data.summary.totalBalance >= 0 ? "text-emerald-400" : "text-red-400"}`}
                    />
                  </div>
                  <div className="ml-3 w-0 flex-1">
                    <dl>
                      <dt className="text-xs font-medium text-gray-500 truncate">
                        Účetní bilance
                      </dt>
                      <dd
                        className={`text-sm font-medium ${data.summary.totalBalance >= 0 ? "text-emerald-600" : "text-red-600"}`}
                      >
                        {formatCurrency(data.summary.totalBalance)}
                      </dd>
                    </dl>
                  </div>
                </div>
              </div>
            </div>

            <div className="bg-white overflow-hidden shadow rounded-lg">
              <div className="p-3">
                <div className="flex items-center">
                  <div className="flex-shrink-0">
                    <Calendar
                      className={`h-5 w-5 ${data.summary.averageMonthlyBalance >= 0 ? "text-blue-400" : "text-red-400"}`}
                    />
                  </div>
                  <div className="ml-3 w-0 flex-1">
                    <dl>
                      <dt className="text-xs font-medium text-gray-500 truncate">
                        Průměrná měsíční bilance
                      </dt>
                      <dd
                        className={`text-sm font-medium ${data.summary.averageMonthlyBalance >= 0 ? "text-blue-600" : "text-red-600"}`}
                      >
                        {formatCurrency(data.summary.averageMonthlyBalance)}
                      </dd>
                    </dl>
                  </div>
                </div>
              </div>
            </div>

            {includeStockData && data.summary.stockSummary && (
              <>
                <div className="bg-white overflow-hidden shadow rounded-lg">
                  <div className="p-3">
                    <div className="flex items-center">
                      <div className="flex-shrink-0">
                        <Package
                          className={`h-5 w-5 ${data.summary.stockSummary.totalStockValueChange && data.summary.stockSummary.totalStockValueChange >= 0 ? "text-purple-400" : "text-orange-400"}`}
                        />
                      </div>
                      <div className="ml-3 w-0 flex-1">
                        <dl>
                          <dt className="text-xs font-medium text-gray-500 truncate">
                            Změna hodnoty skladu
                          </dt>
                          <dd
                            className={`text-sm font-medium ${data.summary.stockSummary.totalStockValueChange && data.summary.stockSummary.totalStockValueChange >= 0 ? "text-purple-600" : "text-orange-600"}`}
                          >
                            {formatCurrency(
                              data.summary.stockSummary.totalStockValueChange || 0,
                            )}
                          </dd>
                        </dl>
                      </div>
                    </div>
                  </div>
                </div>

                <div className="bg-white overflow-hidden shadow rounded-lg">
                  <div className="p-3">
                    <div className="flex items-center">
                      <div className="flex-shrink-0">
                        <BarChart3
                          className={`h-5 w-5 ${data.summary.stockSummary.totalBalanceWithStock && data.summary.stockSummary.totalBalanceWithStock >= 0 ? "text-emerald-400" : "text-red-400"}`}
                        />
                      </div>
                      <div className="ml-3 w-0 flex-1">
                        <dl>
                          <dt className="text-xs font-medium text-gray-500 truncate">
                            Celková bilance vč. skladu
                          </dt>
                          <dd
                            className={`text-sm font-medium ${data.summary.stockSummary.totalBalanceWithStock && data.summary.stockSummary.totalBalanceWithStock >= 0 ? "text-emerald-600" : "text-red-600"}`}
                          >
                            {formatCurrency(
                              data.summary.stockSummary.totalBalanceWithStock || 0,
                            )}
                          </dd>
                        </dl>
                      </div>
                    </div>
                  </div>
                </div>
              </>
            )}
          </div>
        )}

        {/* Chart */}
        {chartData && (
          <FinancialChart
            chartData={chartData}
            chartOptions={chartOptions}
            title={`Finanční přehled - ${getPeriodLabel(selectedPeriod)}${includeStockData ? " (včetně skladu)" : ""}`}
          />
        )}

        {/* Monthly data */}
        {data?.data && (
          <>
            {isMobile ? (
              <div className="mb-8">
                <button
                  type="button"
                  onClick={() => setIsDataExpanded((prev) => !prev)}
                  className="w-full flex items-center justify-between px-4 py-3 bg-white shadow sm:rounded-md text-left"
                >
                  <span className="text-base font-medium text-gray-900">
                    Měsíční data ({data.data.length})
                  </span>
                  {isDataExpanded ? (
                    <ChevronUp className="h-5 w-5 text-gray-500" />
                  ) : (
                    <ChevronDown className="h-5 w-5 text-gray-500" />
                  )}
                </button>
                {isDataExpanded && (
                  <div className="mt-2">
                    <FinancialDataCards
                      data={data.data}
                      includeStockData={includeStockData}
                    />
                  </div>
                )}
              </div>
            ) : (
              <div className="bg-white shadow sm:rounded-md mb-8">
                <div className="px-4 py-5 sm:px-6 border-b border-gray-200">
                  <h3 className="text-lg leading-6 font-medium text-gray-900">
                    Měsíční data
                  </h3>
                  <p className="mt-1 max-w-2xl text-sm text-gray-500">
                    Detailní rozpis příjmů, nákladů a bilance po jednotlivých
                    měsících{includeStockData ? " (včetně skladových dat)" : ""}
                  </p>
                </div>
                <FinancialDataTable
                  data={data.data}
                  includeStockData={includeStockData}
                />
              </div>
            )}
          </>
        )}

        {/* Empty state */}
        {data?.data && data.data.length === 0 && (
          <div className="text-center py-12">
            <DollarSign className="mx-auto h-12 w-12 text-gray-400" />
            <h3 className="mt-2 text-sm font-medium text-gray-900">
              Žádná finanční data
            </h3>
            <p className="mt-1 text-sm text-gray-500">
              Pro vybrané období nejsou k dispozici žádná finanční data.
            </p>
          </div>
        )}
      </div>
    </div>
  );
};

export default FinancialOverview;
```

- [ ] **Step 6.2: Run build and lint**

```bash
cd frontend && npm run build 2>&1 | tail -20
```

Expected: build succeeds with no TypeScript errors.

```bash
cd frontend && npm run lint 2>&1 | tail -20
```

Expected: no lint errors.

- [ ] **Step 6.3: Run all unit tests**

```bash
cd frontend && npm test -- --watchAll=false 2>&1 | tail -30
```

Expected: all existing tests still pass; new tests from tasks 1–5 also pass.

- [ ] **Step 6.4: Commit**

```bash
git add frontend/src/components/pages/FinancialOverview.tsx
git commit -m "feat(finance): wire child components into FinancialOverview, enable mobile layout"
```

---

## Task 7: Add E2E test for mobile viewport + register finance project in Playwright

**Files:**
- Create: `frontend/test/e2e/finance/financial-overview-mobile.spec.ts`
- Modify: `frontend/playwright.config.ts`

- [ ] **Step 7.1: Add the `finance` project to `playwright.config.ts`**

In `frontend/playwright.config.ts`, add to the `projects` array (after the `marketing` entry):

```typescript
    {
      name: 'finance',
      testDir: './test/e2e/finance',
      use: { ...devices['Desktop Chrome'] },
    },
```

- [ ] **Step 7.2: Create the E2E test file**

Create `frontend/test/e2e/finance/financial-overview-mobile.spec.ts`:

```typescript
import { test, expect, devices } from '@playwright/test'
import { navigateToApp } from '../helpers/e2e-auth-helper'
import { waitForPageLoad } from '../helpers/wait-helpers'

const MOBILE_VIEWPORT = devices['iPhone 12'].viewport  // 390 × 844

test.describe('Financial Overview — mobile viewport', () => {
  test.use({ viewport: MOBILE_VIEWPORT })

  test.beforeEach(async ({ page }) => {
    await navigateToApp(page)
    const baseUrl = process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz'
    await page.goto(`${baseUrl}/finance/overview`)
    await waitForPageLoad(page)
  })

  test('KPI cards and chart are visible without scrolling', async ({ page }) => {
    // At least one KPI card heading is visible above the fold
    await expect(page.getByText('Celkové příjmy')).toBeVisible()
    await expect(page.getByText('Celkové náklady')).toBeVisible()

    // Chart section heading is present (it may require a small scroll on very small screens)
    await expect(page.getByText(/Finanční přehled -/)).toBeVisible()
  })

  test('filters panel is collapsed by default and expands on tap', async ({ page }) => {
    // Toggle button is visible
    const toggleBtn = page.getByText(/Filtry & období/)
    await expect(toggleBtn).toBeVisible()

    // Period select is hidden initially
    await expect(page.getByLabel('Časové období:')).not.toBeVisible()

    // Tap to expand
    await toggleBtn.click()

    // Period select is now visible
    await expect(page.getByLabel('Časové období:')).toBeVisible()
  })

  test('monthly data is collapsed by default and shows cards when expanded', async ({ page }) => {
    // Expander button present
    const expanderBtn = page.getByRole('button', { name: /Měsíční data/ })
    await expect(expanderBtn).toBeVisible()

    // Cards not visible yet
    await expect(page.getByText('Příjmy').first()).not.toBeVisible()

    // Expand
    await expanderBtn.click()

    // Card rows are now visible
    await expect(page.getByText('Příjmy').first()).toBeVisible()
    await expect(page.getByText('Náklady').first()).toBeVisible()
  })
})
```

- [ ] **Step 7.3: Verify the E2E test file compiles**

```bash
cd frontend && npx tsc --noEmit --project tsconfig.json 2>&1 | grep "financial-overview-mobile" | head -10
```

Expected: no errors referencing the new file.

- [ ] **Step 7.4: Commit**

```bash
git add frontend/test/e2e/finance/financial-overview-mobile.spec.ts \
        frontend/playwright.config.ts
git commit -m "test(finance): add Playwright mobile-viewport E2E test for financial overview"
```

---

## Verification checklist

After all tasks are complete, confirm the following:

- [ ] `cd frontend && npm run build` — passes with no errors
- [ ] `cd frontend && npm run lint` — passes with no errors
- [ ] `cd frontend && npm test -- --watchAll=false` — all tests pass
- [ ] Manual check at 375px (Chrome DevTools): KPI cards in 2 columns, filters behind toggle, chart swipeable for 13/26-month period, monthly data collapses to cards
- [ ] Manual check at 1280px: layout visually identical to before this change
- [ ] `./scripts/run-playwright-tests.sh` — E2E suite passes (run against staging)
