# PackingStatsTile Drill-Down Consistency Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `PackingStatsTile` use the shared `urlUtils.ts` drill-down helpers (`isTileClickable`, `getTileTooltip`, `createFilteredUrl`, `TileDataWithDrillDown`) and a `targetUrl` prop from `tileRegistry.tsx`, instead of its own ad-hoc `{enabled, tooltip}` shape and a hardcoded `navigate('/baleni')`.

**Architecture:** No new components or files. `PackingStatsTile.tsx` swaps its private drill-down typing/logic for the same helpers `CountTile.tsx`/`InventorySummaryTile.tsx` already use, and gains an optional `targetUrl?: string` prop. `tileRegistry.tsx` passes `targetUrl="/baleni"` into it — the one line that moves the route string from the component to the frontend's routing source of truth. Backend (`PackingStatsTile.cs`) is untouched; it already emits the correct `{filters, enabled, tooltip}` shape.

**Tech Stack:** React + TypeScript, react-router-dom (`useNavigate`), Jest + `@testing-library/react` for tests.

---

## File Structure

- **Modify:** `frontend/src/components/dashboard/tiles/PackingStatsTile.tsx` — replace custom `drillDown` prop typing and click logic with `TileDataWithDrillDown` + `isTileClickable`/`getTileTooltip`/`createFilteredUrl`; add `targetUrl?: string` prop.
- **Modify:** `frontend/src/components/dashboard/tiles/tileRegistry.tsx` — pass `targetUrl="/baleni"` into the `packingstats` renderer entry.
- **Create:** `frontend/src/components/dashboard/tiles/__tests__/PackingStatsTile.test.tsx` — new test file (none currently exists for this tile), modeled on the existing sibling tests `DataQualityTile.test.tsx` / `FailedJobsTile.test.tsx`.
- No changes to `frontend/src/utils/urlUtils.ts` (helpers already exist and are correct) and no changes to any backend file.

---

### task: refactor-packing-stats-tile-drilldown

**Files:**
- Modify: `frontend/src/components/dashboard/tiles/PackingStatsTile.tsx` (whole file, currently 106 lines)
- Test: `frontend/src/components/dashboard/tiles/__tests__/PackingStatsTile.test.tsx` (new file)

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/dashboard/tiles/__tests__/PackingStatsTile.test.tsx` with this exact content:

```tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { PackingStatsTile } from '../PackingStatsTile';

const mockNavigate = jest.fn();
jest.mock('react-router-dom', () => {
  const actual = jest.requireActual('react-router-dom');
  return { ...actual, useNavigate: () => mockNavigate };
});

const baseStats = {
  ordersBeingPackedCount: 3,
  ordersBeingProcessedCount: 7,
  ordersBeingPackedCountLastSync: null,
  totalOrdersPackedToday: 42,
  packedByPacker: [{ packerId: 'u1', packerName: 'Jana', orderCount: 20 }],
};

const renderTile = (data: any, targetUrl?: string) =>
  render(
    <BrowserRouter>
      <PackingStatsTile data={data} targetUrl={targetUrl} />
    </BrowserRouter>,
  );

beforeEach(() => {
  mockNavigate.mockReset();
});

describe('PackingStatsTile', () => {
  it('renders the error state and is not clickable', () => {
    const { container } = renderTile({ status: 'error', error: 'Nepodařilo se načíst data balení' });

    expect(screen.getByText('Nepodařilo se načíst data balení')).toBeInTheDocument();
    fireEvent.click(container.firstChild as HTMLElement);
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  it('is not clickable when drillDown is absent', () => {
    const { container } = renderTile({ status: 'success', data: baseStats });

    const wrapper = container.firstChild as HTMLElement;
    expect(wrapper.className).not.toContain('cursor-pointer');
    fireEvent.click(wrapper);
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  it('is not clickable when drillDown.enabled is false', () => {
    const { container } = renderTile(
      { status: 'success', data: baseStats, drillDown: { enabled: false, filters: {}, tooltip: 'x' } },
      '/baleni',
    );

    const wrapper = container.firstChild as HTMLElement;
    expect(wrapper.className).not.toContain('cursor-pointer');
    fireEvent.click(wrapper);
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  it('navigates to targetUrl with no query string when filters is empty (current backend payload)', () => {
    const { container } = renderTile(
      { status: 'success', data: baseStats, drillDown: { enabled: true, filters: {}, tooltip: 'Přejít do modulu Balení' } },
      '/baleni',
    );

    const wrapper = container.firstChild as HTMLElement;
    expect(wrapper.className).toContain('cursor-pointer');
    expect(wrapper.getAttribute('title')).toBe('Přejít do modulu Balení');

    fireEvent.click(wrapper);
    expect(mockNavigate).toHaveBeenCalledWith('/baleni');
  });

  it('navigates with query params when filters is non-empty', () => {
    const { container } = renderTile(
      { status: 'success', data: baseStats, drillDown: { enabled: true, filters: { packerId: 'u1' }, tooltip: 'Přejít do modulu Balení' } },
      '/baleni',
    );

    fireEvent.click(container.firstChild as HTMLElement);
    expect(mockNavigate).toHaveBeenCalledWith('/baleni?packerId=u1');
  });

  it('does not navigate when targetUrl is not supplied', () => {
    const { container } = renderTile({
      status: 'success',
      data: baseStats,
      drillDown: { enabled: true, filters: {}, tooltip: 'Přejít do modulu Balení' },
    });

    fireEvent.click(container.firstChild as HTMLElement);
    expect(mockNavigate).not.toHaveBeenCalled();
  });

  it('renders the packer breakdown list', () => {
    renderTile(
      { status: 'success', data: baseStats, drillDown: { enabled: true, filters: {}, tooltip: 'x' } },
      '/baleni',
    );

    expect(screen.getByText('Jana')).toBeInTheDocument();
    expect(screen.getByText('20')).toBeInTheDocument();
    expect(screen.getByText('42')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && CI=true npx react-scripts test src/components/dashboard/tiles/__tests__/PackingStatsTile.test.tsx --watchAll=false`

Expected: FAIL. Specifically the empty-`filters`/non-empty-`filters`/no-`targetUrl` tests fail because today's `PackingStatsTile.tsx` reads `data.drillDown?.enabled` and unconditionally calls `navigate('/baleni')` (ignoring `targetUrl` and `filters` entirely), so:
- "navigates with query params when filters is non-empty" fails: `mockNavigate` is called with `'/baleni'` instead of `'/baleni?packerId=u1'`.
- "does not navigate when targetUrl is not supplied" fails: today's code still navigates to `/baleni` because it never checks `targetUrl`.

- [ ] **Step 3: Replace `PackingStatsTile.tsx` with the updated implementation**

Replace the full contents of `frontend/src/components/dashboard/tiles/PackingStatsTile.tsx` with:

```tsx
import React from 'react';
import { useNavigate } from 'react-router-dom';
import {
  createFilteredUrl,
  isTileClickable,
  getTileTooltip,
  TileDataWithDrillDown,
} from '../../../utils/urlUtils';

interface PackerStat {
  packerId: string | null;
  packerName: string;
  orderCount: number;
}

interface PackingStatsData {
  ordersBeingPackedCount: number | null;
  ordersBeingProcessedCount: number | null;
  ordersBeingPackedCountLastSync: string | null;
  totalOrdersPackedToday: number;
  packedByPacker: PackerStat[];
}

interface PackingStatsTileProps {
  data: TileDataWithDrillDown & {
    status?: string;
    error?: string;
    data?: PackingStatsData;
  };
  targetUrl?: string;
}

export const PackingStatsTile: React.FC<PackingStatsTileProps> = ({ data, targetUrl }) => {
  const navigate = useNavigate();

  if (data.status === 'error') {
    return (
      <div className="h-full flex items-center justify-center text-center">
        <p className="text-red-600 dark:text-red-400 text-sm">{data.error || 'Chyba při načítání dat'}</p>
      </div>
    );
  }

  const stats = data.data;
  if (!stats) return null;

  const isClickable = isTileClickable(data);
  const tooltip = getTileTooltip(data);

  const handleClick = () => {
    if (isClickable && targetUrl && data.drillDown?.filters) {
      navigate(createFilteredUrl(targetUrl, data.drillDown.filters));
    }
  };

  return (
    <div
      className={`h-full flex flex-col gap-3 ${isClickable ? 'cursor-pointer' : ''}`}
      onClick={isClickable ? handleClick : undefined}
      title={tooltip}
    >
      <div className="grid grid-cols-3 gap-3">
        <div className="bg-secondary-blue-pale dark:bg-graphite-surface-2 rounded-lg p-3 text-center">
          <p className="text-xs text-neutral-gray dark:text-graphite-muted mb-1">Vyřizuje se</p>
          <p className="text-2xl font-bold text-primary-blue dark:text-graphite-accent">
            {stats.ordersBeingProcessedCount ?? '—'}
          </p>
          {stats.ordersBeingPackedCountLastSync && (
            <p className="text-xs text-neutral-gray dark:text-graphite-muted mt-1">
              sync {new Date(stats.ordersBeingPackedCountLastSync).toLocaleTimeString('cs-CZ', { hour: '2-digit', minute: '2-digit' })}
            </p>
          )}
        </div>
        <div className="bg-secondary-blue-pale dark:bg-graphite-surface-2 rounded-lg p-3 text-center">
          <p className="text-xs text-neutral-gray dark:text-graphite-muted mb-1">Balí se</p>
          <p className="text-2xl font-bold text-primary-blue dark:text-graphite-accent">
            {stats.ordersBeingPackedCount ?? '—'}
          </p>
          {stats.ordersBeingPackedCountLastSync && (
            <p className="text-xs text-neutral-gray dark:text-graphite-muted mt-1">
              sync {new Date(stats.ordersBeingPackedCountLastSync).toLocaleTimeString('cs-CZ', { hour: '2-digit', minute: '2-digit' })}
            </p>
          )}
        </div>
        <div className="bg-secondary-blue-pale dark:bg-graphite-surface-2 rounded-lg p-3 text-center">
          <p className="text-xs text-neutral-gray dark:text-graphite-muted mb-1">Zabaleno dnes</p>
          <p className="text-2xl font-bold text-primary-blue dark:text-graphite-accent">
            {stats.totalOrdersPackedToday}
          </p>
        </div>
      </div>

      {stats.packedByPacker.length > 0 && (
        <ul className="space-y-1 flex-1 overflow-y-auto">
          {stats.packedByPacker.map((p) => (
            <li
              key={p.packerId ?? p.packerName}
              className="flex items-center justify-between py-1.5 px-2 bg-secondary-blue-pale dark:bg-graphite-surface-2 rounded"
            >
              <span className="text-xs font-medium text-neutral-slate dark:text-graphite-text truncate">{p.packerName}</span>
              <span className="text-xs font-bold text-primary-blue dark:text-graphite-accent ml-2 flex-shrink-0">{p.orderCount}</span>
            </li>
          ))}
        </ul>
      )}

      {stats.packedByPacker.length === 0 && (
        <p className="text-xs text-neutral-gray dark:text-graphite-muted italic">Dnes zatím nikdo nezabalil žádnou objednávku.</p>
      )}
    </div>
  );
};
```

Note: only the imports, the `PackingStatsTileProps` interface, and the clickability/tooltip/click-handler block change. The stat grid JSX and packer list JSX are copied verbatim from the current file — do not alter them.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && CI=true npx react-scripts test src/components/dashboard/tiles/__tests__/PackingStatsTile.test.tsx --watchAll=false`

Expected: PASS — all 7 tests green.

- [ ] **Step 5: Commit**

```bash
cd frontend
git add src/components/dashboard/tiles/PackingStatsTile.tsx src/components/dashboard/tiles/__tests__/PackingStatsTile.test.tsx
git commit -m "refactor(dashboard): PackingStatsTile uses shared drillDown helpers and targetUrl prop"
```

---

### task: wire-target-url-in-tile-registry

**Files:**
- Modify: `frontend/src/components/dashboard/tiles/tileRegistry.tsx:150` (the `packingstats` entry)

- [ ] **Step 1: Update the `packingstats` entry**

In `frontend/src/components/dashboard/tiles/tileRegistry.tsx`, find the line:

```tsx
  packingstats: ({ data }) => <PackingStatsTile data={data} />,
```

Replace it with:

```tsx
  packingstats: ({ data }) => <PackingStatsTile data={data} targetUrl="/baleni" />,
```

This is the only change in this file.

- [ ] **Step 2: Re-run the `PackingStatsTile` test suite**

Run: `cd frontend && CI=true npx react-scripts test src/components/dashboard/tiles/__tests__/PackingStatsTile.test.tsx --watchAll=false`

Expected: PASS (unaffected by this file — the test suite passes `targetUrl` directly as a prop and does not import `tileRegistry.tsx`; this step guards against an unrelated regression having crept in).

- [ ] **Step 3: Type-check and lint the frontend**

Run: `cd frontend && npm run build`
Expected: build succeeds with no new TypeScript errors (confirms `PackingStatsTileProps`'s new shape is compatible with how `tileRegistry.tsx` now calls it, and that `TileDataWithDrillDown`'s `data` prop is structurally satisfied by whatever `DashboardTileType`'s `data: any` passes through).

Run: `cd frontend && npm run lint`
Expected: no new lint errors introduced by either file.

- [ ] **Step 4: Run the full frontend test suite**

Run: `cd frontend && CI=true npm test -- --watchAll=false`
Expected: PASS — no existing test (in particular no dashboard/tile snapshot or integration test) regresses from the `targetUrl` addition.

- [ ] **Step 5: Commit**

```bash
cd frontend
git add src/components/dashboard/tiles/tileRegistry.tsx
git commit -m "feat(dashboard): pass targetUrl=/baleni into PackingStatsTile via tileRegistry"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (shared `TileDataWithDrillDown` typing + helpers, no hardcoded `navigate('/baleni')`, no custom `drillDown` field) → covered by `refactor-packing-stats-tile-drilldown` Step 3.
- FR-2 (`targetUrl?: string` prop, graceful no-op when absent) → covered by `refactor-packing-stats-tile-drilldown` Step 3 (prop declaration) and Step 1/4 tests ("does not navigate when targetUrl is not supplied").
- FR-3 (`tileRegistry.tsx` passes `targetUrl="/baleni"`) → covered by `wire-target-url-in-tile-registry` Step 1.
- FR-4 (no visible behavior change; empty `filters` yields no query string; non-enabled stays non-clickable) → covered by the "navigates to targetUrl with no query string...", "is not clickable when drillDown is absent", and "is not clickable when drillDown.enabled is false" tests in `refactor-packing-stats-tile-drilldown` Step 1.
- NFR-1 (structural consistency with `InventorySummaryTile`) → the Step 3 implementation uses the identical `isTileClickable`/`getTileTooltip`/`handleClick` shape as `InventorySummaryTile.tsx`.
- NFR-2 (no backend changes) → no backend file appears in either task's Files list.

**2. Placeholder scan:** No TBD/TODO/"add appropriate handling" phrases; every step shows complete, runnable code or an exact command with its expected output.

**3. Type consistency:** `PackingStatsTileProps`, `PackingStatsData`, and `PackerStat` are defined once (Step 3 of the first task) and used as-is by the test file (Step 1 of the same task) and by `tileRegistry.tsx` (second task, which only passes props — it does not reference the interface names directly, so there is no drift risk).

No gaps found; no additional tasks needed.
