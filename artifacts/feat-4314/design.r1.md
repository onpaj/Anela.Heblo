# Design: PackingStatsTile drill-down consistency fix

This feature has no user-facing UI/UX change (architect's assessment: `Skip Design: true`; spec FR-4 requires the rendered tile and its click behavior to be visually and functionally identical to today). The design below therefore covers component contracts and data shapes only — no wireframes, no new visual components.

## Component Design

### `PackingStatsTile` (`frontend/src/components/dashboard/tiles/PackingStatsTile.tsx`)

**Responsibility (unchanged):** render the 3-stat grid (orders being processed / being packed / packed today) and the per-packer breakdown list, from `PackingStatsData`.

**Responsibility (changed):** derive clickability, tooltip, and navigation target using the shared `frontend/src/utils/urlUtils.ts` helpers instead of its own ad-hoc logic, and accept the navigation target as a prop rather than hardcoding it.

**Props contract (after):**

```ts
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
  targetUrl?: string; // Base URL this tile navigates to (frontend-owned route)
}
```

Notes:
- `PackerStat` and `PackingStatsData` are unchanged from today — internal render logic for the stat grid and packer list is untouched.
- The custom `drillDown?: { enabled: boolean; tooltip?: string }` field is removed entirely; `drillDown` now comes from `TileDataWithDrillDown` (`{ filters?, enabled, tooltip? }`), matching what the backend already emits.
- `status` becomes optional (`status?: string`) to match `TileDataWithDrillDown`'s base shape exactly, consistent with how `InventorySummaryTile` and `CountTile` type it — this is a type-only relaxation with no runtime effect, since the existing `if (data.status === 'error')` check still works unchanged for `undefined`.

**Internal logic contract (after):**

```ts
export const PackingStatsTile: React.FC<PackingStatsTileProps> = ({ data, targetUrl }) => {
  const navigate = useNavigate();

  const isClickable = isTileClickable(data);
  const tooltip = getTileTooltip(data);

  const handleClick = () => {
    if (isClickable && targetUrl && data.drillDown?.filters) {
      navigate(createFilteredUrl(targetUrl, data.drillDown.filters));
    }
  };

  // ...existing error/stats-null guards, unchanged...

  return (
    <div
      className={`h-full flex flex-col gap-3 ${isClickable ? 'cursor-pointer' : ''}`}
      onClick={isClickable ? handleClick : undefined}
      title={tooltip}
    >
      {/* ...existing stat grid + packer list markup, unchanged... */}
    </div>
  );
};
```

This is structurally identical to `InventorySummaryTile`'s `handleClick`/`isClickable`/`tooltip` block — same helper calls, same guard condition, same JSX wiring pattern. No new component boundaries are introduced.

### `tileRegistry.tsx` (`frontend/src/components/dashboard/tiles/tileRegistry.tsx`)

**Responsibility (unchanged):** map tile type keys to renderers, supplying each tile's frontend-owned `targetUrl` where applicable — this file already does this for every `CountTile`/`InventorySummaryTile` entry.

**Change:** the `packingstats` entry gains the `targetUrl` prop, consistent with every other drill-down-capable entry in the same file:

```ts
packingstats: ({ data }) => <PackingStatsTile data={data} targetUrl="/baleni" />,
```

## Data Schemas

No backend/API schema change. Reused, unmodified frontend types (`frontend/src/utils/urlUtils.ts`):

```ts
export interface DrillDownInfo {
  filters?: Record<string, any>;
  enabled: boolean;
  tooltip?: string;
}

export interface TileDataWithDrillDown {
  status?: string;
  data?: { count?: number; [key: string]: any };
  error?: string;
  drillDown?: DrillDownInfo;
  [key: string]: any;
}
```

Backend payload shape (`Anela.Heblo.Application/Features/Packaging/DashboardTiles/PackingStatsTile.cs`, unchanged, already matches `DrillDownInfo`):

```json
{
  "status": "success",
  "data": {
    "ordersBeingPackedCount": 3,
    "ordersBeingProcessedCount": 7,
    "ordersBeingPackedCountLastSync": "2026-09-26T08:00:00+02:00",
    "totalOrdersPackedToday": 42,
    "packedByPacker": [
      { "packerId": "u1", "packerName": "Jana", "orderCount": 20 }
    ]
  },
  "metadata": { "lastUpdated": "2026-09-26T08:00:00+02:00", "source": "PackageRepository" },
  "drillDown": { "filters": {}, "enabled": true, "tooltip": "Přejít do modulu Balení" }
}
```
