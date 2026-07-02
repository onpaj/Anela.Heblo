### task: extract-tile-registry-and-slim-dispatcher

**Files:**
- Create: `frontend/src/components/dashboard/tiles/tileRegistry.tsx`
- Modify: `frontend/src/components/dashboard/tiles/TileContent.tsx` (full file, currently 96 lines)
- Test: `frontend/src/components/dashboard/tiles/__tests__/TileContent.test.tsx` (existing, must pass **unmodified** — this is the acceptance contract; do not edit it)

This is a single self-contained refactor of one file into two (registry + slim dispatcher), verified entirely by the existing test suite. Per the spec/arch-review, it is not split into multiple tasks.

- [ ] Step 1: Confirm the baseline test suite passes before touching anything. Run:
  ```
  cd frontend && CI=true npx react-scripts test src/components/dashboard/tiles/__tests__/TileContent.test.tsx --watchAll=false
  ```
  Expect: PASS, 20 tests passing (this is the pre-refactor baseline — if it fails now, stop and investigate before proceeding, since the refactor must not be blamed for a pre-existing failure).

- [ ] Step 2: Create `frontend/src/components/dashboard/tiles/tileRegistry.tsx` with the full registry, transcribing all 23 cases verbatim from the current switch in `TileContent.tsx` (lines 34-94 as read from the current file). Use this exact content:

  ```tsx
  import React from 'react';
  import { DashboardTile as DashboardTileType } from '../../../api/hooks/useDashboard';
  import { BackgroundTasksTile } from './BackgroundTasksTile';
  import { ProductionTile } from './ProductionTile';
  import { ConditionsTile } from './ConditionsTile';
  import { ManualActionRequiredTile } from './ManualActionRequiredTile';
  import { PurchaseOrdersInTransitTile } from './PurchaseOrdersInTransitTile';
  import { CountTile } from './CountTile';
  import { InventorySummaryTile } from './InventorySummaryTile';
  import { LowStockAlertTile } from './LowStockAlertTile';
  import { DataQualityTile } from './DataQualityTile';
  import { DqtYesterdayStatusTile } from './DqtYesterdayStatusTile';
  import { WeatherForecastTile } from './WeatherForecastTile';
  import { FailedJobsTile } from './FailedJobsTile';
  import { PackingStatsTile } from './PackingStatsTile';
  import { Truck, PackageCheck, Package, FileText, Landmark, ClipboardList, Beaker, AlertTriangle, Gift } from 'lucide-react';

  export type TileRenderer = React.FC<{ data: any; tile: DashboardTileType }>;

  export const TILE_RENDERERS: Record<string, TileRenderer> = {
    backgroundtaskstatus: ({ data }) => <BackgroundTasksTile data={data} />,
    todayproduction: ({ data, tile }) => <ProductionTile data={data} title={tile.title || 'Dnes'} />,
    nextdayproduction: ({ data, tile }) => <ProductionTile data={data} title={tile.title || 'Zítra'} />,
    // Manufacture tiles
    manufactureconditions: ({ data }) => <ConditionsTile data={data} />,
    manualactionrequired: ({ data, tile }) => (
      <ManualActionRequiredTile data={data} tileCategory={tile.category} tileTitle={tile.title} />
    ),
    // Purchase tiles
    purchaseordersintransit: ({ data, tile }) => (
      <PurchaseOrdersInTransitTile data={data} tileCategory={tile.category} tileTitle={tile.title} />
    ),
    // Transport tiles
    intransitboxes: ({ data, tile }) => (
      <CountTile
        data={data}
        icon={<Truck className="h-10 w-10" />}
        iconColor="text-blue-600"
        tileCategory={tile.category}
        tileTitle={tile.title}
        targetUrl="/logistics/transport-boxes"
      />
    ),
    receivedboxes: ({ data, tile }) => (
      <CountTile
        data={data}
        icon={<PackageCheck className="h-10 w-10" />}
        iconColor="text-green-600"
        tileCategory={tile.category}
        tileTitle={tile.title}
        targetUrl="/logistics/transport-boxes"
      />
    ),
    errorboxes: ({ data, tile }) => (
      <CountTile
        data={data}
        icon={<Package className="h-10 w-10" />}
        iconColor="text-indigo-600"
        tileCategory={tile.category}
        tileTitle={tile.title}
        targetUrl="/logistics/transport-boxes"
      />
    ),
    // Statistics tiles
    invoiceimportstatistics: ({ data, tile }) => (
      <CountTile
        data={data}
        icon={<FileText className="h-10 w-10" />}
        iconColor="text-amber-600"
        tileCategory={tile.category}
        tileTitle={tile.title}
        targetUrl="/automation/invoice-import-statistics"
      />
    ),
    bankstatementimportstatistics: ({ data, tile }) => (
      <CountTile
        data={data}
        icon={<Landmark className="h-10 w-10" />}
        iconColor="text-emerald-600"
        tileCategory={tile.category}
        tileTitle={tile.title}
        targetUrl="/finance/bank-statements"
      />
    ),
    // Inventory tiles
    productinventorycount: ({ data, tile }) => (
      <CountTile
        data={data}
        icon={<ClipboardList className="h-10 w-10" />}
        iconColor="text-purple-600"
        tileCategory={tile.category}
        tileTitle={tile.title}
        targetUrl="/logistics/inventory"
      />
    ),
    materialinventorycount: ({ data, tile }) => (
      <CountTile
        data={data}
        icon={<Beaker className="h-10 w-10" />}
        iconColor="text-teal-600"
        tileCategory={tile.category}
        tileTitle={tile.title}
        targetUrl="/manufacturing/inventory"
      />
    ),
    productinventorysummary: ({ data }) => (
      <InventorySummaryTile data={data} targetUrl="/logistics/inventory" />
    ),
    materialwithexpirationinventorysummary: ({ data }) => (
      <InventorySummaryTile data={data} targetUrl="/manufacturing/inventory" />
    ),
    materialwithoutexpirationinventorysummary: ({ data }) => (
      <InventorySummaryTile data={data} targetUrl="/manufacturing/inventory" />
    ),
    // Purchase efficiency tiles
    lowstockefficiency: ({ data, tile }) => (
      <CountTile
        data={data}
        icon={<AlertTriangle className="h-10 w-10" />}
        iconColor="text-orange-600"
        tileCategory={tile.category}
        tileTitle={tile.title}
        targetUrl="/purchase/stock-analysis"
      />
    ),
    // Gift package tiles
    criticalgiftpackages: ({ data, tile }) => (
      <CountTile
        data={data}
        icon={<Gift className="h-10 w-10" />}
        iconColor="text-red-600"
        tileCategory={tile.category}
        tileTitle={tile.title}
        targetUrl="/logistics/gift-package-manufacturing"
      />
    ),
    // Low stock alert tile
    lowstockalert: ({ data }) => <LowStockAlertTile data={data} />,
    // Data quality tile
    dataqualitystatus: ({ data }) => <DataQualityTile data={data} />,
    dqtyesterdaystatus: ({ data }) => <DqtYesterdayStatusTile data={data} />,
    weatherforecast: ({ data }) => <WeatherForecastTile data={data} />,
    failedjobs: ({ data }) => <FailedJobsTile data={data} />,
    packingstats: ({ data }) => <PackingStatsTile data={data} />,
  };
  ```

  Notes while writing this file:
  - Keep the same relative import paths as the current `TileContent.tsx` (e.g. `./BackgroundTasksTile`, `./CountTile`) — the test file's `jest.mock('../BackgroundTasksTile', ...)` calls resolve by absolute module path, not by importer, so these must stay identical for mocks to keep intercepting.
  - The `DashboardTileType` import path from `tileRegistry.tsx` is `../../../api/hooks/useDashboard` (this file lives at `frontend/src/components/dashboard/tiles/tileRegistry.tsx`, three levels up from `frontend/src/api/hooks/useDashboard.ts` — same relative path as the current import in `TileContent.tsx`).
  - Do not import `DefaultTile`, `LoadingTile`, or `UnauthorizedTile` here — those stay in `TileContent.tsx` only.
  - Do not import `TileContent.tsx` from this file (one-directional dependency, per spec FR-1 acceptance criteria).

- [ ] Step 3: Replace the entire contents of `frontend/src/components/dashboard/tiles/TileContent.tsx` with:
  ```tsx
  import React from 'react';
  import { DashboardTile as DashboardTileType } from '../../../api/hooks/useDashboard';
  import { LoadingTile } from './LoadingTile';
  import { UnauthorizedTile } from './UnauthorizedTile';
  import { DefaultTile } from './DefaultTile';
  import { TILE_RENDERERS } from './tileRegistry';

  interface TileContentProps {
    tile: DashboardTileType;
  }

  export const TileContent: React.FC<TileContentProps> = ({ tile }) => {
    if (tile.isUnauthorized) {
      return <UnauthorizedTile />;
    }

    if (!tile.data) {
      return <LoadingTile />;
    }

    const Renderer = TILE_RENDERERS[tile.tileId];
    return Renderer ? <Renderer data={tile.data} tile={tile} /> : <DefaultTile data={tile.data} />;
  };
  ```
  This removes all per-tile imports (`BackgroundTasksTile`, `ProductionTile`, `CountTile`, etc. and the `lucide-react` icon imports) from `TileContent.tsx` — they now live only in `tileRegistry.tsx`.

- [ ] Step 4: Run the existing test suite (must pass unmodified — this is the executable acceptance contract per spec FR-2):
  ```
  cd frontend && CI=true npx react-scripts test src/components/dashboard/tiles/__tests__/TileContent.test.tsx --watchAll=false
  ```
  Expect: PASS, all 20 tests passing, identical to the Step 1 baseline. If any test fails, treat it as a transcription bug in `tileRegistry.tsx` (wrong icon color, wrong `targetUrl`, dropped `tileCategory`/`tileTitle` prop, or a missing/misspelled `tileId` key) — do not modify the test file to make it pass.

- [ ] Step 5: Type-check and lint the two touched files:
  ```
  cd frontend && npx tsc --noEmit -p tsconfig.json && npx eslint src/components/dashboard/tiles/TileContent.tsx src/components/dashboard/tiles/tileRegistry.tsx
  ```
  Expect: no TypeScript errors, no new ESLint errors introduced by these two files.

- [ ] Step 6: Run the full frontend build to confirm nothing else references the removed per-tile imports/exports from `TileContent.tsx` (e.g. no other module imports `Truck`/`CountTile` re-exported through `TileContent.tsx`, which it never exported anyway):
  ```
  cd frontend && npm run build
  ```
  Expect: build succeeds with no compile errors.

- [ ] Step 7: Run the full frontend test suite once more to confirm no other test file was affected by this change (e.g. any test importing `TileContent` indirectly through `frontend/src/components/dashboard/tiles/index.ts` or the dashboard page):
  ```
  cd frontend && CI=true npx react-scripts test --watchAll=false
  ```
  Expect: PASS, no new failures relative to the pre-existing suite (any pre-existing unrelated failures are out of scope — only confirm nothing dashboard/tile-related regressed).

- [ ] Step 8: Run the project-standard frontend lint gate (per CLAUDE.md validation requirements):
  ```
  cd frontend && npm run lint
  ```
  Expect: no new lint errors attributable to `TileContent.tsx` or `tileRegistry.tsx`.

- [ ] Step 9: Verify the diff is surgical — only the two files above changed, nothing else:
  ```
  git status --short frontend/src/components/dashboard/tiles/
  ```
  Expect output:
  ```
   M frontend/src/components/dashboard/tiles/TileContent.tsx
  ?? frontend/src/components/dashboard/tiles/tileRegistry.tsx
  ```
  `frontend/src/components/dashboard/tiles/index.ts` and `__tests__/TileContent.test.tsx` must show no changes (per spec Out of Scope and FR-2 acceptance criteria).

- [ ] Step 10: Commit the refactor:
  ```
  git add frontend/src/components/dashboard/tiles/TileContent.tsx frontend/src/components/dashboard/tiles/tileRegistry.tsx
  git commit -m "refactor(dashboard): extract tile dispatch into TILE_RENDERERS registry

Replaces the switch(tile.tileId) in TileContent.tsx with a lookup
against a new tileRegistry.tsx module, so adding a new dashboard tile
no longer requires editing TileContent.tsx. No behavior change;
TileContent.test.tsx passes unmodified."
  ```
