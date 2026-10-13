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

