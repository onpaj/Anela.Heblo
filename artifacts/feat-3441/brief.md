## Module
Dashboard

## Finding
`frontend/src/components/dashboard/tiles/TileContent.tsx` (lines 34–94) dispatches to tile-specific React components via a `switch (tile.tileId)` with 20+ string-literal cases:

```tsx
switch (tile.tileId) {
  case 'backgroundtaskstatus': return <BackgroundTaskStatusTile data={data} />;
  case 'todayproduction':      return <TodayProductionTile data={data} />;
  case 'nextdayproduction':    return <NextDayProductionTile data={data} />;
  case 'manufactureconditions': return <ManufactureConditionsTile data={data} />;
  // … 16 more cases …
  default: return <DefaultTile />;
}
```

Every time a new tile is registered on the backend, this file must be modified. With the tile count growing (20+ already), the switch is a recurring churn hotspot.

## Why it matters
Open/Closed principle: the component is closed for extension without modification. As the Dashboard tile catalogue expands, each new tile registration requires a code change here — creating a predictable merge-conflict location and the risk of forgetting the frontend registration.

## Suggested fix
Replace the switch with a static record-based registry:

```tsx
// tileRegistry.ts (co-located with TileContent)
import type { DashboardTile } from '../../../api/hooks/useDashboard';

type TileRenderer = React.FC<{ data: any; tile: DashboardTile }>;

export const TILE_RENDERERS: Record<string, TileRenderer> = {
  backgroundtaskstatus: ({ data }) => <BackgroundTaskStatusTile data={data} />,
  todayproduction:      ({ data, tile }) => <TodayProductionTile data={data} tile={tile} />,
  // …
};
```

```tsx
// TileContent.tsx
const Renderer = TILE_RENDERERS[tile.tileId] ?? DefaultTile;
return <Renderer data={data} tile={tile} />;
```

New tiles are registered by adding an entry to `TILE_RENDERERS` — `TileContent.tsx` itself never changes.

---
_Filed by daily arch-review routine on 2026-06-30._
