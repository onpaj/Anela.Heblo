# Mobile Terminal Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `/terminal/*` route group with a mobile-first layout (no sidebar, no topbar) to Anela.Heblo, including a reusable `ScanInput` component and placeholder workflow tiles, so warehouse/manufacture staff can open the app on Android + HID barcode scanner and navigate to future workflows.

**Architecture:** A single new top-level `<Routes>` split in `App.tsx`: `/terminal/*` mounts `TerminalLayout` (mobile shell with `<Outlet>`); all other paths fall through to the existing `<Layout>` via a catch-all `<Route path="*">`. No backend changes. No new API calls. Auth is handled by the existing `<AuthGuard>`.

**Tech Stack:** React 18, TypeScript, react-router-dom v6, Tailwind CSS (existing custom tokens), lucide-react, Jest + React Testing Library.

---

## File Map

| File | Action |
|---|---|
| `frontend/src/components/terminal/TerminalLayout.tsx` | Create — mobile shell with sticky top bar (back + title + user menu), `<Outlet>`, `max-w-md` content area |
| `frontend/src/components/terminal/TerminalHome.tsx` | Create — 3 workflow tiles linking to sub-routes |
| `frontend/src/components/terminal/ComingSoonPage.tsx` | Create — placeholder page used by all 3 workflow sub-routes |
| `frontend/src/components/terminal/ScanInput.tsx` | Create — always-focused scanner input component |
| `frontend/src/components/terminal/__tests__/TerminalLayout.test.tsx` | Create — tests back button visibility, navigation |
| `frontend/src/components/terminal/__tests__/TerminalHome.test.tsx` | Create — tests 3 tiles render with correct hrefs |
| `frontend/src/components/terminal/__tests__/ScanInput.test.tsx` | Create — tests auto-focus, Enter submit, button submit, blur re-focus |
| `frontend/src/App.tsx` | Modify — restructure to split `/terminal/*` vs `*` routes |
| `frontend/src/components/Layout/Sidebar.tsx` | Modify — add "Terminál" link to "Sklad" section |

---

## Task 1: ScanInput component (TDD)

**Files:**
- Create: `frontend/src/components/terminal/__tests__/ScanInput.test.tsx`
- Create: `frontend/src/components/terminal/ScanInput.tsx`

- [ ] **Step 1: Write the failing tests**

```tsx
// frontend/src/components/terminal/__tests__/ScanInput.test.tsx
import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import ScanInput from '../ScanInput';

beforeEach(() => {
  jest.useFakeTimers();
});

afterEach(() => {
  jest.runOnlyPendingTimers();
  jest.useRealTimers();
});

describe('ScanInput', () => {
  const onScan = jest.fn();

  beforeEach(() => {
    onScan.mockClear();
  });

  it('auto-focuses the input on mount', () => {
    render(<ScanInput label="Kód" onScan={onScan} />);
    const input = screen.getByRole('textbox');
    expect(document.activeElement).toBe(input);
  });

  it('uppercases input value by default', () => {
    render(<ScanInput label="Kód" onScan={onScan} />);
    const input = screen.getByRole('textbox');
    fireEvent.change(input, { target: { value: 'abc123' } });
    expect(input).toHaveValue('ABC123');
  });

  it('does not uppercase when uppercase=false', () => {
    render(<ScanInput label="Kód" onScan={onScan} uppercase={false} />);
    const input = screen.getByRole('textbox');
    fireEvent.change(input, { target: { value: 'abc' } });
    expect(input).toHaveValue('abc');
  });

  it('calls onScan and clears input on Enter', () => {
    render(<ScanInput label="Kód" onScan={onScan} />);
    const input = screen.getByRole('textbox');
    fireEvent.change(input, { target: { value: 'B001' } });
    fireEvent.submit(input.closest('form')!);
    expect(onScan).toHaveBeenCalledWith('B001');
    expect(input).toHaveValue('');
  });

  it('calls onScan on Potvrdit button click', () => {
    render(<ScanInput label="Kód" onScan={onScan} />);
    const input = screen.getByRole('textbox');
    fireEvent.change(input, { target: { value: 'LOT-99' } });
    fireEvent.click(screen.getByRole('button', { name: /potvrdit/i }));
    expect(onScan).toHaveBeenCalledWith('LOT-99');
  });

  it('does not call onScan when input is empty', () => {
    render(<ScanInput label="Kód" onScan={onScan} />);
    fireEvent.submit(screen.getByRole('textbox').closest('form')!);
    expect(onScan).not.toHaveBeenCalled();
  });

  it('disables input and button when loading=true', () => {
    render(<ScanInput label="Kód" onScan={onScan} loading={true} />);
    expect(screen.getByRole('textbox')).toBeDisabled();
    expect(screen.getByRole('button', { name: /potvrdit/i })).toBeDisabled();
  });

  it('re-focuses input 100ms after blur', () => {
    render(<ScanInput label="Kód" onScan={onScan} />);
    const input = screen.getByRole('textbox');
    fireEvent.blur(input);
    act(() => { jest.advanceTimersByTime(100); });
    expect(document.activeElement).toBe(input);
  });

  it('does not re-focus on blur when loading=true', () => {
    render(<ScanInput label="Kód" onScan={onScan} loading={true} />);
    const input = screen.getByRole('textbox');
    input.focus();
    fireEvent.blur(input);
    act(() => { jest.advanceTimersByTime(100); });
    expect(document.activeElement).not.toBe(input);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd frontend && npm test -- --testPathPattern="ScanInput" --watchAll=false 2>&1 | tail -20
```
Expected: FAIL — `Cannot find module '../ScanInput'`

- [ ] **Step 3: Implement ScanInput**

```tsx
// frontend/src/components/terminal/ScanInput.tsx
import React, { useRef, useState, useCallback, useEffect } from 'react';
import { Scan, Loader2 } from 'lucide-react';

interface ScanInputProps {
  label: string;
  placeholder?: string;
  onScan: (value: string) => void;
  loading?: boolean;
  uppercase?: boolean;
  autoFocusOnMount?: boolean;
}

const ScanInput: React.FC<ScanInputProps> = ({
  label,
  placeholder = 'Naskenujte nebo zadejte kód...',
  onScan,
  loading = false,
  uppercase = true,
  autoFocusOnMount = true,
}) => {
  const [value, setValue] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);
  const loadingRef = useRef(loading);
  loadingRef.current = loading;

  useEffect(() => {
    if (autoFocusOnMount) {
      inputRef.current?.focus();
    }
  }, [autoFocusOnMount]);

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setValue(uppercase ? e.target.value.toUpperCase() : e.target.value);
  };

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();
      const trimmed = value.trim();
      if (!trimmed) return;
      onScan(trimmed);
      setValue('');
      setTimeout(() => {
        if (!loadingRef.current) inputRef.current?.focus();
      }, 100);
    },
    [value, onScan],
  );

  const handleBlur = useCallback(() => {
    if (loadingRef.current) return;
    setTimeout(() => {
      if (!loadingRef.current) inputRef.current?.focus();
    }, 100);
  }, []);

  return (
    <div className="space-y-2">
      <label className="block text-sm font-medium text-neutral-slate">{label}</label>
      <form onSubmit={handleSubmit} className="flex gap-2">
        <div className="relative flex-1">
          <Scan className="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-neutral-gray pointer-events-none" />
          <input
            ref={inputRef}
            type="text"
            value={value}
            onChange={handleChange}
            onBlur={handleBlur}
            placeholder={placeholder}
            disabled={loading}
            autoComplete="off"
            autoCapitalize="off"
            className="w-full h-14 pl-10 pr-3 text-lg border border-border-light rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-primary-blue disabled:bg-gray-100 disabled:cursor-not-allowed"
          />
        </div>
        <button
          type="submit"
          disabled={loading || !value.trim()}
          className="h-14 px-5 bg-primary-blue text-white font-medium rounded-xl hover:bg-accent-blue-bright disabled:opacity-50 disabled:cursor-not-allowed transition-colors flex items-center gap-2 whitespace-nowrap"
        >
          {loading && <Loader2 className="h-5 w-5 animate-spin" />}
          Potvrdit
        </button>
      </form>
    </div>
  );
};

export default ScanInput;
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd frontend && npm test -- --testPathPattern="ScanInput" --watchAll=false 2>&1 | tail -20
```
Expected: PASS — 9 tests pass

- [ ] **Step 5: Commit**

```bash
cd frontend && git add src/components/terminal/ScanInput.tsx src/components/terminal/__tests__/ScanInput.test.tsx
git commit -m "feat: add ScanInput component for hardware barcode scanner workflows"
```

---

## Task 2: TerminalLayout component (TDD)

**Files:**
- Create: `frontend/src/components/terminal/__tests__/TerminalLayout.test.tsx`
- Create: `frontend/src/components/terminal/TerminalLayout.tsx`

- [ ] **Step 1: Write the failing tests**

```tsx
// frontend/src/components/terminal/__tests__/TerminalLayout.test.tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import TerminalLayout from '../TerminalLayout';

// Stub UserProfile — it has complex auth dependencies
jest.mock('../../auth/UserProfile', () => ({
  __esModule: true,
  default: () => <div data-testid="user-profile" />,
}));

const renderWithRouter = (initialPath: string, children?: React.ReactNode) =>
  render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/terminal/*" element={<TerminalLayout />}>
          <Route index element={<div>Home content</div>} />
          <Route path="receive" element={<div>Receive content</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );

describe('TerminalLayout', () => {
  it('renders the app title', () => {
    renderWithRouter('/terminal');
    expect(screen.getByText('Heblo Terminál')).toBeInTheDocument();
  });

  it('hides back button on /terminal (home)', () => {
    renderWithRouter('/terminal');
    expect(screen.queryByRole('button', { name: /zpět/i })).not.toBeInTheDocument();
  });

  it('shows back button on sub-routes', () => {
    renderWithRouter('/terminal/receive');
    expect(screen.getByRole('button', { name: /zpět/i })).toBeInTheDocument();
  });

  it('renders child route content via Outlet', () => {
    renderWithRouter('/terminal/receive');
    expect(screen.getByText('Receive content')).toBeInTheDocument();
  });

  it('renders user profile', () => {
    renderWithRouter('/terminal');
    expect(screen.getByTestId('user-profile')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd frontend && npm test -- --testPathPattern="TerminalLayout" --watchAll=false 2>&1 | tail -20
```
Expected: FAIL — `Cannot find module '../TerminalLayout'`

- [ ] **Step 3: Implement TerminalLayout**

```tsx
// frontend/src/components/terminal/TerminalLayout.tsx
import React from 'react';
import { Outlet, useLocation, useNavigate } from 'react-router-dom';
import { ArrowLeft } from 'lucide-react';
import UserProfile from '../auth/UserProfile';

const TERMINAL_ROOT = '/terminal';

const TerminalLayout: React.FC = () => {
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const isHome = pathname === TERMINAL_ROOT || pathname === `${TERMINAL_ROOT}/`;

  return (
    <div className="min-h-screen flex flex-col bg-background-gray">
      <header className="h-14 sticky top-0 z-10 bg-white border-b border-border-light flex items-center px-4 gap-3">
        {!isHome && (
          <button
            onClick={() => navigate(TERMINAL_ROOT)}
            aria-label="Zpět"
            className="p-2 -ml-2 rounded-md text-neutral-gray hover:text-primary-blue hover:bg-secondary-blue-pale transition-colors"
          >
            <ArrowLeft className="h-5 w-5" />
          </button>
        )}
        <span className="flex-1 text-base font-semibold text-neutral-slate select-none">
          Heblo Terminál
        </span>
        <UserProfile compact={true} />
      </header>

      <main className="flex-1 overflow-y-auto p-4">
        <div className="max-w-md mx-auto w-full">
          <Outlet />
        </div>
      </main>
    </div>
  );
};

export default TerminalLayout;
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd frontend && npm test -- --testPathPattern="TerminalLayout" --watchAll=false 2>&1 | tail -20
```
Expected: PASS — 5 tests pass

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/terminal/TerminalLayout.tsx frontend/src/components/terminal/__tests__/TerminalLayout.test.tsx
git commit -m "feat: add TerminalLayout — mobile shell with sticky top bar and Outlet"
```

---

## Task 3: TerminalHome and ComingSoonPage (TDD)

**Files:**
- Create: `frontend/src/components/terminal/__tests__/TerminalHome.test.tsx`
- Create: `frontend/src/components/terminal/TerminalHome.tsx`
- Create: `frontend/src/components/terminal/ComingSoonPage.tsx`

- [ ] **Step 1: Write the failing tests**

```tsx
// frontend/src/components/terminal/__tests__/TerminalHome.test.tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import TerminalHome from '../TerminalHome';

const renderHome = () =>
  render(
    <MemoryRouter>
      <TerminalHome />
    </MemoryRouter>,
  );

describe('TerminalHome', () => {
  it('renders heading', () => {
    renderHome();
    expect(screen.getByText('Vyberte operaci')).toBeInTheDocument();
  });

  it('renders tile for transport-box receiving', () => {
    renderHome();
    const tile = screen.getByTestId('workflow-tile-receive');
    expect(tile).toBeInTheDocument();
    expect(tile).toHaveAttribute('href', '/terminal/receive');
  });

  it('renders tile for stocktaking', () => {
    renderHome();
    const tile = screen.getByTestId('workflow-tile-stocktake');
    expect(tile).toHaveAttribute('href', '/terminal/stocktake');
  });

  it('renders tile for lot identification', () => {
    renderHome();
    const tile = screen.getByTestId('workflow-tile-lot-identification');
    expect(tile).toHaveAttribute('href', '/terminal/lot-identification');
  });

  it('shows coming-soon label on all tiles', () => {
    renderHome();
    const labels = screen.getAllByText('Brzy k dispozici');
    expect(labels).toHaveLength(3);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd frontend && npm test -- --testPathPattern="TerminalHome" --watchAll=false 2>&1 | tail -20
```
Expected: FAIL — `Cannot find module '../TerminalHome'`

- [ ] **Step 3: Implement TerminalHome**

```tsx
// frontend/src/components/terminal/TerminalHome.tsx
import React from 'react';
import { Link } from 'react-router-dom';
import { Package, ClipboardList, Tag, ChevronRight } from 'lucide-react';

interface WorkflowTile {
  id: string;
  title: string;
  description: string;
  href: string;
  icon: React.ElementType;
}

const WORKFLOWS: WorkflowTile[] = [
  {
    id: 'receive',
    title: 'Příjem boxu',
    description: 'Naskenujte kód boxu a potvrďte příjem zásilky do skladu',
    href: '/terminal/receive',
    icon: Package,
  },
  {
    id: 'stocktake',
    title: 'Inventura',
    description: 'Inventarizace materiálu a surovin po šaržích',
    href: '/terminal/stocktake',
    icon: ClipboardList,
  },
  {
    id: 'lot-identification',
    title: 'Identifikace šarže',
    description: 'Evidujte šarže při příjmu a sledujte spotřebu ve výrobě',
    href: '/terminal/lot-identification',
    icon: Tag,
  },
];

const TerminalHome: React.FC = () => (
  <div className="space-y-3 pt-2">
    <h1 className="text-xl font-bold text-neutral-slate">Vyberte operaci</h1>
    {WORKFLOWS.map(({ id, title, description, href, icon: Icon }) => (
      <Link
        key={id}
        to={href}
        data-testid={`workflow-tile-${id}`}
        className="flex items-center gap-4 bg-white border border-border-light rounded-xl p-4 shadow-soft hover:border-primary-blue hover:shadow-hover transition-all min-h-[72px]"
      >
        <div className="flex-shrink-0 w-12 h-12 bg-secondary-blue-pale rounded-xl flex items-center justify-center">
          <Icon className="h-6 w-6 text-primary-blue" />
        </div>
        <div className="flex-1 min-w-0">
          <p className="text-base font-semibold text-neutral-slate">{title}</p>
          <p className="text-sm text-neutral-gray mt-0.5">{description}</p>
          <span className="text-xs text-neutral-gray italic">Brzy k dispozici</span>
        </div>
        <ChevronRight className="h-5 w-5 text-neutral-gray flex-shrink-0" />
      </Link>
    ))}
  </div>
);

export default TerminalHome;
```

- [ ] **Step 4: Implement ComingSoonPage**

```tsx
// frontend/src/components/terminal/ComingSoonPage.tsx
import React from 'react';
import { Wrench } from 'lucide-react';

interface ComingSoonPageProps {
  title: string;
}

const ComingSoonPage: React.FC<ComingSoonPageProps> = ({ title }) => (
  <div
    className="flex flex-col items-center justify-center py-20 text-center"
    data-testid="coming-soon-page"
  >
    <div className="w-16 h-16 bg-secondary-blue-pale rounded-full flex items-center justify-center mb-4">
      <Wrench className="h-8 w-8 text-primary-blue" />
    </div>
    <h2 className="text-xl font-bold text-neutral-slate mb-2">{title}</h2>
    <p className="text-sm text-neutral-gray">Tato funkce bude brzy k dispozici.</p>
  </div>
);

export default ComingSoonPage;
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
cd frontend && npm test -- --testPathPattern="TerminalHome" --watchAll=false 2>&1 | tail -20
```
Expected: PASS — 5 tests pass

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/terminal/TerminalHome.tsx frontend/src/components/terminal/ComingSoonPage.tsx frontend/src/components/terminal/__tests__/TerminalHome.test.tsx
git commit -m "feat: add TerminalHome workflow tiles and ComingSoonPage placeholder"
```

---

## Task 4: Wire routes in App.tsx

**Files:**
- Modify: `frontend/src/App.tsx`

The current structure nests `<Routes>` inside `<Layout>` inside `<AuthGuard>`. We need a top-level `<Routes>` that splits `/terminal/*` from everything else.

- [ ] **Step 1: Add imports for terminal components**

At the top of `frontend/src/App.tsx`, after the existing imports, add:

```tsx
import TerminalLayout from "./components/terminal/TerminalLayout";
import TerminalHome from "./components/terminal/TerminalHome";
import ComingSoonPage from "./components/terminal/ComingSoonPage";
```

- [ ] **Step 2: Restructure the JSX in the return statement**

Find this block in `App.tsx` (lines 337–498):
```tsx
<AuthGuard>
  <Layout statusBar={<StatusBar />}>
    <Routes>
      <Route path="/" element={<Dashboard />} />
      ... (all routes)
    </Routes>
  </Layout>
</AuthGuard>
```

Replace with:
```tsx
<AuthGuard>
  <Routes>
    {/* Mobile terminal — no sidebar, no topbar */}
    <Route path="/terminal/*" element={<TerminalLayout />}>
      <Route index element={<TerminalHome />} />
      <Route path="receive" element={<ComingSoonPage title="Příjem boxu" />} />
      <Route path="stocktake" element={<ComingSoonPage title="Inventura" />} />
      <Route
        path="lot-identification"
        element={<ComingSoonPage title="Identifikace šarže" />}
      />
    </Route>

    {/* Desktop app — full Layout with sidebar */}
    <Route
      path="*"
      element={
        <Layout statusBar={<StatusBar />}>
          <Routes>
            <Route path="/" element={<Dashboard />} />
            <Route path="/finance/overview" element={<FinancialOverview />} />
            <Route path="/finance/bank-statements" element={<BankStatementImportChart />} />
            <Route path="/analytics/product-margin-summary" element={<ProductMarginSummary />} />
            <Route path="/catalog" element={<CatalogList />} />
            <Route path="/purchase/orders" element={<PurchaseOrderList />} />
            <Route path="/purchase/stock-analysis" element={<PurchaseStockAnalysis />} />
            <Route path="/purchase/invoice-classification" element={<InvoiceClassificationPage />} />
            <Route path="/manufacturing/stock-analysis" element={<ManufacturingStockAnalysis />} />
            <Route path="/manufacturing/output" element={<ManufactureOutput />} />
            <Route path="/manufacturing/batch-calculator" element={<ManufactureBatchCalculator />} />
            <Route path="/manufacturing/batch-planning" element={<BatchPlanningCalculator />} />
            <Route path="/manufacturing/orders" element={<ManufactureOrderList />} />
            <Route path="/manufacturing/orders/:id" element={<ManufactureOrderDetail />} />
            <Route path="/products/margins" element={<ProductMarginsList />} />
            <Route path="/journal" element={<JournalList />} />
            <Route path="/marketing/calendar" element={<MarketingCalendarPage />} />
            <Route path="/marketing/photobank" element={<PhotobankPage />} />
            <Route path="/marketing/photobank/settings" element={<PhotobankSettingsPage />} />
            <Route path="/leaflet-generator" element={<LeafletGeneratorPage />} />
            <Route path="/journal/new" element={<JournalEntryNew />} />
            <Route path="/journal/:id/edit" element={<JournalEntryEdit />} />
            <Route path="/logistics/inventory" element={<InventoryList />} />
            <Route path="/manufacturing/inventory" element={<ManufactureInventoryList />} />
            <Route path="/logistics/transport-boxes" element={<TransportBoxList />} />
            <Route path="/logistics/receive-boxes" element={<TransportBoxReceive />} />
            <Route path="/logistics/gift-package-manufacturing" element={<GiftPackageManufacturing />} />
            <Route path="/logistics/warehouse-statistics" element={<WarehouseStatistics />} />
            <Route path="/logistics/packing-materials" element={<PackingMaterialsPage />} />
            <Route path="/logistics/expedition-archive" element={<ExpeditionListArchivePage />} />
            <Route path="/automation/invoice-import-statistics" element={<InvoiceImportStatistics />} />
            <Route path="/automation/background-tasks" element={<BackgroundTasks />} />
            <Route path="/customer/issued-invoices" element={<IssuedInvoicesPage />} />
            <Route path="/customer/bank-statements-overview" element={<BankStatementsOverviewPage />} />
            <Route path="/orgchart" element={<OrgChartPage />} />
            <Route path="/stock-operations" element={<StockOperationsPage />} />
            <Route path="/recurring-jobs" element={<RecurringJobsPage />} />
            <Route path="/knowledge-base" element={<KnowledgeBasePage />} />
            <Route path="/knowledge-base/feedback" element={<KnowledgeBaseFeedbackPage />} />
            <Route path="/marketing/feedback" element={<MarketingFeedbackPage />} />
            <Route path="/articles" element={<ArticlesPage />} />
            <Route path="/automation/data-quality" element={<DataQualityPage />} />
          </Routes>
        </Layout>
      }
    />
  </Routes>
</AuthGuard>
```

- [ ] **Step 3: Verify TypeScript compiles**

```bash
cd frontend && npx tsc --noEmit 2>&1 | head -30
```
Expected: no errors (or only pre-existing unrelated errors)

- [ ] **Step 4: Commit**

```bash
git add frontend/src/App.tsx
git commit -m "feat: wire /terminal/* route group alongside desktop Layout in App.tsx"
```

---

## Task 5: Add Sidebar "Terminál" link

**Files:**
- Modify: `frontend/src/components/Layout/Sidebar.tsx`

- [ ] **Step 1: Add the nav item to the "Sklad" section**

In `frontend/src/components/Layout/Sidebar.tsx`, find the `items` array of the `logistika` section (around line 222). The section currently ends with `"sledovani-materialu"` item. Add the terminal link as the last item in that array:

Find:
```tsx
        {
          id: "sledovani-materialu",
          name: "Sledování materiálů",
          href: "/logistics/packing-materials",
        },
      ],
    },
```

Replace with:
```tsx
        {
          id: "sledovani-materialu",
          name: "Sledování materiálů",
          href: "/logistics/packing-materials",
        },
        {
          id: "terminal",
          name: "Terminál",
          href: "/terminal",
        },
      ],
    },
```

- [ ] **Step 2: Run the full frontend test suite to catch regressions**

```bash
cd frontend && npm test -- --watchAll=false 2>&1 | tail -30
```
Expected: all tests pass (or same failures as before this change)

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/Layout/Sidebar.tsx
git commit -m "feat: add Terminál link to Sklad sidebar section"
```

---

## Task 6: Full build verification

- [ ] **Step 1: TypeScript build**

```bash
cd frontend && npm run build 2>&1 | tail -30
```
Expected: `Compiled successfully.` with no TypeScript errors.

- [ ] **Step 2: All tests pass**

```bash
cd frontend && npm test -- --watchAll=false 2>&1 | grep -E "(PASS|FAIL|Tests:|Test Suites:)" | tail -10
```
Expected: all test suites PASS, no FAILs.

- [ ] **Step 3: Manual smoke test (dev server)**

```bash
cd frontend && npm start
```

Checklist (use Chrome DevTools → toggle device toolbar → iPhone 12 Pro 390×844):
- [ ] Login succeeds via existing Entra ID / mock auth
- [ ] Navigate to `/terminal` via Sidebar "Sklad → Terminál" link — no sidebar, no topbar, 3 tiles visible
- [ ] Back button absent on `/terminal` home
- [ ] Tap "Příjem boxu" tile → `/terminal/receive` with ComingSoonPage, back button visible
- [ ] Tap back → returns to `/terminal`
- [ ] Tap "Inventura" → `/terminal/stocktake` with ComingSoonPage
- [ ] Tap "Identifikace šarže" → `/terminal/lot-identification` with ComingSoonPage
- [ ] Desktop routes (e.g. `/catalog`, `/`) still have full sidebar + topbar
- [ ] Browser back from `/terminal` returns to last desktop page (navigation is not trapped)
- [ ] User profile avatar visible in terminal top bar; clicking shows logout option

---

## Verification Summary

```bash
# Type check
cd frontend && npx tsc --noEmit

# Tests
cd frontend && npm test -- --watchAll=false

# Build
cd frontend && npm run build
```

All three commands must succeed before pushing.
