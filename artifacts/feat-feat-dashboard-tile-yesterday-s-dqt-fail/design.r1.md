# Design: Dashboard Tile — Yesterday's DQT Status

## UX/UI Design

### States and Visual Layout

The tile shares the same container shape as `DataQualityTile` (flex column, centered, `min-h-44`, `cursor-pointer`, hover/active transition). The `error` state is non-interactive (no pointer, no hover), matching the sibling tile.

#### State: `no_data`
```
┌──────────────────────────┐
│                          │
│         🕐               │
│      Žádná data          │
│  Včerejší test neproběhl │
│                          │
└──────────────────────────┘
```
- `Clock` icon — `text-gray-400`
- `"Žádná data"` — `text-gray-500 text-sm`
- `"Včerejší test neproběhl"` — `text-gray-400 text-xs mt-1`

#### State: `error`
```
┌──────────────────────────┐
│                          │
│         ✕                │
│  Chyba při načítání dat  │
│                          │
└──────────────────────────┘
```
- `XCircle` icon — `text-red-500`
- `"Chyba při načítání dat"` — `text-red-600 text-sm`
- Not clickable (no `cursor-pointer`, no hover/active)

#### State: `warning` — `runStatus === 'Running'`
```
┌──────────────────────────┐
│                          │
│         🕐               │
│        probíhá           │
│      DD.MM.YYYY          │
│                          │
└──────────────────────────┘
```
- `Clock` icon — `text-amber-500`
- `"probíhá"` — `text-amber-600 text-sm`
- Yesterday's date formatted as `DD.MM.YYYY` (derived from `data.data.dateTo`) — `text-gray-400 text-xs mt-1`

#### State: `warning` — `runStatus === 'Completed'` with mismatches
```
┌──────────────────────────┐
│                          │
│         ⚠                │
│           4              │
│        neshod            │
│    z 123 faktur          │
│      DD.MM.YYYY          │
│                          │
└──────────────────────────┘
```
- `AlertTriangle` icon — `text-red-500`
- Mismatch count — `text-red-700 text-3xl font-bold mb-1`
- `"neshod"` — `text-gray-500 text-sm`
- `"z X faktur"` (when `totalChecked > 0`) — `text-gray-400 text-xs mt-1`
- Yesterday's date as `DD.MM.YYYY` — `text-gray-400 text-xs mt-1`

#### State: `success`
```
┌──────────────────────────┐
│                          │
│         🛡                │
│           0              │
│        vše OK            │
│    z 123 faktur          │
│      DD.MM.YYYY          │
│                          │
└──────────────────────────┘
```
- `ShieldCheck` icon — `text-green-500`
- `"0"` — `text-green-700 text-3xl font-bold mb-1`
- `"vše OK"` — `text-gray-500 text-sm`
- `"z X faktur"` (when `totalChecked > 0`) — `text-gray-400 text-xs mt-1`
- Yesterday's date as `DD.MM.YYYY` — `text-gray-400 text-xs mt-1`

### Component Hierarchy

```
DqtYesterdayStatusTile
└── div (container, click handler — all states except error)
    ├── [icon]           — varies by state
    ├── [count / label]  — varies by state
    └── [sub-labels]     — "z X faktur", date, "probíhá", etc.
```

### Key Interactions

- Click anywhere on the tile (all states except `error`) → `navigate('/automation/data-quality')`.
- The `error` state renders without hover styling and without a click handler, matching `DataQualityTile`.
- Date label for `warning`/`success` states: format `data.data.dateTo` (ISO string `YYYY-MM-DD`) as `DD.MM.YYYY`. If `dateTo` is absent, fall back to the static string `"včera"`.

---

## Component Design

### Backend

#### `DqtYesterdayStatusTile` (new)

**Location:** `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs`

**Implements:** `ITile`

**Constructor dependencies:**
| Parameter | Type |
|---|---|
| `repository` | `IDqtRunRepository` |
| `timeProvider` | `TimeProvider` |
| `logger` | `ILogger<DqtYesterdayStatusTile>` |

**Metadata properties:**
| Property | Value |
|---|---|
| `Title` | `"DQT včera"` |
| `Description` | `"Stav včerejšího DQT testu faktur"` |
| `Size` | `TileSize.Medium` |
| `Category` | `TileCategory.DataQuality` |
| `DefaultEnabled` | `true` |
| `AutoShow` | `false` |
| `ComponentType` | `typeof(object)` |
| `RequiredPermissions` | `Array.Empty<string>()` |

**Framework-derived tile id:** `"dqtyesterdaystatus"` — produced by `TileExtensions.GetTileId` (`Type.Name.ToLowerInvariant().Replace("tile", "")`). No override needed; the class name alone is sufficient. There is no `GetTileId()` method on `ITile`.

**`LoadDataAsync` responsibilities:**
1. Compute `yesterday = DateOnly.FromDateTime(_timeProvider.GetLocalNow().LocalDateTime).AddDays(-1)`
2. Call `_repository.GetLatestByTestTypeAndCoveredDateAsync(DqtTestType.IssuedInvoiceComparison, yesterday, ct)`
3. Map result to status payload per the truth table in Data Schemas
4. Wrap entire body in try/catch: on exception log with `_logger.LogError` (include exception, test type, target date) and return the error payload — never rethrow

#### `IDqtRunRepository` (modified)

**Location:** `backend/src/Anela.Heblo.Domain/Features/DataQuality/IDqtRunRepository.cs`

**New method appended to interface:**
```csharp
Task<DqtRun?> GetLatestByTestTypeAndCoveredDateAsync(
    DqtTestType testType,
    DateOnly coveredDate,
    CancellationToken cancellationToken = default);
```

**Semantics:** Returns the most recent run (by `StartedAt DESC`) of the given `testType` satisfying `DateFrom <= coveredDate && DateTo >= coveredDate`. Returns `null` when no such run exists. Does not load navigation properties (`Results` excluded).

#### `DqtRunRepository` (modified)

**Location:** `backend/src/Anela.Heblo.Persistence/DataQuality/DqtRunRepository.cs`

**Implementation:**
```csharp
public async Task<DqtRun?> GetLatestByTestTypeAndCoveredDateAsync(
    DqtTestType testType,
    DateOnly coveredDate,
    CancellationToken cancellationToken = default)
{
    return await DbSet
        .Where(r => r.TestType == testType
                    && r.DateFrom <= coveredDate
                    && r.DateTo >= coveredDate)
        .OrderByDescending(r => r.StartedAt)
        .FirstOrDefaultAsync(cancellationToken);
}
```

Ordering uses `StartedAt DESC` only, matching `GetLatestByTestTypeAsync`. No `.Include` — the tile reads only scalar fields on `DqtRun`. No index migration required (see Data Schemas).

#### `DashboardModule` (modified)

**Location:** `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs`

Append after the `DataQualityStatusTile` registration:
```csharp
services.RegisterTile<DqtYesterdayStatusTile>();
```

### Frontend

#### `DqtYesterdayStatusTile.tsx` (new)

**Location:** `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx`

**Responsibilities:**
- Accept `DqtYesterdayStatusTileProps` (see Data Schemas)
- Branch on `data.status` to render one of four visual states
- Within `warning`: further branch on `data.data?.runStatus === 'Running'` → Clock + `"probíhá"` vs. AlertTriangle + mismatch count
- Format `data.data?.dateTo` (`YYYY-MM-DD`) as `DD.MM.YYYY` for the date sub-label; fall back to `"včera"` when absent
- Attach `onClick → navigate('/automation/data-quality')` to the container for all states except `error`
- Source icons from `lucide-react`: `ShieldCheck`, `AlertTriangle`, `XCircle`, `Clock` (all already imported by `DataQualityTile.tsx`)

#### `TileContent.tsx` (modified)

**Location:** `frontend/src/components/dashboard/tiles/TileContent.tsx`

Add one import:
```typescript
import { DqtYesterdayStatusTile } from './DqtYesterdayStatusTile';
```

Add one switch case after `case 'dataqualitystatus'`:
```typescript
case 'dqtyesterdaystatus':
  return <DqtYesterdayStatusTile data={tile.data} />;
```

---

## Data Schemas

### Backend payload (`LoadDataAsync` return)

**Success / Warning:**
```json
{
  "status": "success" | "warning",
  "data": {
    "runId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "runStatus": "Completed" | "Running",
    "dateFrom": "2026-05-05",
    "dateTo": "2026-05-05",
    "totalChecked": 123,
    "totalMismatches": 4
  },
  "drillDown": { "href": "/automation/data-quality", "enabled": true }
}
```

**No data / Error:**
```json
{
  "status": "no_data" | "error",
  "data": null,
  "drillDown": { "href": "/automation/data-quality", "enabled": true }
}
```

### Status mapping truth table

| Run | `run.Status` | `run.TotalMismatches` | Response `status` |
|---|---|---|---|
| `null` | — | — | `"no_data"` |
| present | `Failed` | — | `"error"` |
| present | `Running` | — | `"warning"` |
| present | `Completed` | `> 0` | `"warning"` |
| present | `Completed` | `0` | `"success"` |
| (exception) | — | — | `"error"` (caught + logged) |

### Frontend props contract

```typescript
interface DqtYesterdayStatusTileProps {
  data: {
    status?: 'success' | 'warning' | 'error' | 'no_data';
    data?: {
      runId?: string;
      runStatus?: 'Completed' | 'Failed' | 'Running';
      dateFrom?: string;        // ISO date "YYYY-MM-DD"
      dateTo?: string;          // ISO date "YYYY-MM-DD"
      totalChecked?: number;
      totalMismatches?: number;
    } | null;
    drillDown?: {
      href: string;
      enabled: boolean;
    };
  };
}
```

> Note: the sibling `DataQualityTile.tsx` reads `data.data?.mismatchCount` — a pre-existing field name mismatch with its own backend. This tile uses `totalMismatches` consistently end-to-end and does not inherit that discrepancy.

### Repository EF Core query

```sql
SELECT TOP 1 *
FROM   DqtRuns
WHERE  TestType = @testType
  AND  DateFrom <= @coveredDate
  AND  DateTo   >= @coveredDate
ORDER BY StartedAt DESC
```

No new index or migration. The existing `IX_DqtRuns_TestType_StartedAt` narrows rows by test type; the date-range predicate operates over small cardinality (≈1–few rows per test type per day). Revisit only if `DqtRuns` exceeds ~10 000 rows.
