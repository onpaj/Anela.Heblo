# Specification: Dashboard Tile — Yesterday's DQT Status

## Summary

Add a new dashboard tile (`dqtyesterdaystatus`) that surfaces the result of the previous day's automated Data Quality Test (DQT) run for issued invoices. The tile is scoped to a fixed period — yesterday in the server's local date — and is distinct from the existing `dataqualitystatus` tile, which always shows the most recent run regardless of date. Users opening the dashboard each morning can immediately see whether yesterday's invoice DQT detected mismatches, failed, or did not run.

## Background

The DQT pipeline runs on a schedule and compares issued invoices for a given date range. The existing **Kvalita dat** tile (`DataQualityStatusTile`) calls `IDqtRunRepository.GetLatestByTestTypeAsync` and shows whatever run is most recent — this is non-deterministic from a user perspective: it could be a run from today (manually triggered), yesterday (scheduled), or older if the scheduler failed. Operators want a stable, predictable "did yesterday's check pass?" indicator so a missing or failed run is unambiguous and visible at a glance.

The new tile reuses the existing dashboard tile pipeline (`ITile`, `RegisterTile<T>`, `GET /api/dashboard/data`), the existing `DqtRun` entity, and the existing `IDqtRunRepository`. No new endpoints, no new database tables, and no schema migrations are required.

## Functional Requirements

### FR-1: Backend tile class `DqtYesterdayStatusTile`

A new `ITile` implementation lives at `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs` and mirrors the structure of `DataQualityStatusTile`.

**Tile metadata:**
- `GetTileId()` → `"dqtyesterdaystatus"` (hardcoded constant matching the convention used by other tiles; the `tileId` is derived by the dashboard registration pipeline from the class name unless explicitly overridden — implementation must verify and align with existing tile registration mechanics).
- `Title` → `"DQT včera"`
- `Description` → `"Stav včerejšího DQT testu faktur"`
- `Size` → `TileSize.Medium` (same as `DataQualityStatusTile`)
- `Category` → `TileCategory.DataQuality`
- `DefaultEnabled` → `true`
- `AutoShow` → `false`
- `RequiredPermissions` → empty array

**Acceptance criteria:**
- Tile is discoverable via the dashboard tile registry once registered.
- Tile metadata fields match the values listed above.
- The class depends on `IDqtRunRepository` and a clock abstraction (or `TimeProvider` / `DateTimeOffset.Now` — see NFR-3) injected via constructor.

### FR-2: Repository extension — query DQT runs by covered date

The repository must expose a way to find the DQT run(s) whose `[DateFrom, DateTo]` range includes a target date. The brief allows either filtering through `GetPaginatedAsync` or adding a dedicated method. **The chosen approach is a new method** to keep the tile's query intent explicit and avoid coupling tile loading to pagination semantics.

**New repository method:**

```csharp
// backend/src/Anela.Heblo.Domain/Features/DataQuality/IDqtRunRepository.cs
Task<DqtRun?> GetLatestByTestTypeAndCoveredDateAsync(
    DqtTestType testType,
    DateOnly coveredDate,
    CancellationToken cancellationToken = default);
```

**Semantics:**
- Returns the most recent run (by `CompletedAt` desc, falling back to `StartedAt` desc when `CompletedAt` is null) whose covered range satisfies `DateFrom <= coveredDate <= DateTo`.
- Filters by the supplied `testType` (`DqtTestType.IssuedInvoiceComparison` for this tile).
- Returns `null` when no such run exists.

**Implementation lives in** `backend/src/Anela.Heblo.Persistence/DataQuality/DqtRunRepository.cs` and uses EF Core with parameterized predicates.

**Acceptance criteria:**
- Returns the correct run when a single run covers yesterday.
- Returns the most recently completed run when multiple runs cover yesterday.
- Returns `null` when no run covers yesterday.
- Does not include `Results` navigation in the returned entity (tile only needs aggregate counts).
- Method is added to the `IDqtRunRepository` interface and implemented in `DqtRunRepository`.

### FR-3: Tile data loading — `LoadDataAsync`

`DqtYesterdayStatusTile.LoadDataAsync` returns a payload shaped exactly like `DataQualityStatusTile`'s response so the frontend tile component can be a direct adaptation.

**Yesterday computation:**
- Compute `yesterday = DateOnly.FromDateTime(now.LocalDateTime).AddDays(-1)` where `now` is sourced from a `TimeProvider` injected via DI (see NFR-3) — falls back to `DateTimeOffset.Now` if no provider is available.
- "Local" means the server's configured timezone (the Heblo deployment runs in Europe/Prague). The DQT job runs in the same timezone, so this is consistent.

**Status mapping:**

| Condition | `status` |
| --- | --- |
| No run covers yesterday | `"no_data"` |
| Run exists, `Status == DqtRunStatus.Failed` | `"error"` |
| Run exists, `Status == DqtRunStatus.Running` | `"warning"` (still in progress — partial data) |
| Run exists, `Status == DqtRunStatus.Completed`, `TotalMismatches > 0` | `"warning"` |
| Run exists, `Status == DqtRunStatus.Completed`, `TotalMismatches == 0` | `"success"` |
| Repository throws | `"error"` (caught, logged, no exception propagated) |

**Payload (success / warning case):**

```json
{
  "status": "success" | "warning" | "error" | "no_data",
  "data": {
    "runId": "<guid>",
    "runStatus": "Completed" | "Failed" | "Running",
    "dateFrom": "YYYY-MM-DD",
    "dateTo": "YYYY-MM-DD",
    "totalChecked": 123,
    "totalMismatches": 4
  },
  "drillDown": { "href": "/automation/data-quality", "enabled": true }
}
```

**Payload (no_data / error case):**

```json
{
  "status": "no_data" | "error",
  "data": null,
  "drillDown": { "href": "/automation/data-quality", "enabled": true }
}
```

> Note: the existing `DataQualityStatusTile` uses `drillDown.href = "/data-quality"`, but the brief and frontend code (`DataQualityTile.tsx` line 22) navigate to `/automation/data-quality`. The new tile uses `/automation/data-quality` to match the brief and the actual route.

**Acceptance criteria:**
- Returns the correct `status` value for each condition above.
- Catches exceptions from the repository and returns the error payload — never throws to the caller.
- Logs the exception with structured context (`ILogger<DqtYesterdayStatusTile>`).
- `dateFrom` / `dateTo` are serialized as ISO date strings (`YYYY-MM-DD`).

### FR-4: DI registration

`DashboardModule.AddDashboardModule` (`backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs`) must register the new tile via `services.RegisterTile<DqtYesterdayStatusTile>()`.

**Acceptance criteria:**
- The tile appears in the response of `GET /api/dashboard/tiles` (or whichever endpoint exposes the tile registry).
- The tile is loaded by `GET /api/dashboard/data` when enabled.
- DI resolution succeeds at app startup (verified by integration test).

### FR-5: Frontend tile component

A new component lives at `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx` and renders the tile's four states (`no_data`, `error`, `warning`, `success`).

**Visual differences vs. `DataQualityTile.tsx`:**
- A short "yesterday" subtitle (e.g. `"včera"` or the formatted yesterday date `DD.MM.YYYY`) is shown beneath the mismatch count, replacing the date-range line that the existing tile shows.
- For the `warning` state where `runStatus === 'Running'`, render a `Clock` icon (instead of `AlertTriangle`) and the label `"probíhá"` to disambiguate "still running" from "completed with mismatches".
- All other visuals (icons, colors, click target, accessibility) match `DataQualityTile.tsx`.

**Click behavior:**
- Clicking anywhere on the tile navigates to `/automation/data-quality` via `useNavigate`.

**Acceptance criteria:**
- Renders the correct icon and copy for each of the four states.
- Distinguishes "Running" from "Completed with mismatches" visually.
- Clicking navigates to `/automation/data-quality`.
- Component matches the design and accessibility behavior of `DataQualityTile.tsx` (touch targets, hover/active states, screen reader–friendly text).

### FR-6: Frontend dispatcher registration

`frontend/src/components/dashboard/tiles/TileContent.tsx` adds a new `case 'dqtyesterdaystatus'` that renders `<DqtYesterdayStatusTile data={tile.data} />`. Imports are updated accordingly.

**Acceptance criteria:**
- Switch case for `'dqtyesterdaystatus'` exists and renders the new component.
- No regression to the existing `'dataqualitystatus'` rendering.

### FR-7: Settings panel discoverability

The tile must appear in the dashboard settings panel (`DashboardSettings.tsx`) so users can enable/disable it. The settings panel is data-driven from the tile registry returned by the backend, so this requirement is satisfied automatically by FR-4 — no frontend settings changes are expected.

**Acceptance criteria:**
- Tile appears in the settings panel under category "Kvalita dat" (`TileCategory.DataQuality`).
- Tile can be toggled on/off and the preference persists per user.

## Non-Functional Requirements

### NFR-1: Performance

- `LoadDataAsync` must complete in under **300 ms p95** under typical load. The query is a single indexed lookup over `DqtRun` filtered by `TestType` and a date range.
- A database index on `(TestType, DateFrom, DateTo)` should already exist or be added if missing — verify in `DqtRunConfiguration.cs`. If missing, add a migration. If a migration is required, document it; migrations on this project are manual.
- The tile's payload is small (< 1 KB) and adds negligible bandwidth to the dashboard response.

### NFR-2: Security

- No new endpoints; the existing dashboard endpoint authentication and authorization apply.
- `RequiredPermissions` is intentionally empty, matching the existing DQT tile.
- No PII or sensitive data is exposed beyond the existing run aggregates.
- The repository query uses parameterized EF Core predicates — no SQL injection surface.

### NFR-3: Time source

- The "yesterday" calculation must be testable. Inject `TimeProvider` (added to .NET 8) so unit tests can supply a fake clock.
- Use `timeProvider.GetLocalNow().Date` to derive yesterday in the server's local timezone.

### NFR-4: Observability

- Errors from the repository are logged with `ILogger.LogError` including the exception, the test type, and the target date.
- A `LogDebug` line on successful loads helps trace dashboard refreshes during development; not required for production logging.

### NFR-5: Internationalization

- All user-facing strings in the tile (`"DQT včera"`, `"včera"`, `"neshod"`, `"probíhá"`, `"Žádná data"`) are Czech, matching the rest of the application. No i18n framework changes required.

### NFR-6: Testing

- Backend unit tests cover `DqtYesterdayStatusTile.LoadDataAsync` for all five branches in the status-mapping table (no_data, success, warning–mismatches, warning–running, error–failed run, error–exception). Use Moq for `IDqtRunRepository` and a fake `TimeProvider`.
- Backend unit tests cover the new repository method `GetLatestByTestTypeAndCoveredDateAsync` against an in-memory or SQLite EF Core context, asserting the date-range overlap semantics and "most recent" tie-breaker.
- Frontend Jest tests cover `DqtYesterdayStatusTile.tsx` render output for each of the four states, plus the navigation click handler.
- Coverage stays at or above the 80% project floor.

## Data Model

No new entities or schema changes. The tile reads from the existing `DqtRun` aggregate (`backend/src/Anela.Heblo.Domain/Features/DataQuality/DqtRun.cs`):

| Field | Type | Source |
| --- | --- | --- |
| `Id` | `Guid` | existing |
| `TestType` | `DqtTestType` | existing — filtered to `IssuedInvoiceComparison` |
| `DateFrom` / `DateTo` | `DateOnly` | existing — used for "covers yesterday" predicate |
| `Status` | `DqtRunStatus` (`Running` / `Completed` / `Failed`) | existing |
| `StartedAt` / `CompletedAt` | `DateTime` / `DateTime?` | existing — used for tie-breaker ordering |
| `TotalChecked` | `int` | existing |
| `TotalMismatches` | `int` | existing |

A potential composite index on `(TestType, DateFrom, DateTo)` may need to be added if not already present — see NFR-1.

## API / Interface Design

No new HTTP endpoints. The tile data flows through the existing pipeline:

```
GET /api/dashboard/data
  → DashboardController
    → MediatR pipeline / tile registry
      → DqtYesterdayStatusTile.LoadDataAsync(parameters, ct)
        → IDqtRunRepository.GetLatestByTestTypeAndCoveredDateAsync(IssuedInvoiceComparison, yesterday, ct)
          → EF Core query on DqtRun
```

**Frontend rendering pipeline:**

```
useDashboard hook → fetches /api/dashboard/data
  → tiles[] includes { tileId: 'dqtyesterdaystatus', data: { status, data, drillDown } }
    → DashboardGrid → DashboardTile → TileContent (switch on tileId)
      → DqtYesterdayStatusTile (new component)
```

## Dependencies

- **`DqtRun` entity** and **`DqtRunStatus` enum** — existing in `Anela.Heblo.Domain.Features.DataQuality`.
- **`IDqtRunRepository`** — extended with one new method (FR-2).
- **`ITile`, `RegisterTile<T>()`, `TileSize`, `TileCategory`** — existing in `Anela.Heblo.Xcc.Services.Dashboard`.
- **`TimeProvider`** — built-in to .NET 8; verify it is registered in DI (`services.AddSingleton(TimeProvider.System)` if not).
- **Frontend `lucide-react` icons** — `AlertTriangle`, `ShieldCheck`, `XCircle`, `Clock` already imported by sibling tiles.
- **Frontend `useNavigate`** — already used by `DataQualityTile`.
- The DQT pipeline that produces `DqtRun` rows (`InvoiceDqtJob`, `InvoiceDqtJobRunner`) is independent of this feature — not modified.

## Out of Scope

- Changes to the DQT scheduler, the comparator, or the DQT run lifecycle.
- A history view of past "yesterday" results — the tile only shows yesterday at the moment of dashboard load.
- Surfacing individual mismatched invoices on the tile (drill-down to `/automation/data-quality` covers that).
- Adding configurable date offsets (e.g. "show last N days" or "show specific date") — the brief is explicit that this tile is scoped to yesterday.
- Notifications, emails, or alerts when yesterday's run failed — that belongs to a separate alerting feature.
- Modifying the existing `DataQualityStatusTile` or its frontend component beyond what is required to add the new sibling tile.
- Renaming `DataQualityStatusTile` to clarify the "latest" semantic — even though the contrast with the new tile makes the existing name slightly ambiguous, leave it untouched to keep the change surgical.

## Open Questions

None.

## Status: COMPLETE
