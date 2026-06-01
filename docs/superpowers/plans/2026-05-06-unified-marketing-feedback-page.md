# Unified Marketing Feedback Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `/marketing/feedback` stub with a tabbed page that aggregates KB, Leaflet, and Article feedback through shared generic components and per-feature adapter hooks.

**Architecture:** Four generic React components live in `frontend/src/components/feedback/`. Three adapter hooks in `frontend/src/components/feedback/adapters/` normalize each feature's API response into shared types. `MarketingFeedbackPage` composes them with isolated per-tab state. `KnowledgeBaseFeedbackPage` is migrated to use the same generics; the four old KB-specific component files are deleted.

**Tech Stack:** React 18, TypeScript, TanStack Query v5, Tailwind CSS, Jest + React Testing Library (`react-scripts test`)

---

## File Map

**Create:**
- `frontend/src/components/feedback/types.ts` — shared interfaces
- `frontend/src/components/feedback/GenericFeedbackStatsBar.tsx`
- `frontend/src/components/feedback/GenericFeedbackFilters.tsx`
- `frontend/src/components/feedback/GenericFeedbackTable.tsx`
- `frontend/src/components/feedback/GenericFeedbackDetailModal.tsx`
- `frontend/src/components/feedback/adapters/useKbFeedbackAdapter.ts`
- `frontend/src/components/feedback/adapters/useLeafletFeedbackAdapter.ts`
- `frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts`
- `frontend/src/components/feedback/__tests__/GenericFeedbackStatsBar.test.tsx`
- `frontend/src/components/feedback/__tests__/GenericFeedbackFilters.test.tsx`
- `frontend/src/components/feedback/__tests__/GenericFeedbackTable.test.tsx`
- `frontend/src/components/feedback/__tests__/GenericFeedbackDetailModal.test.tsx`
- `frontend/src/components/feedback/adapters/__tests__/useKbFeedbackAdapter.test.ts`
- `frontend/src/components/feedback/adapters/__tests__/useLeafletFeedbackAdapter.test.ts`
- `frontend/src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts`
- `frontend/src/pages/__tests__/MarketingFeedbackPage.test.tsx`

**Modify:**
- `frontend/src/api/hooks/useLeaflet.ts` — add `LeafletFeedbackSummary` + `LeafletFeedbackListResponse`, update query return type
- `frontend/src/pages/MarketingFeedbackPage.tsx` — replace stub
- `frontend/src/pages/KnowledgeBaseFeedbackPage.tsx` — migrate to generics

**Delete (after migration):**
- `frontend/src/components/knowledge-base/FeedbackStatsBar.tsx`
- `frontend/src/components/knowledge-base/FeedbackFilters.tsx`
- `frontend/src/components/knowledge-base/FeedbackTable.tsx`
- `frontend/src/components/knowledge-base/FeedbackDetailModal.tsx`

---

## Task 1: Create worktree

**Files:** none (git operation)

- [ ] **Step 1: Create the worktree from feature/genai_consistency**

```bash
git fetch origin
git worktree add ../Anela.Heblo-feature-934 -b feature/934-unified-marketing-feedback-page origin/feature/genai_consistency
```

- [ ] **Step 2: Verify worktree and branch**

```bash
git worktree list
# Expected output includes:
# .../Anela.Heblo-feature-934  [feature/934-unified-marketing-feedback-page]
```

All remaining tasks run inside `../Anela.Heblo-feature-934/`.

---

## Task 2: Shared types

**Files:**
- Create: `frontend/src/components/feedback/types.ts`

No tests for a pure types file.

- [ ] **Step 1: Create the types file**

```typescript
// frontend/src/components/feedback/types.ts
import type { ReactNode } from 'react';

export interface GenericFeedbackStats {
  totalItems: number;
  totalWithFeedback: number;
  avgPrecisionScore: number | null;
  avgStyleScore: number | null;
}

export interface FeedbackRow {
  id: string;
  primaryText: string;
  secondaryText?: string;
  createdAt: string;
  userId?: string;
  precisionScore?: number | null;
  styleScore?: number | null;
  hasFeedback: boolean;
}

export interface FeedbackDetail extends FeedbackRow {
  feedbackComment?: string | null;
  extra?: ReactNode;
}

export interface GenericFeedbackParams {
  pageNumber: number;
  pageSize: number;
  sortBy: string;
  sortDescending: boolean;
  hasFeedback?: boolean;
  userId?: string;
}

export const DEFAULT_FEEDBACK_PARAMS: GenericFeedbackParams = {
  pageNumber: 1,
  pageSize: 20,
  sortBy: 'CreatedAt',
  sortDescending: true,
};

export const SORT_COLUMNS = [
  { value: 'CreatedAt', label: 'Datum' },
  { value: 'PrecisionScore', label: 'Přesnost' },
  { value: 'StyleScore', label: 'Styl' },
] as const;
```

- [ ] **Step 2: Commit**

```bash
git add frontend/src/components/feedback/types.ts
git commit -m "feat: add shared feedback types for unified page"
```

---

## Task 3: Add Leaflet feedback types and fix query return type

**Files:**
- Modify: `frontend/src/api/hooks/useLeaflet.ts`

- [ ] **Step 1: Add the two missing interfaces after `LeafletFeedbackListParams`**

Find the block:
```typescript
export interface LeafletFeedbackListParams {
  hasFeedback?: boolean;
  userId?: string;
  sortBy?: string;
  sortDescending?: boolean;
  pageNumber?: number;
  pageSize?: number;
}
```

Add after it:
```typescript
export interface LeafletFeedbackSummary {
  id: string;
  topic: string;
  audience: string;
  length: string;
  finalMarkdown: string;
  kbSourceCount: number;
  leafletSourceCount: number;
  durationMs: number;
  createdAt: string;
  userId: string | null;
  precisionScore: number | null;
  styleScore: number | null;
  feedbackComment: string | null;
}

export interface LeafletFeedbackListResponse {
  items: LeafletFeedbackSummary[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  stats: {
    totalGenerations: number;
    totalWithFeedback: number;
    avgPrecisionScore: number | null;
    avgStyleScore: number | null;
  };
}
```

- [ ] **Step 2: Update `useLeafletFeedbackListQuery` to return typed promise**

Find:
```typescript
    queryFn: async () => {
```

Replace with:
```typescript
    queryFn: async (): Promise<LeafletFeedbackListResponse> => {
```

And find `return response.json();` inside that function and replace with:
```typescript
      return response.json() as Promise<LeafletFeedbackListResponse>;
```

- [ ] **Step 3: Verify TypeScript compiles**

```bash
cd frontend && npx tsc --noEmit 2>&1 | grep -i "useLeaflet" | head -10
# Expected: no errors
```

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/hooks/useLeaflet.ts
git commit -m "feat: add LeafletFeedbackSummary and LeafletFeedbackListResponse types"
```

---

## Task 4: GenericFeedbackStatsBar (TDD)

**Files:**
- Create: `frontend/src/components/feedback/GenericFeedbackStatsBar.tsx`
- Test: `frontend/src/components/feedback/__tests__/GenericFeedbackStatsBar.test.tsx`

- [ ] **Step 1: Write the failing tests**

```typescript
// frontend/src/components/feedback/__tests__/GenericFeedbackStatsBar.test.tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import GenericFeedbackStatsBar from '../GenericFeedbackStatsBar';
import type { GenericFeedbackStats } from '../types';

const stats: GenericFeedbackStats = {
  totalItems: 42,
  totalWithFeedback: 10,
  avgPrecisionScore: 3.5,
  avgStyleScore: null,
};

test('shows skeleton cards when loading', () => {
  render(<GenericFeedbackStatsBar stats={undefined} isLoading={true} itemLabel="dotazů" />);
  // Four skeleton divs rendered with animate-pulse
  const skeletons = document.querySelectorAll('.animate-pulse');
  expect(skeletons.length).toBe(4);
});

test('shows item count with label', () => {
  render(<GenericFeedbackStatsBar stats={stats} isLoading={false} itemLabel="dotazů" />);
  expect(screen.getByText('42')).toBeInTheDocument();
  expect(screen.getByText(/dotazů/i)).toBeInTheDocument();
});

test('shows feedback count and percentage', () => {
  render(<GenericFeedbackStatsBar stats={stats} isLoading={false} itemLabel="dotazů" />);
  expect(screen.getByText('10')).toBeInTheDocument();
  expect(screen.getByText(/24 %/)).toBeInTheDocument();
});

test('shows precision score when present', () => {
  render(<GenericFeedbackStatsBar stats={stats} isLoading={false} itemLabel="dotazů" />);
  expect(screen.getByText('3.5')).toBeInTheDocument();
});

test('shows dash when style score is null', () => {
  render(<GenericFeedbackStatsBar stats={stats} isLoading={false} itemLabel="dotazů" />);
  expect(screen.getAllByText('–').length).toBeGreaterThan(0);
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd frontend && react-scripts test --testPathPattern="GenericFeedbackStatsBar" --watchAll=false 2>&1 | tail -10
# Expected: FAIL — Cannot find module '../GenericFeedbackStatsBar'
```

- [ ] **Step 3: Implement the component**

```tsx
// frontend/src/components/feedback/GenericFeedbackStatsBar.tsx
import React from 'react';
import type { GenericFeedbackStats } from './types';

interface Props {
  stats: GenericFeedbackStats | undefined;
  isLoading: boolean;
  itemLabel: string;
}

const SkeletonCard: React.FC = () => (
  <div className="bg-white border border-gray-200 rounded-lg p-4 animate-pulse">
    <div className="h-3 bg-gray-200 rounded w-24 mb-3" />
    <div className="h-7 bg-gray-200 rounded w-16" />
  </div>
);

const StatCard: React.FC<{ label: string; value: React.ReactNode }> = ({ label, value }) => (
  <div className="bg-white border border-gray-200 rounded-lg p-4">
    <p className="text-xs text-gray-500 uppercase tracking-wide">{label}</p>
    <p className="text-2xl font-semibold text-gray-900 mt-1">{value}</p>
  </div>
);

const GenericFeedbackStatsBar: React.FC<Props> = ({ stats, isLoading, itemLabel }) => {
  if (isLoading || !stats) {
    return (
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <SkeletonCard /><SkeletonCard /><SkeletonCard /><SkeletonCard />
      </div>
    );
  }

  const feedbackPct =
    stats.totalItems > 0
      ? Math.round((stats.totalWithFeedback / stats.totalItems) * 100)
      : 0;

  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
      <StatCard
        label={`Celkem ${itemLabel}`}
        value={stats.totalItems}
      />
      <StatCard
        label="S feedbackem"
        value={
          <>
            {stats.totalWithFeedback}
            <span className="text-sm font-normal text-gray-500 ml-1">({feedbackPct} %)</span>
          </>
        }
      />
      <StatCard
        label="Ø Přesnost"
        value={
          stats.avgPrecisionScore !== null ? (
            <>{stats.avgPrecisionScore}<span className="text-sm font-normal text-gray-500 ml-1">/ 5</span></>
          ) : '–'
        }
      />
      <StatCard
        label="Ø Styl"
        value={
          stats.avgStyleScore !== null ? (
            <>{stats.avgStyleScore}<span className="text-sm font-normal text-gray-500 ml-1">/ 5</span></>
          ) : '–'
        }
      />
    </div>
  );
};

export default GenericFeedbackStatsBar;
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd frontend && react-scripts test --testPathPattern="GenericFeedbackStatsBar" --watchAll=false 2>&1 | tail -10
# Expected: PASS
```

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/feedback/GenericFeedbackStatsBar.tsx \
        frontend/src/components/feedback/__tests__/GenericFeedbackStatsBar.test.tsx
git commit -m "feat: add GenericFeedbackStatsBar component"
```

---

## Task 5: GenericFeedbackFilters (TDD)

**Files:**
- Create: `frontend/src/components/feedback/GenericFeedbackFilters.tsx`
- Test: `frontend/src/components/feedback/__tests__/GenericFeedbackFilters.test.tsx`

- [ ] **Step 1: Write the failing tests**

```typescript
// frontend/src/components/feedback/__tests__/GenericFeedbackFilters.test.tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import GenericFeedbackFilters from '../GenericFeedbackFilters';

const sortColumns = [
  { value: 'CreatedAt', label: 'Datum' },
  { value: 'PrecisionScore', label: 'Přesnost' },
];

const defaultProps = {
  hasFeedback: undefined as boolean | undefined,
  sortBy: 'CreatedAt',
  sortDescending: true,
  pageSize: 20,
  allowedSortColumns: sortColumns,
  onHasFeedbackChange: jest.fn(),
  onSortByChange: jest.fn(),
  onSortDescendingChange: jest.fn(),
  onPageSizeChange: jest.fn(),
};

beforeEach(() => jest.clearAllMocks());

test('renders all four filter selects', () => {
  render(<GenericFeedbackFilters {...defaultProps} />);
  expect(screen.getByLabelText(/feedback/i)).toBeInTheDocument();
  expect(screen.getByLabelText(/řadit/i)).toBeInTheDocument();
  expect(screen.getByLabelText(/pořadí/i)).toBeInTheDocument();
  expect(screen.getByLabelText(/na stránce/i)).toBeInTheDocument();
});

test('calls onHasFeedbackChange with true when "Pouze s feedbackem" selected', () => {
  render(<GenericFeedbackFilters {...defaultProps} />);
  fireEvent.change(screen.getByLabelText(/feedback/i), { target: { value: 'true' } });
  expect(defaultProps.onHasFeedbackChange).toHaveBeenCalledWith(true);
});

test('calls onHasFeedbackChange with undefined when "Vše" selected', () => {
  render(<GenericFeedbackFilters {...{ ...defaultProps, hasFeedback: true }} />);
  fireEvent.change(screen.getByLabelText(/feedback/i), { target: { value: '' } });
  expect(defaultProps.onHasFeedbackChange).toHaveBeenCalledWith(undefined);
});

test('calls onSortByChange when sort column changes', () => {
  render(<GenericFeedbackFilters {...defaultProps} />);
  fireEvent.change(screen.getByLabelText(/řadit/i), { target: { value: 'PrecisionScore' } });
  expect(defaultProps.onSortByChange).toHaveBeenCalledWith('PrecisionScore');
});

test('calls onPageSizeChange with number when page size changes', () => {
  render(<GenericFeedbackFilters {...defaultProps} />);
  fireEvent.change(screen.getByLabelText(/na stránce/i), { target: { value: '50' } });
  expect(defaultProps.onPageSizeChange).toHaveBeenCalledWith(50);
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd frontend && react-scripts test --testPathPattern="GenericFeedbackFilters" --watchAll=false 2>&1 | tail -10
# Expected: FAIL — Cannot find module '../GenericFeedbackFilters'
```

- [ ] **Step 3: Implement the component**

```tsx
// frontend/src/components/feedback/GenericFeedbackFilters.tsx
import React from 'react';

interface SortColumn {
  value: string;
  label: string;
}

interface Props {
  hasFeedback: boolean | undefined;
  sortBy: string;
  sortDescending: boolean;
  pageSize: number;
  allowedSortColumns: SortColumn[];
  onHasFeedbackChange: (v: boolean | undefined) => void;
  onSortByChange: (v: string) => void;
  onSortDescendingChange: (v: boolean) => void;
  onPageSizeChange: (v: number) => void;
}

const selectClass =
  'border border-gray-300 rounded-md text-sm px-2 py-1.5 focus:outline-none focus:ring-2 focus:ring-blue-500';

const GenericFeedbackFilters: React.FC<Props> = ({
  hasFeedback,
  sortBy,
  sortDescending,
  pageSize,
  allowedSortColumns,
  onHasFeedbackChange,
  onSortByChange,
  onSortDescendingChange,
  onPageSizeChange,
}) => (
  <div className="flex flex-wrap gap-3 items-center">
    <div className="flex items-center gap-2">
      <label htmlFor="filter-feedback" className="text-sm text-gray-600 whitespace-nowrap">
        Feedback:
      </label>
      <select
        id="filter-feedback"
        value={hasFeedback === undefined ? '' : String(hasFeedback)}
        onChange={(e) =>
          onHasFeedbackChange(
            e.target.value === '' ? undefined : e.target.value === 'true',
          )
        }
        className={selectClass}
      >
        <option value="">Vše</option>
        <option value="true">Pouze s feedbackem</option>
        <option value="false">Pouze bez feedbacku</option>
      </select>
    </div>

    <div className="flex items-center gap-2">
      <label htmlFor="filter-sort" className="text-sm text-gray-600 whitespace-nowrap">
        Řadit dle:
      </label>
      <select
        id="filter-sort"
        value={sortBy}
        onChange={(e) => onSortByChange(e.target.value)}
        className={selectClass}
      >
        {allowedSortColumns.map((col) => (
          <option key={col.value} value={col.value}>
            {col.label}
          </option>
        ))}
      </select>
    </div>

    <div className="flex items-center gap-2">
      <label htmlFor="filter-order" className="text-sm text-gray-600 whitespace-nowrap">
        Pořadí:
      </label>
      <select
        id="filter-order"
        value={sortDescending ? 'true' : 'false'}
        onChange={(e) => onSortDescendingChange(e.target.value === 'true')}
        className={selectClass}
      >
        <option value="true">Sestupně</option>
        <option value="false">Vzestupně</option>
      </select>
    </div>

    <div className="flex items-center gap-2">
      <label htmlFor="filter-pagesize" className="text-sm text-gray-600 whitespace-nowrap">
        Na stránce:
      </label>
      <select
        id="filter-pagesize"
        value={String(pageSize)}
        onChange={(e) => onPageSizeChange(parseInt(e.target.value, 10))}
        className={selectClass}
      >
        <option value="10">10</option>
        <option value="20">20</option>
        <option value="50">50</option>
      </select>
    </div>
  </div>
);

export default GenericFeedbackFilters;
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd frontend && react-scripts test --testPathPattern="GenericFeedbackFilters" --watchAll=false 2>&1 | tail -10
# Expected: PASS
```

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/feedback/GenericFeedbackFilters.tsx \
        frontend/src/components/feedback/__tests__/GenericFeedbackFilters.test.tsx
git commit -m "feat: add GenericFeedbackFilters component"
```

---

## Task 6: GenericFeedbackTable (TDD)

**Files:**
- Create: `frontend/src/components/feedback/GenericFeedbackTable.tsx`
- Test: `frontend/src/components/feedback/__tests__/GenericFeedbackTable.test.tsx`

- [ ] **Step 1: Write the failing tests**

```typescript
// frontend/src/components/feedback/__tests__/GenericFeedbackTable.test.tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import GenericFeedbackTable from '../GenericFeedbackTable';
import type { FeedbackRow } from '../types';

const rows: FeedbackRow[] = [
  {
    id: 'row-1',
    primaryText: 'Jak funguje věrnostní program?',
    secondaryText: 'Věrnostní program nabízí slevy.',
    createdAt: '2026-01-15T10:30:00Z',
    precisionScore: 4,
    styleScore: null,
    hasFeedback: true,
  },
  {
    id: 'row-2',
    primaryText: 'Co jsou produktové kategorie?',
    createdAt: '2026-01-16T08:00:00Z',
    precisionScore: null,
    styleScore: null,
    hasFeedback: false,
  },
];

const defaultProps = {
  rows,
  isLoading: false,
  totalCount: 2,
  pageNumber: 1,
  pageSize: 20,
  totalPages: 1,
  onPageChange: jest.fn(),
  onRowClick: jest.fn(),
  primaryLabel: 'Dotaz',
};

beforeEach(() => jest.clearAllMocks());

test('renders primary text column header', () => {
  render(<GenericFeedbackTable {...defaultProps} />);
  expect(screen.getByText('Dotaz')).toBeInTheDocument();
});

test('renders rows with primary text', () => {
  render(<GenericFeedbackTable {...defaultProps} />);
  expect(screen.getByText('Jak funguje věrnostní program?')).toBeInTheDocument();
  expect(screen.getByText('Co jsou produktové kategorie?')).toBeInTheDocument();
});

test('shows "Ano" badge for rows with feedback', () => {
  render(<GenericFeedbackTable {...defaultProps} />);
  expect(screen.getByText('Ano')).toBeInTheDocument();
});

test('calls onRowClick with id when row clicked', () => {
  render(<GenericFeedbackTable {...defaultProps} />);
  fireEvent.click(screen.getByText('Jak funguje věrnostní program?'));
  expect(defaultProps.onRowClick).toHaveBeenCalledWith('row-1');
});

test('shows empty state when no rows', () => {
  render(<GenericFeedbackTable {...defaultProps} rows={[]} totalCount={0} />);
  expect(screen.getByText('Žádné záznamy nenalezeny.')).toBeInTheDocument();
});

test('disables previous button on first page', () => {
  render(<GenericFeedbackTable {...defaultProps} pageNumber={1} totalPages={3} />);
  const prevButtons = screen.getAllByRole('button').filter(b => b.textContent === '‹');
  expect(prevButtons[0]).toBeDisabled();
});

test('disables next button on last page', () => {
  render(<GenericFeedbackTable {...defaultProps} pageNumber={3} totalPages={3} totalCount={60} />);
  const nextButtons = screen.getAllByRole('button').filter(b => b.textContent === '›');
  expect(nextButtons[0]).toBeDisabled();
});

test('calls onPageChange when next button clicked', () => {
  render(<GenericFeedbackTable {...defaultProps} pageNumber={1} totalPages={3} totalCount={60} />);
  const nextButtons = screen.getAllByRole('button').filter(b => b.textContent === '›');
  fireEvent.click(nextButtons[0]);
  expect(defaultProps.onPageChange).toHaveBeenCalledWith(2);
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd frontend && react-scripts test --testPathPattern="GenericFeedbackTable" --watchAll=false 2>&1 | tail -10
# Expected: FAIL — Cannot find module '../GenericFeedbackTable'
```

- [ ] **Step 3: Implement the component**

```tsx
// frontend/src/components/feedback/GenericFeedbackTable.tsx
import React from 'react';
import type { FeedbackRow } from './types';
import { formatDateTime } from '../../utils/formatters';

interface Props {
  rows: FeedbackRow[];
  isLoading: boolean;
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  onRowClick: (id: string) => void;
  primaryLabel: string;
}

const ScoreCell: React.FC<{ score: number | null | undefined }> = ({ score }) => {
  if (score == null) return <span className="text-gray-400">–</span>;
  return (
    <span className="inline-flex items-center gap-1">
      {Array.from({ length: 5 }, (_, i) => (
        <span
          key={i}
          className={`inline-block w-2 h-2 rounded-full ${i < score ? 'bg-blue-500' : 'bg-gray-200'}`}
        />
      ))}
    </span>
  );
};

const GenericFeedbackTable: React.FC<Props> = ({
  rows, isLoading, totalCount, pageNumber, pageSize, totalPages, onPageChange, onRowClick, primaryLabel,
}) => {
  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-32 text-sm text-gray-500">Načítám…</div>
    );
  }

  if (rows.length === 0) {
    return (
      <div className="flex items-center justify-center h-32 text-sm text-gray-500">
        Žádné záznamy nenalezeny.
      </div>
    );
  }

  const firstItem = (pageNumber - 1) * pageSize + 1;
  const lastItem = Math.min(pageNumber * pageSize, totalCount);

  return (
    <div className="flex flex-col gap-3">
      <div className="overflow-x-auto rounded-lg border border-gray-200">
        <table className="min-w-full divide-y divide-gray-200 text-sm">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-4 py-3 text-left font-medium text-gray-500 uppercase tracking-wide text-xs whitespace-nowrap">
                Datum
              </th>
              <th className="px-4 py-3 text-left font-medium text-gray-500 uppercase tracking-wide text-xs">
                {primaryLabel}
              </th>
              <th className="px-4 py-3 text-center font-medium text-gray-500 uppercase tracking-wide text-xs whitespace-nowrap">
                Přesnost
              </th>
              <th className="px-4 py-3 text-center font-medium text-gray-500 uppercase tracking-wide text-xs whitespace-nowrap">
                Styl
              </th>
              <th className="px-4 py-3 text-center font-medium text-gray-500 uppercase tracking-wide text-xs whitespace-nowrap">
                Feedback
              </th>
            </tr>
          </thead>
          <tbody className="bg-white divide-y divide-gray-100">
            {rows.map((row) => (
              <tr
                key={row.id}
                onClick={() => onRowClick(row.id)}
                className="cursor-pointer transition-colors hover:bg-gray-50"
              >
                <td className="px-4 py-3 text-gray-600 whitespace-nowrap">
                  {formatDateTime(row.createdAt)}
                </td>
                <td className="px-4 py-3 text-gray-900 max-w-xs">
                  <span className="line-clamp-2">{row.primaryText}</span>
                </td>
                <td className="px-4 py-3 text-center">
                  <ScoreCell score={row.precisionScore} />
                </td>
                <td className="px-4 py-3 text-center">
                  <ScoreCell score={row.styleScore} />
                </td>
                <td className="px-4 py-3 text-center">
                  {row.hasFeedback ? (
                    <span className="inline-block px-2 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-700">
                      Ano
                    </span>
                  ) : (
                    <span className="inline-block px-2 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-500">
                      Ne
                    </span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="flex items-center justify-between text-sm text-gray-600">
        <span>{firstItem}–{lastItem} z {totalCount}</span>
        <div className="flex items-center gap-1">
          <button onClick={() => onPageChange(1)} disabled={pageNumber === 1}
            className="px-2 py-1 rounded-md disabled:opacity-40 hover:bg-gray-100 disabled:hover:bg-transparent">«</button>
          <button onClick={() => onPageChange(pageNumber - 1)} disabled={pageNumber === 1}
            className="px-2 py-1 rounded-md disabled:opacity-40 hover:bg-gray-100 disabled:hover:bg-transparent">‹</button>
          <span className="px-3 py-1 font-medium text-gray-800">{pageNumber} / {totalPages}</span>
          <button onClick={() => onPageChange(pageNumber + 1)} disabled={pageNumber === totalPages}
            className="px-2 py-1 rounded-md disabled:opacity-40 hover:bg-gray-100 disabled:hover:bg-transparent">›</button>
          <button onClick={() => onPageChange(totalPages)} disabled={pageNumber === totalPages}
            className="px-2 py-1 rounded-md disabled:opacity-40 hover:bg-gray-100 disabled:hover:bg-transparent">»</button>
        </div>
      </div>
    </div>
  );
};

export default GenericFeedbackTable;
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd frontend && react-scripts test --testPathPattern="GenericFeedbackTable" --watchAll=false 2>&1 | tail -10
# Expected: PASS
```

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/feedback/GenericFeedbackTable.tsx \
        frontend/src/components/feedback/__tests__/GenericFeedbackTable.test.tsx
git commit -m "feat: add GenericFeedbackTable component"
```

---

## Task 7: GenericFeedbackDetailModal (TDD)

**Files:**
- Create: `frontend/src/components/feedback/GenericFeedbackDetailModal.tsx`
- Test: `frontend/src/components/feedback/__tests__/GenericFeedbackDetailModal.test.tsx`

- [ ] **Step 1: Write the failing tests**

```typescript
// frontend/src/components/feedback/__tests__/GenericFeedbackDetailModal.test.tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import GenericFeedbackDetailModal from '../GenericFeedbackDetailModal';
import type { FeedbackDetail } from '../types';

const detail: FeedbackDetail = {
  id: 'log-1',
  primaryText: 'Jak funguje věrnostní program?',
  secondaryText: 'Věrnostní program nabízí slevy zákazníkům.',
  createdAt: '2026-01-15T10:30:00Z',
  userId: 'user@anela.cz',
  precisionScore: 4,
  styleScore: 3,
  hasFeedback: true,
  feedbackComment: 'Odpověď byla přesná.',
};

const onClose = jest.fn();

beforeEach(() => jest.clearAllMocks());

test('renders primary label and text', () => {
  render(
    <GenericFeedbackDetailModal
      detail={detail} onClose={onClose} primaryLabel="Dotaz" secondaryLabel="Odpověď"
    />
  );
  expect(screen.getByText('Dotaz')).toBeInTheDocument();
  expect(screen.getByText('Jak funguje věrnostní program?')).toBeInTheDocument();
});

test('renders secondary label and text', () => {
  render(
    <GenericFeedbackDetailModal
      detail={detail} onClose={onClose} primaryLabel="Dotaz" secondaryLabel="Odpověď"
    />
  );
  expect(screen.getByText('Odpověď')).toBeInTheDocument();
  expect(screen.getByText('Věrnostní program nabízí slevy zákazníkům.')).toBeInTheDocument();
});

test('renders feedback comment when present', () => {
  render(
    <GenericFeedbackDetailModal
      detail={detail} onClose={onClose} primaryLabel="Dotaz" secondaryLabel="Odpověď"
    />
  );
  expect(screen.getByText('Odpověď byla přesná.')).toBeInTheDocument();
});

test('renders userId when present', () => {
  render(
    <GenericFeedbackDetailModal
      detail={detail} onClose={onClose} primaryLabel="Dotaz" secondaryLabel="Odpověď"
    />
  );
  expect(screen.getByText('user@anela.cz')).toBeInTheDocument();
});

test('calls onClose when X button clicked', () => {
  render(
    <GenericFeedbackDetailModal
      detail={detail} onClose={onClose} primaryLabel="Dotaz" secondaryLabel="Odpověď"
    />
  );
  fireEvent.click(screen.getByLabelText('Zavřít'));
  expect(onClose).toHaveBeenCalledTimes(1);
});

test('calls onClose on Escape key', () => {
  render(
    <GenericFeedbackDetailModal
      detail={detail} onClose={onClose} primaryLabel="Dotaz" secondaryLabel="Odpověď"
    />
  );
  fireEvent.keyDown(document, { key: 'Escape' });
  expect(onClose).toHaveBeenCalledTimes(1);
});

test('renders extra content when provided', () => {
  render(
    <GenericFeedbackDetailModal
      detail={{ ...detail, extra: <div>TopK: 5</div> }}
      onClose={onClose} primaryLabel="Dotaz" secondaryLabel="Odpověď"
    />
  );
  expect(screen.getByText('TopK: 5')).toBeInTheDocument();
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd frontend && react-scripts test --testPathPattern="GenericFeedbackDetailModal" --watchAll=false 2>&1 | tail -10
# Expected: FAIL — Cannot find module '../GenericFeedbackDetailModal'
```

- [ ] **Step 3: Implement the component**

```tsx
// frontend/src/components/feedback/GenericFeedbackDetailModal.tsx
import React from 'react';
import { X } from 'lucide-react';
import type { FeedbackDetail } from './types';
import { formatDateTime } from '../../utils/formatters';

interface Props {
  detail: FeedbackDetail;
  onClose: () => void;
  primaryLabel: string;
  secondaryLabel: string;
}

const ScoreDots: React.FC<{ score: number | null | undefined; max?: number }> = ({ score, max = 5 }) => {
  if (score == null) return <span className="text-gray-400 text-sm">–</span>;
  return (
    <span className="flex gap-1 items-center">
      {Array.from({ length: max }, (_, i) => (
        <span key={i} className={`inline-block w-3 h-3 rounded-full ${i < score ? 'bg-blue-500' : 'bg-gray-200'}`} />
      ))}
      <span className="ml-1 text-sm text-gray-700">{score}/{max}</span>
    </span>
  );
};

const GenericFeedbackDetailModal: React.FC<Props> = ({ detail, onClose, primaryLabel, secondaryLabel }) => {
  React.useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg shadow-xl w-[75vw] max-h-[90vh] overflow-hidden flex flex-col">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200 flex-shrink-0">
          <h2 className="text-base font-semibold text-gray-800">Detail záznamu</h2>
          <button onClick={onClose} className="p-1 rounded-md text-gray-400 hover:text-gray-600 hover:bg-gray-100" aria-label="Zavřít">
            <X className="w-5 h-5" />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto p-6 space-y-5 text-sm">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Datum</p>
              <p className="text-gray-900">{formatDateTime(detail.createdAt)}</p>
            </div>
            {detail.userId && (
              <div>
                <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Uživatel</p>
                <p className="text-gray-900 break-all">{detail.userId}</p>
              </div>
            )}
          </div>

          <div>
            <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">{primaryLabel}</p>
            <p className="text-gray-900 whitespace-pre-wrap">{detail.primaryText}</p>
          </div>

          {detail.secondaryText && (
            <div>
              <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">{secondaryLabel}</p>
              <p className="text-gray-700 whitespace-pre-wrap leading-relaxed">{detail.secondaryText}</p>
            </div>
          )}

          {detail.extra}

          {detail.hasFeedback && (
            <div className="border-t border-gray-100 pt-5 space-y-4">
              <p className="text-xs font-semibold text-gray-500 uppercase tracking-wide">Feedback</p>
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <p className="text-xs text-gray-500 mb-1">Přesnost</p>
                  <ScoreDots score={detail.precisionScore} />
                </div>
                <div>
                  <p className="text-xs text-gray-500 mb-1">Styl</p>
                  <ScoreDots score={detail.styleScore} />
                </div>
              </div>
              {detail.feedbackComment && (
                <div>
                  <p className="text-xs text-gray-500 mb-1">Komentář</p>
                  <p className="text-gray-700 whitespace-pre-wrap">{detail.feedbackComment}</p>
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default GenericFeedbackDetailModal;
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd frontend && react-scripts test --testPathPattern="GenericFeedbackDetailModal" --watchAll=false 2>&1 | tail -10
# Expected: PASS
```

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/feedback/GenericFeedbackDetailModal.tsx \
        frontend/src/components/feedback/__tests__/GenericFeedbackDetailModal.test.tsx
git commit -m "feat: add GenericFeedbackDetailModal component"
```

---

## Task 8: KB feedback adapter (TDD)

**Files:**
- Create: `frontend/src/components/feedback/adapters/useKbFeedbackAdapter.ts`
- Test: `frontend/src/components/feedback/adapters/__tests__/useKbFeedbackAdapter.test.ts`

- [ ] **Step 1: Write the failing tests**

```typescript
// frontend/src/components/feedback/adapters/__tests__/useKbFeedbackAdapter.test.ts
import { renderHook } from '@testing-library/react';
import * as kbHooks from '../../../../api/hooks/useKnowledgeBase';
import { useKbFeedbackAdapter } from '../useKbFeedbackAdapter';
import type { GenericFeedbackParams } from '../../types';

jest.mock('../../../../api/hooks/useKnowledgeBase');

const mockLog = {
  id: 'log-1',
  question: 'Jak funguje věrnostní program?',
  answer: 'Věrnostní program nabízí různé výhody pro zákazníky po celý rok.',
  topK: 5,
  sourceCount: 3,
  durationMs: 1200,
  createdAt: '2026-01-15T10:30:00Z',
  userId: 'user@anela.cz',
  precisionScore: 4,
  styleScore: 3,
  feedbackComment: 'Výborná odpověď.',
  hasFeedback: true,
};

const mockStats = {
  totalQuestions: 100,
  totalWithFeedback: 20,
  avgPrecisionScore: 3.8,
  avgStyleScore: 4.1,
};

const params: GenericFeedbackParams = {
  pageNumber: 1, pageSize: 20, sortBy: 'CreatedAt', sortDescending: true,
};

beforeEach(() => {
  jest.spyOn(kbHooks, 'useKnowledgeBaseFeedbackListQuery').mockReturnValue({
    data: {
      success: true,
      logs: [mockLog],
      totalCount: 1,
      pageNumber: 1,
      pageSize: 20,
      totalPages: 1,
      stats: mockStats,
    },
    isLoading: false,
    isError: false,
  } as any);
});

afterEach(() => jest.restoreAllMocks());

test('maps question to primaryText', () => {
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.rows[0].primaryText).toBe('Jak funguje věrnostní program?');
});

test('maps truncated answer to secondaryText', () => {
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.rows[0].secondaryText).toBe(
    mockLog.answer.slice(0, 120),
  );
});

test('maps userId', () => {
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.rows[0].userId).toBe('user@anela.cz');
});

test('maps totalQuestions to totalItems in stats', () => {
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.stats?.totalItems).toBe(100);
});

test('passes through avgPrecisionScore', () => {
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.stats?.avgPrecisionScore).toBe(3.8);
});

test('returns totalPages from query', () => {
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.totalPages).toBe(1);
});

test('returns empty rows and undefined stats when loading', () => {
  jest.spyOn(kbHooks, 'useKnowledgeBaseFeedbackListQuery').mockReturnValue({
    data: undefined, isLoading: true, isError: false,
  } as any);
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.rows).toEqual([]);
  expect(result.current.stats).toBeUndefined();
  expect(result.current.isLoading).toBe(true);
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd frontend && react-scripts test --testPathPattern="useKbFeedbackAdapter" --watchAll=false 2>&1 | tail -10
# Expected: FAIL — Cannot find module '../useKbFeedbackAdapter'
```

- [ ] **Step 3: Implement the adapter**

```typescript
// frontend/src/components/feedback/adapters/useKbFeedbackAdapter.ts
import { useKnowledgeBaseFeedbackListQuery } from '../../../api/hooks/useKnowledgeBase';
import type { FeedbackDetail, GenericFeedbackParams, GenericFeedbackStats } from '../types';

export function useKbFeedbackAdapter(params: GenericFeedbackParams) {
  const query = useKnowledgeBaseFeedbackListQuery({
    pageNumber: params.pageNumber,
    pageSize: params.pageSize,
    sortBy: params.sortBy,
    sortDescending: params.sortDescending,
    hasFeedback: params.hasFeedback,
    userId: params.userId,
  });

  const rows: FeedbackDetail[] = (query.data?.logs ?? []).map((log) => ({
    id: log.id,
    primaryText: log.question,
    secondaryText: log.answer.slice(0, 120),
    createdAt: log.createdAt,
    userId: log.userId ?? undefined,
    precisionScore: log.precisionScore,
    styleScore: log.styleScore,
    hasFeedback: log.hasFeedback,
    feedbackComment: log.feedbackComment,
  }));

  const stats: GenericFeedbackStats | undefined = query.data?.stats
    ? {
        totalItems: query.data.stats.totalQuestions,
        totalWithFeedback: query.data.stats.totalWithFeedback,
        avgPrecisionScore: query.data.stats.avgPrecisionScore,
        avgStyleScore: query.data.stats.avgStyleScore,
      }
    : undefined;

  return {
    rows,
    stats,
    totalCount: query.data?.totalCount ?? 0,
    totalPages: query.data?.totalPages ?? 1,
    pageNumber: query.data?.pageNumber ?? params.pageNumber,
    isLoading: query.isLoading,
    isError: query.isError,
  };
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd frontend && react-scripts test --testPathPattern="useKbFeedbackAdapter" --watchAll=false 2>&1 | tail -10
# Expected: PASS
```

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/feedback/adapters/useKbFeedbackAdapter.ts \
        frontend/src/components/feedback/adapters/__tests__/useKbFeedbackAdapter.test.ts
git commit -m "feat: add useKbFeedbackAdapter hook"
```

---

## Task 9: Leaflet feedback adapter (TDD)

**Files:**
- Create: `frontend/src/components/feedback/adapters/useLeafletFeedbackAdapter.ts`
- Test: `frontend/src/components/feedback/adapters/__tests__/useLeafletFeedbackAdapter.test.ts`

- [ ] **Step 1: Write the failing tests**

```typescript
// frontend/src/components/feedback/adapters/__tests__/useLeafletFeedbackAdapter.test.ts
import { renderHook } from '@testing-library/react';
import * as leafletHooks from '../../../../api/hooks/useLeaflet';
import { useLeafletFeedbackAdapter } from '../useLeafletFeedbackAdapter';
import type { GenericFeedbackParams } from '../../types';

jest.mock('../../../../api/hooks/useLeaflet');

const mockItem = {
  id: 'gen-1',
  topic: 'Letní kolekce 2026',
  audience: 'ženy 25-40',
  length: 'medium',
  finalMarkdown: 'Tato letní kolekce přináší svěží barvy a moderní střihy pro ženy všech věkových kategorií.',
  kbSourceCount: 2,
  leafletSourceCount: 3,
  durationMs: 5000,
  createdAt: '2026-01-15T10:30:00Z',
  userId: 'user@anela.cz',
  precisionScore: 5,
  styleScore: 4,
  feedbackComment: null,
};

const mockStats = {
  totalGenerations: 50,
  totalWithFeedback: 12,
  avgPrecisionScore: 4.2,
  avgStyleScore: null,
};

const params: GenericFeedbackParams = {
  pageNumber: 2, pageSize: 10, sortBy: 'CreatedAt', sortDescending: false,
};

beforeEach(() => {
  jest.spyOn(leafletHooks, 'useLeafletFeedbackListQuery').mockReturnValue({
    data: { items: [mockItem], totalCount: 50, pageNumber: 2, pageSize: 10, stats: mockStats },
    isLoading: false,
    isError: false,
  } as any);
});

afterEach(() => jest.restoreAllMocks());

test('maps topic to primaryText', () => {
  const { result } = renderHook(() => useLeafletFeedbackAdapter(params));
  expect(result.current.rows[0].primaryText).toBe('Letní kolekce 2026');
});

test('maps truncated finalMarkdown to secondaryText', () => {
  const { result } = renderHook(() => useLeafletFeedbackAdapter(params));
  expect(result.current.rows[0].secondaryText).toBe(mockItem.finalMarkdown.slice(0, 120));
});

test('maps totalGenerations to totalItems in stats', () => {
  const { result } = renderHook(() => useLeafletFeedbackAdapter(params));
  expect(result.current.stats?.totalItems).toBe(50);
});

test('calculates totalPages from totalCount and pageSize', () => {
  const { result } = renderHook(() => useLeafletFeedbackAdapter(params));
  // 50 items / 10 per page = 5 pages
  expect(result.current.totalPages).toBe(5);
});

test('passes GenericFeedbackParams to query (translates field names)', () => {
  renderHook(() => useLeafletFeedbackAdapter(params));
  expect(leafletHooks.useLeafletFeedbackListQuery).toHaveBeenCalledWith(
    expect.objectContaining({ pageNumber: 2, pageSize: 10, sortDescending: false }),
  );
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd frontend && react-scripts test --testPathPattern="useLeafletFeedbackAdapter" --watchAll=false 2>&1 | tail -10
# Expected: FAIL — Cannot find module '../useLeafletFeedbackAdapter'
```

- [ ] **Step 3: Implement the adapter**

```typescript
// frontend/src/components/feedback/adapters/useLeafletFeedbackAdapter.ts
import { useLeafletFeedbackListQuery } from '../../../api/hooks/useLeaflet';
import type { FeedbackDetail, GenericFeedbackParams, GenericFeedbackStats } from '../types';

export function useLeafletFeedbackAdapter(params: GenericFeedbackParams) {
  const query = useLeafletFeedbackListQuery({
    pageNumber: params.pageNumber,
    pageSize: params.pageSize,
    sortBy: params.sortBy,
    sortDescending: params.sortDescending,
    hasFeedback: params.hasFeedback,
    userId: params.userId,
  });

  const rows: FeedbackDetail[] = (query.data?.items ?? []).map((item) => ({
    id: item.id,
    primaryText: item.topic,
    secondaryText: item.finalMarkdown.slice(0, 120),
    createdAt: item.createdAt,
    userId: item.userId ?? undefined,
    precisionScore: item.precisionScore,
    styleScore: item.styleScore,
    hasFeedback: item.precisionScore !== null || item.styleScore !== null,
    feedbackComment: item.feedbackComment,
  }));

  const stats: GenericFeedbackStats | undefined = query.data?.stats
    ? {
        totalItems: query.data.stats.totalGenerations,
        totalWithFeedback: query.data.stats.totalWithFeedback,
        avgPrecisionScore: query.data.stats.avgPrecisionScore,
        avgStyleScore: query.data.stats.avgStyleScore,
      }
    : undefined;

  const totalCount = query.data?.totalCount ?? 0;
  const pageSize = query.data?.pageSize ?? params.pageSize;
  const totalPages = pageSize > 0 ? Math.ceil(totalCount / pageSize) : 1;

  return {
    rows,
    stats,
    totalCount,
    totalPages,
    pageNumber: query.data?.pageNumber ?? params.pageNumber,
    isLoading: query.isLoading,
    isError: query.isError,
  };
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd frontend && react-scripts test --testPathPattern="useLeafletFeedbackAdapter" --watchAll=false 2>&1 | tail -10
# Expected: PASS
```

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/feedback/adapters/useLeafletFeedbackAdapter.ts \
        frontend/src/components/feedback/adapters/__tests__/useLeafletFeedbackAdapter.test.ts
git commit -m "feat: add useLeafletFeedbackAdapter hook"
```

---

## Task 10: Article feedback adapter (TDD)

**Files:**
- Create: `frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts`
- Test: `frontend/src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts`

- [ ] **Step 1: Write the failing tests**

```typescript
// frontend/src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts
import { renderHook } from '@testing-library/react';
import * as articleHooks from '../../../../api/hooks/useArticles';
import { useArticleFeedbackAdapter } from '../useArticleFeedbackAdapter';
import type { GenericFeedbackParams } from '../../types';

jest.mock('../../../../api/hooks/useArticles');

const mockArticle = {
  id: 'art-1',
  topic: 'Péče o pleť v létě',
  title: 'Jak pečovat o pleť v letních měsících',
  requestedBy: 'user@anela.cz',
  generatedAt: '2026-01-15T10:30:00Z',
  precisionScore: 4,
  styleScore: 5,
  feedbackComment: 'Skvělý článek.',
  hasFeedback: true,
};

const mockArticleNoTitle = {
  id: 'art-2',
  topic: 'Zimní vlasová péče',
  title: null,
  requestedBy: 'other@anela.cz',
  generatedAt: null,
  precisionScore: null,
  styleScore: null,
  feedbackComment: null,
  hasFeedback: false,
};

const mockStats = {
  totalArticles: 30,
  totalWithFeedback: 5,
  avgPrecisionScore: 3.9,
  avgStyleScore: 4.0,
};

const params: GenericFeedbackParams = {
  pageNumber: 1, pageSize: 20, sortBy: 'CreatedAt', sortDescending: true, userId: 'user@anela.cz',
};

beforeEach(() => {
  jest.spyOn(articleHooks, 'useArticleFeedbackListQuery').mockReturnValue({
    data: {
      articles: [mockArticle, mockArticleNoTitle],
      totalCount: 30,
      page: 1,
      pageSize: 20,
      totalPages: 2,
      stats: mockStats,
    },
    isLoading: false,
    isError: false,
  } as any);
});

afterEach(() => jest.restoreAllMocks());

test('maps title to primaryText when title is present', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.rows[0].primaryText).toBe('Jak pečovat o pleť v letních měsících');
});

test('falls back to topic as primaryText when title is null', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.rows[1].primaryText).toBe('Zimní vlasová péče');
});

test('maps topic to secondaryText', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.rows[0].secondaryText).toBe('Péče o pleť v létě');
});

test('maps requestedBy to userId', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.rows[0].userId).toBe('user@anela.cz');
});

test('maps generatedAt to createdAt', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.rows[0].createdAt).toBe('2026-01-15T10:30:00Z');
});

test('uses empty string for createdAt when generatedAt is null', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.rows[1].createdAt).toBe('');
});

test('maps totalArticles to totalItems in stats', () => {
  const { result } = renderHook(() => useArticleFeedbackAdapter(params));
  expect(result.current.stats?.totalItems).toBe(30);
});

test('translates GenericFeedbackParams to article-specific params', () => {
  renderHook(() => useArticleFeedbackAdapter(params));
  expect(articleHooks.useArticleFeedbackListQuery).toHaveBeenCalledWith(
    expect.objectContaining({
      page: 1,
      pageSize: 20,
      sortBy: 'CreatedAt',
      descending: true,
      requestedBy: 'user@anela.cz',
    }),
  );
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd frontend && react-scripts test --testPathPattern="useArticleFeedbackAdapter" --watchAll=false 2>&1 | tail -10
# Expected: FAIL — Cannot find module '../useArticleFeedbackAdapter'
```

- [ ] **Step 3: Implement the adapter**

```typescript
// frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts
import { useArticleFeedbackListQuery } from '../../../api/hooks/useArticles';
import type { FeedbackDetail, GenericFeedbackParams, GenericFeedbackStats } from '../types';

export function useArticleFeedbackAdapter(params: GenericFeedbackParams) {
  const query = useArticleFeedbackListQuery({
    page: params.pageNumber,
    pageSize: params.pageSize,
    sortBy: params.sortBy,
    descending: params.sortDescending,
    hasFeedback: params.hasFeedback,
    requestedBy: params.userId,
  });

  const rows: FeedbackDetail[] = (query.data?.articles ?? []).map((article) => ({
    id: article.id,
    primaryText: article.title ?? article.topic,
    secondaryText: article.topic,
    createdAt: article.generatedAt ?? '',
    userId: article.requestedBy,
    precisionScore: article.precisionScore,
    styleScore: article.styleScore,
    hasFeedback: article.hasFeedback,
    feedbackComment: article.feedbackComment,
  }));

  const stats: GenericFeedbackStats | undefined = query.data?.stats
    ? {
        totalItems: query.data.stats.totalArticles,
        totalWithFeedback: query.data.stats.totalWithFeedback,
        avgPrecisionScore: query.data.stats.avgPrecisionScore,
        avgStyleScore: query.data.stats.avgStyleScore,
      }
    : undefined;

  return {
    rows,
    stats,
    totalCount: query.data?.totalCount ?? 0,
    totalPages: query.data?.totalPages ?? 1,
    pageNumber: query.data?.page ?? params.pageNumber,
    isLoading: query.isLoading,
    isError: query.isError,
  };
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd frontend && react-scripts test --testPathPattern="useArticleFeedbackAdapter" --watchAll=false 2>&1 | tail -10
# Expected: PASS
```

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts \
        frontend/src/components/feedback/adapters/__tests__/useArticleFeedbackAdapter.test.ts
git commit -m "feat: add useArticleFeedbackAdapter hook"
```

---

## Task 11: MarketingFeedbackPage (TDD)

**Files:**
- Modify: `frontend/src/pages/MarketingFeedbackPage.tsx`
- Test: `frontend/src/pages/__tests__/MarketingFeedbackPage.test.tsx`

- [ ] **Step 1: Write the failing tests**

```typescript
// frontend/src/pages/__tests__/MarketingFeedbackPage.test.tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import MarketingFeedbackPage from '../MarketingFeedbackPage';
import * as kbHooks from '../../api/hooks/useKnowledgeBase';
import * as leafletHooks from '../../api/hooks/useLeaflet';
import * as articleHooks from '../../api/hooks/useArticles';
import * as kbAdapter from '../../components/feedback/adapters/useKbFeedbackAdapter';
import * as leafletAdapter from '../../components/feedback/adapters/useLeafletFeedbackAdapter';
import * as articleAdapter from '../../components/feedback/adapters/useArticleFeedbackAdapter';
import type { FeedbackDetail, GenericFeedbackStats } from '../../components/feedback/types';

jest.mock('../../api/hooks/useKnowledgeBase');
jest.mock('../../api/hooks/useLeaflet');
jest.mock('../../api/hooks/useArticles');
jest.mock('../../components/feedback/adapters/useKbFeedbackAdapter');
jest.mock('../../components/feedback/adapters/useLeafletFeedbackAdapter');
jest.mock('../../components/feedback/adapters/useArticleFeedbackAdapter');

const emptyAdapterResult = {
  rows: [] as FeedbackDetail[],
  stats: undefined as GenericFeedbackStats | undefined,
  totalCount: 0,
  totalPages: 1,
  pageNumber: 1,
  isLoading: false,
  isError: false,
};

const kbRow: FeedbackDetail = {
  id: 'kb-1',
  primaryText: 'KB otázka',
  createdAt: '2026-01-01T00:00:00Z',
  hasFeedback: false,
};

const leafletRow: FeedbackDetail = {
  id: 'lf-1',
  primaryText: 'Leaflet téma',
  createdAt: '2026-01-01T00:00:00Z',
  hasFeedback: false,
};

function setupMocks({
  hasKb = true,
  hasLeaflet = false,
  hasArticle = false,
}: { hasKb?: boolean; hasLeaflet?: boolean; hasArticle?: boolean } = {}) {
  jest.spyOn(kbHooks, 'useKnowledgeBaseUploadPermission').mockReturnValue(hasKb);
  jest.spyOn(leafletHooks, 'useLeafletUploadPermission').mockReturnValue(hasLeaflet);
  jest.spyOn(articleHooks, 'useArticleGeneratorPermission').mockReturnValue(hasArticle);

  jest.spyOn(kbAdapter, 'useKbFeedbackAdapter').mockReturnValue({
    ...emptyAdapterResult,
    rows: [kbRow],
  });
  jest.spyOn(leafletAdapter, 'useLeafletFeedbackAdapter').mockReturnValue({
    ...emptyAdapterResult,
    rows: [leafletRow],
  });
  jest.spyOn(articleAdapter, 'useArticleFeedbackAdapter').mockReturnValue(emptyAdapterResult);
}

beforeEach(() => jest.clearAllMocks());

test('renders three tab buttons', () => {
  setupMocks();
  render(<MarketingFeedbackPage />);
  expect(screen.getByRole('button', { name: /poradenství/i })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: /letáky/i })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: /články/i })).toBeInTheDocument();
});

test('shows KB rows by default on first tab', () => {
  setupMocks();
  render(<MarketingFeedbackPage />);
  expect(screen.getByText('KB otázka')).toBeInTheDocument();
});

test('switching to Letáky tab shows leaflet rows', () => {
  setupMocks();
  render(<MarketingFeedbackPage />);
  fireEvent.click(screen.getByRole('button', { name: /letáky/i }));
  expect(screen.getByText('Leaflet téma')).toBeInTheDocument();
});

test('switching tabs resets selected row', () => {
  setupMocks();
  render(<MarketingFeedbackPage />);
  // Click a KB row to select it
  fireEvent.click(screen.getByText('KB otázka'));
  expect(screen.getByText('Detail záznamu')).toBeInTheDocument();
  // Switch tab — modal should close
  fireEvent.click(screen.getByRole('button', { name: /letáky/i }));
  expect(screen.queryByText('Detail záznamu')).not.toBeInTheDocument();
});

test('shows access denied when user has no roles', () => {
  setupMocks({ hasKb: false, hasLeaflet: false, hasArticle: false });
  render(<MarketingFeedbackPage />);
  expect(screen.getByText('Přístup odepřen.')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: /poradenství/i })).not.toBeInTheDocument();
});

test('clicking row opens detail modal', () => {
  setupMocks();
  render(<MarketingFeedbackPage />);
  fireEvent.click(screen.getByText('KB otázka'));
  expect(screen.getByText('Detail záznamu')).toBeInTheDocument();
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd frontend && react-scripts test --testPathPattern="MarketingFeedbackPage" --watchAll=false 2>&1 | tail -15
# Expected: FAIL — tests error or component renders stub text
```

- [ ] **Step 3: Implement the page**

```tsx
// frontend/src/pages/MarketingFeedbackPage.tsx
import React, { useState } from 'react';
import { MessageSquare } from 'lucide-react';
import { useKnowledgeBaseUploadPermission } from '../api/hooks/useKnowledgeBase';
import { useLeafletUploadPermission } from '../api/hooks/useLeaflet';
import { useArticleGeneratorPermission } from '../api/hooks/useArticles';
import { useKbFeedbackAdapter } from '../components/feedback/adapters/useKbFeedbackAdapter';
import { useLeafletFeedbackAdapter } from '../components/feedback/adapters/useLeafletFeedbackAdapter';
import { useArticleFeedbackAdapter } from '../components/feedback/adapters/useArticleFeedbackAdapter';
import GenericFeedbackStatsBar from '../components/feedback/GenericFeedbackStatsBar';
import GenericFeedbackFilters from '../components/feedback/GenericFeedbackFilters';
import GenericFeedbackTable from '../components/feedback/GenericFeedbackTable';
import GenericFeedbackDetailModal from '../components/feedback/GenericFeedbackDetailModal';
import {
  DEFAULT_FEEDBACK_PARAMS,
  SORT_COLUMNS,
  type FeedbackDetail,
  type GenericFeedbackParams,
} from '../components/feedback/types';

type FeatureTab = 'kb' | 'leaflet' | 'article';

const TAB_LABELS: Record<FeatureTab, string> = {
  kb: 'Poradenství (KB)',
  leaflet: 'Letáky',
  article: 'Články',
};

const ITEM_LABELS: Record<FeatureTab, string> = {
  kb: 'dotazů',
  leaflet: 'generování',
  article: 'článků',
};

const PRIMARY_LABELS: Record<FeatureTab, string> = {
  kb: 'Dotaz',
  leaflet: 'Téma',
  article: 'Téma článku',
};

const SECONDARY_LABELS: Record<FeatureTab, string> = {
  kb: 'Odpověď',
  leaflet: 'Výstup',
  article: 'Téma',
};

const MarketingFeedbackPage: React.FC = () => {
  const hasKb = useKnowledgeBaseUploadPermission();
  const hasLeaflet = useLeafletUploadPermission();
  const hasArticle = useArticleGeneratorPermission();

  const [activeTab, setActiveTab] = useState<FeatureTab>('kb');
  const [selectedRowId, setSelectedRowId] = useState<string | null>(null);
  const [kbParams, setKbParams] = useState<GenericFeedbackParams>(DEFAULT_FEEDBACK_PARAMS);
  const [leafletParams, setLeafletParams] = useState<GenericFeedbackParams>(DEFAULT_FEEDBACK_PARAMS);
  const [articleParams, setArticleParams] = useState<GenericFeedbackParams>(DEFAULT_FEEDBACK_PARAMS);

  const kb = useKbFeedbackAdapter(kbParams);
  const leaflet = useLeafletFeedbackAdapter(leafletParams);
  const article = useArticleFeedbackAdapter(articleParams);

  if (!hasKb && !hasLeaflet && !hasArticle) {
    return <div className="p-6 text-sm text-gray-500">Přístup odepřen.</div>;
  }

  const activeData = { kb, leaflet, article }[activeTab];
  const activeParams = { kb: kbParams, leaflet: leafletParams, article: articleParams }[activeTab];
  const setActiveParams = {
    kb: setKbParams,
    leaflet: setLeafletParams,
    article: setArticleParams,
  }[activeTab];

  const selectedRow: FeedbackDetail | undefined = activeData.rows.find(
    (r) => r.id === selectedRowId,
  );

  const handleTabChange = (tab: FeatureTab) => {
    setActiveTab(tab);
    setSelectedRowId(null);
  };

  const handleParamChange = (update: Partial<GenericFeedbackParams>) => {
    setActiveParams((prev) => ({ ...prev, ...update, pageNumber: 1 }));
    setSelectedRowId(null);
  };

  return (
    <div className="flex flex-col h-full">
      <div className="px-6 py-4 border-b border-gray-200 flex items-center gap-3 flex-shrink-0">
        <MessageSquare className="w-6 h-6 text-blue-600" />
        <h1 className="text-2xl font-semibold text-gray-900">Feedback</h1>
      </div>

      <div className="flex-1 overflow-y-auto p-6 space-y-4">
        {/* Tab bar */}
        <div className="flex gap-1 border-b border-gray-200">
          {(Object.keys(TAB_LABELS) as FeatureTab[]).map((tab) => (
            <button
              key={tab}
              onClick={() => handleTabChange(tab)}
              className={`px-4 py-2 text-sm font-medium border-b-2 transition-colors ${
                activeTab === tab
                  ? 'border-blue-600 text-blue-700'
                  : 'border-transparent text-gray-600 hover:text-gray-900'
              }`}
            >
              {TAB_LABELS[tab]}
            </button>
          ))}
        </div>

        <GenericFeedbackStatsBar
          stats={activeData.stats}
          isLoading={activeData.isLoading}
          itemLabel={ITEM_LABELS[activeTab]}
        />

        <GenericFeedbackFilters
          hasFeedback={activeParams.hasFeedback}
          sortBy={activeParams.sortBy}
          sortDescending={activeParams.sortDescending}
          pageSize={activeParams.pageSize}
          allowedSortColumns={[...SORT_COLUMNS]}
          onHasFeedbackChange={(v) => handleParamChange({ hasFeedback: v })}
          onSortByChange={(v) => handleParamChange({ sortBy: v })}
          onSortDescendingChange={(v) => handleParamChange({ sortDescending: v })}
          onPageSizeChange={(v) => handleParamChange({ pageSize: v })}
        />

        {activeData.isError && (
          <div className="flex items-center justify-center h-32 text-sm text-red-600">
            Nepodařilo se načíst záznamy. Zkuste to znovu.
          </div>
        )}

        {!activeData.isError && (
          <GenericFeedbackTable
            rows={activeData.rows}
            isLoading={activeData.isLoading}
            totalCount={activeData.totalCount}
            pageNumber={activeData.pageNumber}
            pageSize={activeParams.pageSize}
            totalPages={activeData.totalPages}
            onPageChange={(page) =>
              setActiveParams((prev) => ({ ...prev, pageNumber: page }))
            }
            onRowClick={(id) =>
              setSelectedRowId((prev) => (prev === id ? null : id))
            }
            primaryLabel={PRIMARY_LABELS[activeTab]}
          />
        )}
      </div>

      {selectedRow && (
        <GenericFeedbackDetailModal
          detail={selectedRow}
          onClose={() => setSelectedRowId(null)}
          primaryLabel={PRIMARY_LABELS[activeTab]}
          secondaryLabel={SECONDARY_LABELS[activeTab]}
        />
      )}
    </div>
  );
};

export default MarketingFeedbackPage;
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd frontend && react-scripts test --testPathPattern="MarketingFeedbackPage" --watchAll=false 2>&1 | tail -15
# Expected: PASS
```

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/MarketingFeedbackPage.tsx \
        frontend/src/pages/__tests__/MarketingFeedbackPage.test.tsx
git commit -m "feat: implement MarketingFeedbackPage with tabbed feedback view"
```

---

## Task 12: Migrate KnowledgeBaseFeedbackPage and delete old components

**Files:**
- Modify: `frontend/src/pages/KnowledgeBaseFeedbackPage.tsx`
- Delete: `frontend/src/components/knowledge-base/FeedbackStatsBar.tsx`
- Delete: `frontend/src/components/knowledge-base/FeedbackFilters.tsx`
- Delete: `frontend/src/components/knowledge-base/FeedbackTable.tsx`
- Delete: `frontend/src/components/knowledge-base/FeedbackDetailModal.tsx`

- [ ] **Step 1: Rewrite KnowledgeBaseFeedbackPage to use generic components**

Replace the entire file content:

```tsx
// frontend/src/pages/KnowledgeBaseFeedbackPage.tsx
import React, { useState } from 'react';
import { MessageSquare } from 'lucide-react';
import {
  FeedbackLogSummary,
  GetFeedbackListParams,
  useKnowledgeBaseFeedbackListQuery,
} from '../api/hooks/useKnowledgeBase';
import GenericFeedbackStatsBar from '../components/feedback/GenericFeedbackStatsBar';
import GenericFeedbackFilters from '../components/feedback/GenericFeedbackFilters';
import GenericFeedbackTable from '../components/feedback/GenericFeedbackTable';
import GenericFeedbackDetailModal from '../components/feedback/GenericFeedbackDetailModal';
import type { FeedbackDetail } from '../components/feedback/types';
import { SORT_COLUMNS } from '../components/feedback/types';

const defaultParams: GetFeedbackListParams = {
  pageNumber: 1,
  pageSize: 20,
  sortBy: 'CreatedAt',
  sortDescending: true,
};

function mapLogToDetail(log: FeedbackLogSummary): FeedbackDetail {
  return {
    id: log.id,
    primaryText: log.question,
    secondaryText: log.answer,
    createdAt: log.createdAt,
    userId: log.userId ?? undefined,
    precisionScore: log.precisionScore,
    styleScore: log.styleScore,
    hasFeedback: log.hasFeedback,
    feedbackComment: log.feedbackComment,
    extra: (
      <div className="grid grid-cols-3 gap-4 text-sm">
        <div>
          <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">TopK</p>
          <p className="text-gray-900">{log.topK}</p>
        </div>
        <div>
          <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Zdrojů</p>
          <p className="text-gray-900">{log.sourceCount}</p>
        </div>
        <div>
          <p className="text-xs text-gray-500 uppercase tracking-wide mb-1">Doba odezvy</p>
          <p className="text-gray-900">{log.durationMs} ms</p>
        </div>
      </div>
    ),
  };
}

const KnowledgeBaseFeedbackPage: React.FC = () => {
  const [params, setParams] = useState<GetFeedbackListParams>(defaultParams);
  const [selectedLog, setSelectedLog] = useState<FeedbackLogSummary | null>(null);

  const { data, isLoading, isError } = useKnowledgeBaseFeedbackListQuery(params);

  const rows: FeedbackDetail[] = (data?.logs ?? []).map((log) => ({
    id: log.id,
    primaryText: log.question,
    secondaryText: log.answer.slice(0, 120),
    createdAt: log.createdAt,
    userId: log.userId ?? undefined,
    precisionScore: log.precisionScore,
    styleScore: log.styleScore,
    hasFeedback: log.hasFeedback,
    feedbackComment: log.feedbackComment,
  }));

  const stats = data?.stats
    ? {
        totalItems: data.stats.totalQuestions,
        totalWithFeedback: data.stats.totalWithFeedback,
        avgPrecisionScore: data.stats.avgPrecisionScore,
        avgStyleScore: data.stats.avgStyleScore,
      }
    : undefined;

  return (
    <div className="flex flex-col h-full">
      <div className="px-6 py-4 border-b border-gray-200 flex items-center gap-3 flex-shrink-0">
        <MessageSquare className="w-6 h-6 text-blue-600" />
        <h1 className="text-2xl font-semibold text-gray-900">Feedback</h1>
      </div>

      <div className="flex-1 overflow-y-auto p-6 space-y-4">
        <GenericFeedbackStatsBar stats={stats} isLoading={isLoading} itemLabel="dotazů" />

        <GenericFeedbackFilters
          hasFeedback={params.hasFeedback}
          sortBy={params.sortBy ?? 'CreatedAt'}
          sortDescending={params.sortDescending ?? true}
          pageSize={params.pageSize ?? 20}
          allowedSortColumns={[...SORT_COLUMNS]}
          onHasFeedbackChange={(v) => setParams((p) => ({ ...p, pageNumber: 1, hasFeedback: v }))}
          onSortByChange={(v) => setParams((p) => ({ ...p, pageNumber: 1, sortBy: v }))}
          onSortDescendingChange={(v) => setParams((p) => ({ ...p, pageNumber: 1, sortDescending: v }))}
          onPageSizeChange={(v) => setParams((p) => ({ ...p, pageNumber: 1, pageSize: v }))}
        />

        {isError && (
          <div className="flex items-center justify-center h-32 text-sm text-red-600">
            Nepodařilo se načíst záznamy. Zkuste to znovu.
          </div>
        )}

        {!isError && (
          <GenericFeedbackTable
            rows={rows}
            isLoading={isLoading}
            totalCount={data?.totalCount ?? 0}
            pageNumber={data?.pageNumber ?? 1}
            pageSize={params.pageSize ?? 20}
            totalPages={data?.totalPages ?? 1}
            onPageChange={(page) => {
              setParams((p) => ({ ...p, pageNumber: page }));
              setSelectedLog(null);
            }}
            onRowClick={(id) => {
              const log = data?.logs.find((l) => l.id === id) ?? null;
              setSelectedLog((prev) => (prev?.id === id ? null : log));
            }}
            primaryLabel="Dotaz"
          />
        )}
      </div>

      {selectedLog && (
        <GenericFeedbackDetailModal
          detail={mapLogToDetail(selectedLog)}
          onClose={() => setSelectedLog(null)}
          primaryLabel="Dotaz"
          secondaryLabel="Odpověď"
        />
      )}
    </div>
  );
};

export default KnowledgeBaseFeedbackPage;
```

- [ ] **Step 2: Delete the four old KB-specific component files**

```bash
git rm frontend/src/components/knowledge-base/FeedbackStatsBar.tsx \
       frontend/src/components/knowledge-base/FeedbackFilters.tsx \
       frontend/src/components/knowledge-base/FeedbackTable.tsx \
       frontend/src/components/knowledge-base/FeedbackDetailModal.tsx
```

- [ ] **Step 3: Run full test suite to confirm nothing broke**

```bash
cd frontend && react-scripts test --watchAll=false 2>&1 | tail -20
# Expected: all tests pass, no import errors for deleted files
```

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/KnowledgeBaseFeedbackPage.tsx
git commit -m "refactor: migrate KnowledgeBaseFeedbackPage to generic feedback components"
```

---

## Task 13: Build and lint verification

**Files:** none

- [ ] **Step 1: Run TypeScript build**

```bash
cd frontend && npm run build 2>&1 | tail -20
# Expected: "Compiled successfully." or "Build complete."
```

- [ ] **Step 2: Run lint**

```bash
cd frontend && npm run lint 2>&1 | tail -20
# Expected: no errors
```

- [ ] **Step 3: Fix any type errors or lint issues, then commit if changes were needed**

If no fixes needed, skip this step. If fixes were needed:

```bash
git add -p
git commit -m "fix: address build and lint issues in feedback components"
```
