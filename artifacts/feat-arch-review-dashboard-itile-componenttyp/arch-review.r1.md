```markdown
# Architecture Review: Remove dead `ComponentType` property from `ITile` contract

## Skip Design: true

This is a backend-only refactor. No new or changed UI components, screens, or visual design — the frontend already ignores the value (`DashboardTileDto` does not carry it, and `TileContent.tsx` resolves components by `tileId`). No design work required.

## Architectural Fit Assessment

The change strengthens existing architectural conventions:

- **Vertical Slice + Clean Architecture boundary stays intact.** `ITile`, `TileMetadata`, `TileData` live in `Anela.Heblo.Xcc.Services.Dashboard` (cross-cutting infrastructure). Concrete tiles live in feature slices under `Application/Features/*/DashboardTiles/`. The mapping point — `GetTileDataHandler` — is the single place that translates the cross-cutting carrier into the wire DTO. Today it drops `ComponentType`; after this change there is nothing to drop.
- **DTO-vs-internal-type discipline (CLAUDE.md project rule) is reinforced.** `DashboardTileDto` (the wire contract) is a class with no `ComponentType` and is unchanged. The internal carrier types lose a member that no consumer ever reads.
- **Single-responsibility for the contract.** `ITile` is meant to describe *what tile content the backend provides*. Frontend component wiring belongs to the frontend (and already lives there, in `TileContent.tsx`'s `tileId` switch). Removing `ComponentType` realigns `ITile` with its real responsibility.

Integration points touched:
1. **Contract layer** — `ITile`, `TileMetadata`, `TileData` in `backend/src/Anela.Heblo.Xcc/Services/Dashboard/`.
2. **Registry mapping** — `TileRegistry.ToMetadata` in the same folder (the spec missed this site — see Specification Amendments).
3. **Use-case handler** — `GetTileDataHandler` in `Application/Features/Dashboard/UseCases/GetTileData/`.
4. **Tile implementations** — 19 concrete tile classes across 7 feature slices.
5. **Tests** — fixtures, validation tests, registry tests, and 3 per-tile metadata assertions.

There is no transitive impact: no migration, no Key Vault change, no OpenAPI client change (`DashboardTileDto` is the OpenAPI surface and is untouched), no frontend change.

## Proposed Architecture

### Component Overview

```
                       Before                                       After
─────────────────────────────────────────────────  ───────────────────────────────────────────────
ITile.ComponentType ─┐                             ITile (no ComponentType)
                     │                              │
TileRegistry.ToMetadata ──► TileMetadata.ComponentType   TileRegistry.ToMetadata ──► TileMetadata
                                  │                                                       │
GetTileDataHandler ──► TileData.ComponentType            GetTileDataHandler ──► TileData
                                  │                                                       │
                                  X  (dropped, never reaches DTO)                         │
DashboardTileDto (no ComponentType field) ──► JSON       DashboardTileDto ──► JSON  (unchanged shape)
```

The three carriers each shed one member; the mapping site simply has one fewer line. No new components, no new abstractions.

### Key Design Decisions

#### Decision 1: Remove rather than redesign
**Options considered:**
- (a) Remove the property entirely (this spec).
- (b) Add `ComponentType` to `DashboardTileDto` and wire it to the frontend so the backend can drive component selection.
- (c) Replace with a `string ComponentKey` to avoid leaking CLR `Type` over the wire.

**Chosen approach:** (a) — remove.

**Rationale:** The frontend already has authoritative component-to-`tileId` mapping in `TileContent.tsx`. There is no product requirement that says "the backend should choose the frontend component." Adding such a coupling (option b/c) would invert the current — and correct — separation of concerns: backend owns data, frontend owns rendering. Removal is the only option that matches existing intent.

#### Decision 2: Treat all 19 implementations as a single mechanical change
**Options considered:**
- (a) One commit that updates contract + registry + all tiles + tests + handler together.
- (b) Phased removal (interface first with default implementation, then per-slice cleanup).

**Chosen approach:** (a).

**Rationale:** The spec's NFR-4 explicitly *welcomes* compile-time discovery of touch points. C# interfaces cannot carry a default implementation without leaking complexity into every implementer, and a phased approach buys nothing: the change is purely mechanical, fully covered by `dotnet build`, and there is no consumer of the property to coordinate with. The same PR also satisfies the surgical-changes rule in CLAUDE.md — nothing adjacent is improved.

#### Decision 3: Delete tests that *assert* the property; mechanically update tests that *set* it
**Options considered:**
- (a) Delete only the lines that touch `ComponentType`, keeping the surrounding test alive.
- (b) Delete the entire test method when it only asserts `ComponentType`.

**Chosen approach:** (a) — delete the asserting *line*, not the test method.

**Rationale:** In `ManualActionRequiredTileTests`, `ManufactureConditionsTileTests`, and `WeatherForecastTileTests`, the assertions live inside a multi-assert "metadata properties have correct values" test that also asserts `Title`, `Size`, `Category`, etc. Deleting one `Assert`/`Should()` line keeps the surrounding coverage intact. Per the testing rule, we fix what's wrong, not delete passing coverage.

## Implementation Guidance

### Directory / Module Structure

No new files. No moved files. Edits only.

```
backend/
├── src/
│   ├── Anela.Heblo.Xcc/Services/Dashboard/
│   │   ├── ITile.cs                          [edit] remove ComponentType property
│   │   ├── TileMetadata.cs                   [edit] remove ComponentType positional param
│   │   ├── TileData.cs                       [edit] remove ComponentType property
│   │   └── TileRegistry.cs                   [edit] line 67 — drop from ToMetadata factory
│   │
│   └── Anela.Heblo.Application/Features/
│       ├── Dashboard/UseCases/GetTileData/
│       │   └── GetTileDataHandler.cs         [edit] line 84 — drop ComponentType = tile.ComponentType
│       │
│       ├── Analytics/DashboardTiles/InvoiceImportStatisticsTile.cs    [edit]
│       ├── BackgroundJobs/DashboardTiles/FailedJobsTile.cs            [edit]
│       ├── Catalog/DashboardTiles/InventorySummaryTileBase.cs         [edit]
│       ├── Catalog/DashboardTiles/InventoryCountTileBase.cs           [edit]
│       ├── Catalog/DashboardTiles/LowStockAlertTile.cs                [edit]
│       ├── DataQuality/DashboardTiles/DataQualityStatusTile.cs        [edit]
│       ├── DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs       [edit]
│       ├── Logistics/DashboardTiles/CriticalGiftPackagesTile.cs       [edit]
│       ├── Logistics/DashboardTiles/TransportBoxBaseTile.cs           [edit]
│       ├── Manufacture/DashboardTiles/ManualActionRequiredTile.cs     [edit]
│       ├── Manufacture/DashboardTiles/ManufactureConditionsTile.cs    [edit]
│       ├── Manufacture/DashboardTiles/UpcomingProductionTile.cs       [edit]
│       ├── Purchase/DashboardTiles/LowStockEfficiencyTile.cs          [edit]
│       ├── Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs     [edit]
│       └── WeatherForecast/DashboardTiles/WeatherForecastTile.cs      [edit]
│       (plus BackgroundTaskStatusTile.cs in Xcc/Services/Dashboard/Tiles/)
│
└── test/Anela.Heblo.Tests/Features/
    ├── Dashboard/Fixtures/TestTiles.cs                                [edit] 5 sites
    ├── Dashboard/TileExtensionsTests.cs                               [edit] 2 sites
    ├── Dashboard/TileRegistryTests.cs                                 [edit] line 93 — drop TrackedTile.ComponentType
    ├── Dashboard/TileRegistryValidationTests.cs                       [edit] 3 sites
    ├── Manufacture/DashboardTiles/ManualActionRequiredTileTests.cs    [edit] drop assert at line 35
    ├── Manufacture/DashboardTiles/ManufactureConditionsTileTests.cs   [edit] drop assert at line 41
    └── WeatherForecast/DashboardTiles/WeatherForecastTileTests.cs     [edit] drop assert at line 33
```

**Tile subclasses to verify:** `InventorySummaryTileBase` and `InventoryCountTileBase` are base classes with multiple subclasses (e.g. `ProductInventorySummaryTile`, `MaterialInventoryCountTile`, `ReceivedBoxesTile`, etc.). Removing `ComponentType` from the base type removes it from all subclasses for free — confirm none of the subclasses override it (the grep shows only base classes define it).

### Interfaces and Contracts

**`ITile` (after):**
```csharp
public interface ITile
{
    string Title { get; }
    string Description { get; }
    TileSize Size { get; }
    TileCategory Category { get; }
    bool DefaultEnabled { get; }
    bool AutoShow { get; }
    string[] RequiredPermissions { get; }

    Task<object> LoadDataAsync(
        Dictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default);
}
```

**`TileMetadata` (after):**
```csharp
public sealed record TileMetadata(
    string TileId,
    string Title,
    string Description,
    TileSize Size,
    TileCategory Category,
    bool DefaultEnabled,
    bool AutoShow,
    string[] RequiredPermissions);
```

This is a **positional-record signature change**. Every call site that constructs `TileMetadata` (today only `TileRegistry.ToMetadata`) must be updated in the same change. Because it is a `record` (not a DTO crossing the OpenAPI generator) the project rule "DTOs are classes, never records" does not apply — `TileMetadata` is an internal carrier.

**`TileData` (after):** drop the `ComponentType` property and its `typeof(object)` initializer; leave the rest as-is (still a mutable class because `GetTileDataHandler` populates it via object initializer).

**`DashboardTileDto`:** unchanged. This guarantees byte-identical JSON output and zero impact on the auto-generated TypeScript client.

### Data Flow

```
HTTP GET /api/dashboard/tile-data
   │
   ▼
GetTileDataHandler.Handle
   │
   ├──► IMediator.Send(GetUserSettingsRequest)        ── user's visible tiles
   │
   └──► For each visible tile (Parallel.ForEachAsync):
         │
         ├──► ITileRegistry.GetTileMetadata(tileId)   ── returns TileMetadata (no ComponentType)
         │
         ├──► ITileRegistry.GetTileDataAsync(tileId)  ── calls tile.LoadDataAsync(...)
         │
         └──► Build TileData { …, Data = data }       ── no ComponentType to copy
   │
   ▼
Project TileData → DashboardTileDto                   ── identical to today
   │
   ▼
JSON response                                          ── byte-identical
```

No new flow paths, no parallelism changes, no error-handling differences.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Missed `ITile` implementer outside the 19 found by grep — solution fails to build. | Low | The interface change forces a compile error at every implementer. `dotnet build` is the gate. The CLAUDE.md "validation before completion" checklist already mandates `dotnet build`. |
| `TileMetadata` is a positional record — adding/removing a parameter is a breaking signature change. | Low | Single producer (`TileRegistry.ToMetadata` line 59-68); no consumers construct it. Compiler catches any third-party construction. |
| Test `TileRegistryTests.TrackedTile` uses `typeof(TrackedTile)` (the *only* non-`typeof(object)` use in the codebase) — could indicate a test that actually exercises the property. | Low | The line is metadata setup, not an assertion target; the test class tracks scope disposal, not component types. Mechanical removal is safe. Verify by reading the surrounding test before removing. |
| Frontend coincidentally serializes/deserializes `componentType` through some hand-rolled fetch path. | Very low | Confirmed via grep: the only frontend `componentType` references are in `MessageDeliveryIcon.tsx` and `ErrorBoundary.tsx`, neither of which touches the dashboard. |
| OpenAPI client regeneration produces a diff because `TileMetadata`/`TileData` accidentally leak through Swagger. | Very low | Only `DashboardTileDto` is exposed via controllers. Internal carriers are not serialized through the API. Run `npm run build` after change to confirm the TS client is unchanged. |
| Subclass of `InventorySummaryTileBase` / `InventoryCountTileBase` re-declares `ComponentType` and gets orphaned. | Low | Confirmed by grep: only the base classes carry the property. No `override`/`new` redeclarations exist. |

## Specification Amendments

The spec is largely complete, but three additions are needed for the implementer to finish in one pass:

1. **Add `TileRegistry.ToMetadata` to FR-3.** The spec correctly removes the property from the `TileMetadata` record but does not name the call site that constructs it. `Xcc/Services/Dashboard/TileRegistry.cs:67` passes `tile.ComponentType` into the constructor and must be updated when the record's positional signature changes. Failing to update this is a compile error rather than a silent bug, but listing the site keeps the PR diff coherent.

2. **Explicitly include `BackgroundTaskStatusTile.cs`** under FR-2. It lives in `Anela.Heblo.Xcc/Services/Dashboard/Tiles/` (the Xcc project), not in `Application/Features/`. An implementer following "every tile under `Application/Features/*/DashboardTiles/`" could miss it. The grep above is authoritative — 19 production tile files in total (one in Xcc, 18 in Application).

3. **Clarify FR-7 with the three assert sites.** The spec says "tests that assert on `ComponentType` … must be deleted, since the property is being removed entirely." Override this with: *delete the asserting line, not the test method*. The three sites — `ManualActionRequiredTileTests.cs:35`, `ManufactureConditionsTileTests.cs:41`, `WeatherForecastTileTests.cs:33` — are single asserts inside multi-assert `Metadata_HasCorrectValues` tests. Removing the line preserves the surrounding metadata-shape coverage; removing the method would silently lose that coverage.

These are clarifications, not scope changes. No new requirements.

## Prerequisites

None. The change has zero infrastructure dependencies:

- No database migration.
- No Azure Key Vault secret to add (per CLAUDE.md secret rule, this is irrelevant here — no secrets are touched).
- No configuration change (`appsettings*.json` unchanged).
- No OpenAPI regeneration needed: `DashboardTileDto` is the only contract surface and is unchanged; the auto-generated TypeScript client will produce a no-op diff.
- No feature flag (per project rules, none needed for an internal refactor with byte-identical wire output).
- No coordination with frontend, no deployment ordering, no Docker image dependency.

The implementer can land this in a single PR. Validation gates per CLAUDE.md: `dotnet build` + `dotnet format` + `dotnet test` + `npm run build` + `npm run lint`. The nightly E2E suite will exercise the unchanged dashboard endpoints and provide post-merge confirmation.
```