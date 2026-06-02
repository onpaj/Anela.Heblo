# Architecture Review: Fix DI Lifetime Leak in `TileRegistry`

## Skip Design: true

Backend-only refactor — no UI components, screens, or visual changes. HTTP contract is preserved byte-for-byte.

## Architectural Fit Assessment

The change is well-scoped and aligns naturally with the existing architecture:

- **Module boundary respected.** Production code lives in `Anela.Heblo.Xcc` (cross-cutting). Only one downstream consumer (`Anela.Heblo.Application`) is affected. No ripple into other slices.
- **Pattern consistent.** A registry returning value-object snapshots instead of leaked service instances is the conventional .NET DI pattern. The change moves the codebase *toward* the documented Clean Architecture rule that lifetimes should be honored end-to-end.
- **`ITile` interface unchanged.** Tile authors don't have to learn anything new.
- **DTO/record rule (CLAUDE.md).** `TileMetadata` is **not** an OpenAPI DTO — it never crosses the HTTP boundary. The mapped DTO is `DashboardTileDto` (which stays a class). Therefore `record class` for `TileMetadata` is appropriate and consistent with `RefreshTaskExecutionLog` (also a `record` in the Xcc module).
- **Integration points.** Three: (1) `ITileRegistry` interface contract, (2) `DashboardService.GetTileDataAsync` consumer, (3) `GetAvailableTilesHandler` consumer. All are in-process; no cross-process or wire-format concerns.

The only smell: `DashboardService.GetTileDataAsync` resolves each tile *twice* per request (once in `GetTileMetadata`, once inside `GetTileDataAsync` for the data load). This is **pre-existing** behavior — the new design preserves it but does not worsen it. Caching is correctly deferred (per spec "Out of Scope").

## Proposed Architecture

### Component Overview

```
                       ┌────────────────────────────────────────────┐
                       │            Anela.Heblo.Xcc                 │
                       │                                            │
                       │   ┌──────────────┐                         │
                       │   │ ITileRegistry│                         │
                       │   │ (singleton)  │                         │
                       │   └──────┬───────┘                         │
                       │          │ creates per-call scope          │
                       │          ▼                                 │
                       │   ┌──────────────┐ resolves   ┌─────────┐  │
                       │   │ IServiceScope│──────────► │  ITile  │  │
                       │   └──────┬───────┘            │ (scoped)│  │
                       │          │ disposes           └────┬────┘  │
                       │          ▼                         │       │
                       │   ┌────────────────────────────────┘       │
                       │   │ snapshot                               │
                       │   ▼                                        │
                       │   ┌──────────────┐                         │
                       │   │ TileMetadata │ ◄── returned to caller  │
                       │   │ (immutable)  │                         │
                       │   └──────┬───────┘                         │
                       └──────────┼─────────────────────────────────┘
                                  │
            ┌─────────────────────┼─────────────────────┐
            ▼                                           ▼
   ┌────────────────────┐                  ┌───────────────────────────┐
   │ DashboardService   │                  │ GetAvailableTilesHandler  │
   │ (Xcc)              │                  │ (Application)             │
   │  - GetUserSettings │                  │  → DashboardTileDto       │
   │  - GetTileDataAsync│                  └───────────────────────────┘
   │      → TileData    │
   └────────────────────┘
```

`ITile` instances **never** escape the registry's internal scopes.

### Key Design Decisions

#### Decision 1: Value-object snapshot vs. singleton lifetime promotion
**Options considered:**
- (A) Snapshot tile metadata into immutable `TileMetadata` inside the scope, return that. *(spec choice)*
- (B) Register `ITile` implementations as singletons; `GetTile` becomes safe trivially.
- (C) Keep returning `ITile` but require the caller to manage scope.

**Chosen approach:** (A) — snapshot value object.

**Rationale:** (B) forecloses any future tile from legitimately consuming a scoped dependency (e.g., `DbContext`, `ICurrentUserService`) inside `LoadDataAsync` — the very capability `GetTileDataAsync` already supports. (C) leaks DI complexity into every consumer. (A) keeps the scope lifetime invariant inside the registry where it belongs and removes the structural footgun for good.

#### Decision 2: `record class` vs. plain `class` for `TileMetadata`
**Options considered:**
- (A) `record class TileMetadata(...)` with primary constructor / positional members.
- (B) Plain `class` with init-only properties (project DTO rule).

**Chosen approach:** (A) — `record class`.

**Rationale:** The project DTO rule (CLAUDE.md) targets OpenAPI-serialized types because the generators mis-handle record parameter order. `TileMetadata` never crosses the HTTP boundary — it is an internal type mapped *into* `DashboardTileDto` and `TileData` (which remain classes). `record class` gives value-equality semantics and immutability cheaply, and matches the existing `RefreshTaskExecutionLog` precedent in `Anela.Heblo.Xcc/Services/BackgroundRefresh/`.

#### Decision 3: Single shared scope for `GetAvailableTiles()`
**Options considered:**
- (A) One scope wrapping the full enumeration (spec choice).
- (B) One scope per tile.

**Chosen approach:** (A).

**Rationale:** Tiles are resolved against the *registry's* singleton service provider and discarded immediately after snapshotting. There is no per-tile "request" semantically; a single shared scope is cheaper and matches today's behavior. The pre-condition for (B) — tiles needing independent request contexts — is not present.

#### Decision 4: Preserve method name `GetAvailableTiles` despite return-type change
**Chosen approach:** Keep the name; change the type to `IEnumerable<TileMetadata>`.

**Rationale:** Minimizes diff at call sites and the spec explicitly opts for it. The name remains semantically accurate ("available tiles"); only the projection changes. Renaming buys nothing.

## Implementation Guidance

### Directory / Module Structure

**Production (`backend/src/Anela.Heblo.Xcc/Services/Dashboard/`):**
- **NEW** `TileMetadata.cs` — sibling of `ITile.cs`, `TileSize.cs`, `TileCategory.cs`.
- **MODIFY** `ITileRegistry.cs` — swap `ITile?`/`IEnumerable<ITile>` for `TileMetadata?`/`IEnumerable<TileMetadata>`.
- **MODIFY** `TileRegistry.cs` — implement `GetTileMetadata`; rewrite `GetAvailableTiles` body.
- **MODIFY** `DashboardService.cs` — update `GetUserSettingsAsync` and `GetTileDataAsync` to consume metadata. The line `var tile = _tileRegistry.GetTile(...)` becomes a metadata lookup; the subsequent `TileData` projection reads from the metadata snapshot (note that `TileData.TileId = metadata.TileId`, not `tile.GetTileId()`).

**Consumer (`backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetAvailableTiles/`):**
- **MODIFY** `GetAvailableTilesHandler.cs` — read `t.TileId` directly from the metadata record (not `t.GetTileId()`).

**Tests (`backend/test/Anela.Heblo.Tests/`):**
- **MODIFY** `Features/Dashboard/DashboardServiceTests.cs` — replace `GetTile(...)` setups with `GetTileMetadata(...)`; replace `IEnumerable<ITile>` returns with `IEnumerable<TileMetadata>`; drop the helper `ITile` subclasses (`AutoTile1`, `AutoTile2`, `NewAutoShowTile`, `ManualTile`, `TestTileWithData`) in favor of direct `new TileMetadata(...)` construction. Note: `TestTileWithData.TileId` is currently passed but never asserted — the existing tests at lines 222–223, 341–343 actually assert `"testwithdata"` (the reflection-derived id), so those assertions must change to use the explicit `TileId` field instead.
- **MODIFY** `Features/Dashboard/GetAvailableTilesHandlerTests.cs` — same mock-return change; the three `TestTile*` helper classes become `TileMetadata` constructions.
- **NEW** `Xcc/Dashboard/TileRegistryTests.cs` — **not** the path the spec proposes (`Services/Dashboard/`). The repo organizes Xcc tests under `backend/test/Anela.Heblo.Tests/Xcc/` (cf. `Xcc/BackgroundRefresh`), so the new file belongs at `Xcc/Dashboard/TileRegistryTests.cs`. See *Specification Amendments*.

**Untouched:**
- `Controllers/DashboardControllerTests.cs` — uses `IMediator`, not `ITileRegistry`. Spec FR-6 lists this file, but no edits are needed. See *Specification Amendments*.
- `ITile.cs`, `TileExtensions.cs`, `Tiles/BackgroundTaskStatusTile.cs`, `TileRegistryExtensions.cs`, `XccModule.cs`.

### Interfaces and Contracts

```csharp
// New value object — Anela.Heblo.Xcc/Services/Dashboard/TileMetadata.cs
public sealed record class TileMetadata(
    string TileId,
    string Title,
    string Description,
    TileSize Size,
    TileCategory Category,
    bool DefaultEnabled,
    bool AutoShow,
    Type ComponentType,
    string[] RequiredPermissions);

// Modified — Anela.Heblo.Xcc/Services/Dashboard/ITileRegistry.cs
public interface ITileRegistry
{
    void RegisterTile<TTile>() where TTile : class, ITile;
    IEnumerable<TileMetadata> GetAvailableTiles();
    TileMetadata? GetTileMetadata(string tileId);
    Task<object?> GetTileDataAsync(string tileId, Dictionary<string, string>? parameters = null);
    IEnumerable<string> GetRegisteredTileIds();
}
```

Snapshot construction must use `tile.GetTileId()` (the reflection-based extension in `TileExtensions.cs`) to populate `TileMetadata.TileId`, because `ITile` itself does not expose a `TileId` property.

### Data Flow

**`GET /api/dashboard/available-tiles`:**
```
Controller → MediatR → GetAvailableTilesHandler
   → ITileRegistry.GetAvailableTiles()
        → CreateScope()
        → for each registered tile type: GetRequiredService → snapshot → TileMetadata
        → scope disposed
        → return List<TileMetadata>
   → project to DashboardTileDto[]
   → HTTP response (unchanged shape)
```

**`DashboardService.GetTileDataAsync(userId)` per visible tile (in parallel):**
```
ITileRegistry.GetTileMetadata(tileId)  ─► scope #1: resolve, snapshot, dispose ─► TileMetadata
ITileRegistry.GetTileDataAsync(tileId) ─► scope #2: resolve, await LoadDataAsync, dispose ─► object
TileData { ...metadata fields, Data = result }
```

Both scopes are fully contained inside the registry; no scoped reference escapes either method.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Hidden third-party caller of `ITile? GetTile(string)` (e.g., MCP tools, controllers, tests) breaks at build time. | Low | Grep `GetTile\b` across the solution before merge; `dotnet build` will fail loudly if any caller is missed. Production-side search shows only `DashboardService.cs:140` references it. |
| Tests reusing helper `ITile` subclasses (`AutoTile1`, `TestTileWithData`, etc.) silently keep `IEnumerable<ITile>` semantics after the interface change, masking compile failures. | Low | Remove the helper classes outright in the same PR (they have no remaining purpose) instead of leaving them as dead code, so the compiler flags any straggling reference. |
| `TileMetadata.RequiredPermissions` is a mutable `string[]` exposed by an immutable record — a caller could mutate the array and pollute future snapshots. | Low | The snapshot is rebuilt per call from a fresh scope, so mutation cannot persist across calls. Still, prefer copying via `tile.RequiredPermissions.ToArray()` in the snapshot to defend against tiles that return shared array instances. |
| `DashboardService.GetTileDataAsync` resolves each tile twice per request (metadata snapshot + data load). | Low (pre-existing) | Not made worse by this change. If profiling later shows it matters, batch metadata + data into one `GetTileSnapshotAsync` method — explicitly out of scope here. |
| Regression test (FR-7) is asserted as "scope was disposed by the time the call returned" — there is no public API on the default DI scope to observe disposal directly. | Medium | Implement the tracking service as `IDisposable`; flip an internal flag in `Dispose()`. Construct the fake tile so it captures the tracking service reference at construction (e.g., into a field exposed for the test). After `GetTileMetadata` returns, assert the tracking service's `IsDisposed == true`. The assertion "calling any method on `TileMetadata` does not touch the tracking service" is automatic — `TileMetadata` holds primitives and `Type`, not the tile — but the test should still construct a tile that *would* throw `ObjectDisposedException` if its property getters were called post-disposal, to prove the snapshot decouples them. |
| Future tile author re-introduces the bug by reading scoped state from a property getter — undetected. | Medium | Document the registry invariant in an XML doc comment on `ITileRegistry` and add a sentence to `docs/architecture/development_guidelines.md` (per the spec's parenthetical). The FR-7 regression test serves as executable documentation. |
| `BackgroundTaskStatusTile.ComponentType` returns `typeof(object)`. Snapshotting it into `TileMetadata.ComponentType` then exposing it via `TileData.ComponentType` to JSON serialization could fail if any framework code tries to serialize `Type`. | Low | Current `TileData.ComponentType` already exposes `Type` and works today; no change in serialization surface. Confirm by running the existing `DashboardControllerTests` against the new code. |

## Specification Amendments

1. **FR-6 over-scopes the test changes.** `DashboardControllerTests.cs` does **not** reference `ITileRegistry` (it mocks `IMediator`). Remove this bullet from FR-6. The two real test-file impacts are `DashboardServiceTests.cs` and `GetAvailableTilesHandlerTests.cs`.

2. **FR-7 test location should mirror the production project, not the production folder.** The spec writes `backend/test/Anela.Heblo.Tests/Services/Dashboard/TileRegistryTests.cs`, but the test project organizes Xcc tests under `Xcc/<area>/` (cf. existing `Xcc/BackgroundRefresh/`). The new test belongs at `backend/test/Anela.Heblo.Tests/Xcc/Dashboard/TileRegistryTests.cs`.

3. **FR-1: `RequiredPermissions` defensive copy.** Clarify in the acceptance criteria that the snapshot copies the array (`tile.RequiredPermissions.ToArray()`) so that downstream mutation cannot reach into tile internals. Aligns with the immutable-snapshot intent.

4. **FR-7 acceptance criteria need a concrete disposal signal.** The current wording "tracking service was disposed by the time the call returned" needs an observable mechanism. Specify: the tracking service implements `IDisposable` and exposes an `IsDisposed` flag set inside `Dispose()`; the test asserts `tracker.IsDisposed.Should().BeTrue()` after the registry call returns.

5. **Existing assertion in `DashboardServiceTests.GetTileDataAsync_WhenUserHasVisibleTiles_*` checks `tileData.TileId.Should().Be("testwithdata")` (the reflection-derived id) — but the new code reads `metadata.TileId` from the snapshot field.** The test's `TestTileWithData(string tileId)` parameter is *not* honored by the registry today either (because `GetTile().GetTileId()` is reflection-based on the type name). When migrating, replace the helper-class fixture with a direct `new TileMetadata(TileId: "tile1", ...)` and update the assertion to expect the explicit id. Same applies to lines 341–343.

6. **NFR-3 should be tightened with a build-time guard.** Add an acceptance criterion to FR-2: `grep -rn "GetTile\b" backend/src` after the change yields zero references to the removed method (only `GetTileMetadata`, `GetTileDataAsync`, `GetTileId` should appear).

7. **Optional but recommended:** Add an XML doc comment on `ITileRegistry` summarizing the invariant ("Returns metadata snapshots; never returns an `ITile` instance, which is scoped"). This is the lowest-cost mechanism to prevent regression by future tile authors.

## Prerequisites

None — the change is self-contained:

- No database migration.
- No new configuration, environment variables, or Key Vault secrets.
- No new NuGet dependencies (`Microsoft.Extensions.DependencyInjection` is already referenced).
- No frontend coordination required (`DashboardTileDto` and `TileData` shapes are preserved).
- No CI/CD changes.

Validation gate before merge: `dotnet build` + `dotnet format` clean, all touched tests green, and the grep guard from amendment (6) returns zero hits.