# Architecture Review: Relocate Dashboard Business Logic from Xcc to MediatR Handlers

## Skip Design: true

This is a backend-only structural refactor. No new UI components, screens, layouts, or visual decisions are required. The OpenAPI surface is unchanged (FR-9) and the frontend slice regenerates byte-identical.

## Architectural Fit Assessment

The proposal aligns cleanly with the project's documented patterns:

- **Vertical Slice Architecture** (`docs/architecture/development_guidelines.md`): Feature code lives under `Application/Features/<Module>/{Contracts, UseCases, Infrastructure, Services, …}`. Moving the provisioning/loading/locking concerns into `Application/Features/Dashboard/` is exactly where this feature's "guts" should live.
- **"Business logic in MediatR handlers, not controllers — and by extension not in Xcc"** (CLAUDE.md + dev guidelines): the current `DashboardService` is the textbook violation; this refactor is corrective.
- **Existing precedent**: `GetAvailableTilesHandler` already depends directly on `ITileRegistry` (`Application/.../GetAvailableTiles/GetAvailableTilesHandler.cs:11`). The Infrastructure folder convention is established under `Application/Features/Catalog/Infrastructure/`, `DataQuality/Infrastructure/`, etc., so `Application/Features/Dashboard/Infrastructure/` for the lock is consistent.
- **Module wiring**: `DashboardModule.AddDashboardModule()` already exists (`Application/Features/Dashboard/DashboardModule.cs:12`) and is invoked from `ApplicationModule.cs:77`. Lock registration extends this without new wiring code.
- **Reference direction**: today `Application` → `Xcc` (one-way). The refactor adds no reverse dependency. `Xcc` will keep `ITileRegistry`, `IUserDashboardSettingsRepository`, `DashboardOptions`, `ITile`, `TileData`, and the domain entities — all genuinely cross-cutting / contract surfaces.

The only material design tension is **handler-to-handler composition via `IMediator.Send`**, discussed under Decision 2 below.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────────────┐
│  Anela.Heblo.Application.Features.Dashboard                            │
│                                                                        │
│  UseCases/                                                             │
│    GetUserSettings/GetUserSettingsHandler  ──┐                         │
│    GetTileData/GetTileDataHandler  ──────────┼─► IMediator.Send        │
│    SaveUserSettings/SaveUserSettingsHandler  │   (GetUserSettings only)│
│    EnableTile/EnableTileHandler  ────────────┘                         │
│    DisableTile/DisableTileHandler                                      │
│    GetAvailableTiles/GetAvailableTilesHandler  (unchanged)             │
│                                                                        │
│  Infrastructure/                       ◄── NEW                         │
│    IUserDashboardSettingsLock                                          │
│    UserDashboardSettingsLock (SemaphoreSlim-per-user, singleton)       │
│                                                                        │
│  DashboardModule.AddDashboardModule()  ◄── registers lock              │
└──────────────────────────────┬─────────────────────────────────────────┘
                               │ depends on (contracts only)
                               ▼
┌────────────────────────────────────────────────────────────────────────┐
│  Anela.Heblo.Xcc.Services.Dashboard         (kept, technical only)     │
│    ITileRegistry, TileRegistry                                         │
│    IUserDashboardSettingsRepository                                    │
│    DashboardOptions, ITile, TileData, TileSize, TileCategory           │
│    Tiles/BackgroundTaskStatusTile                                      │
│    TileExtensions, TileRegistryExtensions                              │
│                                                                        │
│  XccModule.AddXccServices()                                            │
│    - DELETED: IDashboardService registration                           │
└────────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Place `IUserDashboardSettingsLock` in `Application/Features/Dashboard/Infrastructure/`
**Options considered:**
- (a) `Application/Features/Dashboard/Infrastructure/` — matches existing convention used by Catalog, DataQuality, Manufacture, etc.
- (b) `Application/Features/Dashboard/Services/` — also used in the codebase but for richer domain services.
- (c) A generic `IKeyedAsyncLock<TKey>` in `Anela.Heblo.Application.Shared/` for future reuse.

**Chosen approach:** (a). One feature, one concrete use case, one folder. The lock is Dashboard's persistence-concurrency concern and nothing else needs it today.

**Rationale:** Building a generic shared lock now is premature abstraction — the spec is explicit about no other consumers. If a second feature ever needs the same pattern, extract then. The `Infrastructure/` convention is already established across ≥10 features, so contributors will find it.

#### Decision 2: Compose provisioning across handlers via `IMediator.Send(GetUserSettingsRequest)` — accept one extra read
**Options considered:**
- (a) `IMediator.Send` from Save/Enable/Disable to trigger provisioning, then re-read inside the per-user write lock. (Spec's choice.)
- (b) Extract the provisioning logic into a private helper class (e.g. `IUserDashboardSettingsProvisioner`) in `Application/.../Infrastructure/` and call it directly from each handler under the lock.
- (c) Inline the provisioning in every handler.

**Chosen approach:** (a) — keep the spec.

**Rationale:** The spec is right to route through `IMediator`: it ensures the canonical provisioning path is always the `GetUserSettings` handler, addressable through the pipeline (logging, validation, future cross-cutting behaviors). The cost is two repository reads in the Save/Enable/Disable flow (one inside the inner `GetUserSettings` lock to provision, one inside the outer write lock to mutate). At the scale of one user request per dashboard action, this is negligible. Option (b) would split provisioning logic across two consumers and re-create the very service-layer indirection we are removing. Option (c) duplicates the provisioning logic in four handlers — exactly the maintenance hazard MediatR composition prevents.

**Critical implementation note:** `SemaphoreSlim` is **non-reentrant**. The mutating handlers MUST perform `IMediator.Send` (which acquires & releases the lock internally) **before** acquiring their own lock — never inside it — or the second acquire will deadlock. The spec already states this (FR-5: "acquire the lock once per Handle invocation"; FR-6 narrative: "before acquiring the write lock"), but the implementer must keep this strictly true.

#### Decision 3: Lifetime of `IUserDashboardSettingsLock` — singleton
**Options considered:** singleton vs. scoped.
**Chosen approach:** Singleton.
**Rationale:** The lock's state (the `ConcurrentDictionary<string, SemaphoreSlim>`) must outlive any request scope to actually serialize concurrent users. The current implementation works by using a `static` dictionary on a scoped service — the new design correctly moves that to an explicit singleton lifetime, which is more honest and testable. Confirmed by spec FR-5.

#### Decision 4: `GetTileDataHandler` resolves settings via `IMediator.Send`, not via injected `IUserDashboardSettingsRepository`
**Chosen approach:** Per spec FR-3.
**Rationale:** Reading settings here goes through the canonical provisioning path. The DTO projection (`UserDashboardSettingsDto.Tiles[].{TileId, IsVisible, DisplayOrder}`) is sufficient for visibility/ordering — `GetTileData` does not need the full entity. This also means `GetTileDataHandler` doesn't need to take the per-user lock itself (it's pure read of already-provisioned data), which is correct.

## Implementation Guidance

### Directory / Module Structure

**New files (Application layer):**
```
backend/src/Anela.Heblo.Application/Features/Dashboard/
├── Infrastructure/
│   ├── IUserDashboardSettingsLock.cs
│   └── UserDashboardSettingsLock.cs
```

**Modified files (Application layer):**
```
backend/src/Anela.Heblo.Application/Features/Dashboard/
├── DashboardModule.cs                  # register lock as singleton
└── UseCases/
    ├── GetUserSettings/GetUserSettingsHandler.cs   # absorb FR-1 + FR-2
    ├── GetTileData/GetTileDataHandler.cs           # absorb FR-3
    ├── SaveUserSettings/SaveUserSettingsHandler.cs # FR-4
    ├── EnableTile/EnableTileHandler.cs             # FR-4
    └── DisableTile/DisableTileHandler.cs           # FR-4
```

**Deleted files (Xcc layer):**
```
backend/src/Anela.Heblo.Xcc/Services/Dashboard/
├── DashboardService.cs                 # DELETE
└── IDashboardService.cs                # DELETE
```

**Modified files (Xcc layer):**
```
backend/src/Anela.Heblo.Xcc/XccModule.cs   # remove AddScoped<IDashboardService, DashboardService>()
```

**Test layer:**
```
backend/test/Anela.Heblo.Tests/Features/Dashboard/
├── DashboardServiceTests.cs            # DELETE (assertions ported)
├── GetUserSettingsHandlerTests.cs      # rewrite to mock repo + registry + lock
├── GetTileDataHandlerTests.cs          # rewrite to mock IMediator + registry + options
├── SaveUserSettingsHandlerTests.cs     # rewrite to mock repo + lock + TimeProvider
├── EnableTileHandlerTests.cs           # rewrite
├── DisableTileHandlerTests.cs          # rewrite
└── Infrastructure/
    └── UserDashboardSettingsLockTests.cs  # NEW — covers FR-5 acceptance criteria
```

The shared test tile fixtures (`AutoTile1`, `AutoTile2`, `NewAutoShowTile`, `ManualTile`, `TestTileWithData`) currently sit at the bottom of `DashboardServiceTests.cs`. Move them into a `Features/Dashboard/Fixtures/` (or `TestTiles/`) folder so the handler tests can share them after `DashboardServiceTests.cs` is deleted. Do **not** keep them inline in one handler test file and then `using` from siblings — that's a hidden coupling.

### Interfaces and Contracts

**New — `IUserDashboardSettingsLock.cs`** (`Application/Features/Dashboard/Infrastructure/`):

```csharp
namespace Anela.Heblo.Application.Features.Dashboard.Infrastructure;

public interface IUserDashboardSettingsLock
{
    Task<IAsyncDisposable> AcquireAsync(string userId, CancellationToken cancellationToken = default);
}
```

**New — `UserDashboardSettingsLock.cs`** (default implementation):

- Backed by `ConcurrentDictionary<string, SemaphoreSlim>` keyed by `userId`.
- `AcquireAsync` calls `GetOrAdd` → `WaitAsync(cancellationToken)` → returns an `IAsyncDisposable` whose `DisposeAsync` calls `Release()`.
- The disposable must be safe against double-dispose (use an `int _released` interlocked guard).
- Pass the caller's `CancellationToken` into `WaitAsync` so the acquire can be cancelled cleanly.

**DI registration** in `DashboardModule.AddDashboardModule()`:

```csharp
services.AddSingleton<IUserDashboardSettingsLock, UserDashboardSettingsLock>();
```

**Handler constructor signatures** (after refactor):

| Handler | Injected dependencies |
|---|---|
| `GetUserSettingsHandler` | `ITileRegistry`, `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `TimeProvider` |
| `GetTileDataHandler` | `IMediator`, `ITileRegistry`, `IOptions<DashboardOptions>` |
| `SaveUserSettingsHandler` | `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `TimeProvider`, `IMediator` |
| `EnableTileHandler` | `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `TimeProvider`, `IMediator` |
| `DisableTileHandler` | `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `TimeProvider`, `IMediator` |
| `GetAvailableTilesHandler` | unchanged |

The current handlers use `DateTime.UtcNow` inline (in `DashboardService`) and `TimeProvider` in handlers — once consolidated, **all timestamps in handlers must come from `TimeProvider`**. Drop the `DateTime.UtcNow` calls from the ported code.

### Data Flow

**Flow 1 — `GetUserSettings` for a fresh user (new provisioning path):**
```
Controller → Mediator → GetUserSettingsHandler.Handle
  ├─ await using lock = await _lock.AcquireAsync(userId, ct)
  ├─ settings = await _repo.GetByUserIdAsync(userId)          // returns null
  ├─ autoShowTiles = _registry.GetAvailableTiles()
  │                  .Where(t => t.DefaultEnabled && t.AutoShow)
  ├─ settings = new UserDashboardSettings { UserId, LastModified=Now, Tiles=[…autoShowTiles] }
  ├─ await _repo.AddAsync(settings)
  └─ return UserDashboardSettingsDto projection
```

**Flow 2 — `GetTileData` (read-only):**
```
Controller → Mediator → GetTileDataHandler.Handle
  ├─ settingsResponse = await _mediator.Send(new GetUserSettingsRequest { UserId }, ct)
  │     // triggers provisioning if needed; releases lock before returning
  ├─ visible = settingsResponse.Settings.Tiles
  │              .Where(t => t.IsVisible)
  │              .OrderBy(t => t.DisplayOrder)
  ├─ Parallel.ForEachAsync(visible, opts { MaxDoP = options.MaxConcurrentTileLoads }, async (t, ct) => …)
  │     // per-tile: registry lookup → GetTileDataAsync → TileData, with try/catch → error TileData
  └─ return ordered DashboardTileDto[]
```

**Flow 3 — `EnableTile` (write path):**
```
Controller → Mediator → EnableTileHandler.Handle
  ├─ _ = await _mediator.Send(new GetUserSettingsRequest { UserId }, ct)   // ensures provisioning ran
  ├─ await using lock = await _lock.AcquireAsync(userId, ct)
  ├─ settings = await _repo.GetByUserIdAsync(userId)                       // re-read under lock
  ├─ mutate settings.Tiles (enable existing or append new with maxOrder+1)
  ├─ settings.LastModified = _timeProvider.GetUtcNow().DateTime
  ├─ await _repo.UpdateAsync(settings)
  └─ return EnableTileResponse
```

Same shape for `DisableTile` and `SaveUserSettings`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Re-entrant lock acquisition (Mediator.Send inside `await using` of the lock) — would deadlock instantly. | **High** | Code review checkpoint: the only legal sequence is `Send` → `AcquireAsync`. Add an XML doc comment on `IUserDashboardSettingsLock` warning about non-reentrancy. Consider an integration test that exercises Enable then Disable in sequence to catch a regression. |
| `IAsyncDisposable` not awaited (`using` instead of `await using`) leaves the semaphore held until GC — effectively a permanent deadlock for that user. | **High** | Enforce via code review; consider a Roslyn analyzer or a unit test that fails if a handler holds the lock past `Handle` exit. |
| `Parallel.ForEachAsync` does not propagate the `Handle` `CancellationToken` today (inherited from current `DashboardService`). | Medium | Pass the handler's `cancellationToken` into `ParallelOptions.CancellationToken` and into the per-tile `_registry.GetTileDataAsync` call where supported. The current behavior is a latent bug; fix it as part of the move. |
| Double repository read on every write (Save/Enable/Disable). | Low | Accept (see Decision 2). Add a comment in each write handler pointing at the rationale ("first Send ensures provisioning; second read merges under write lock"). |
| Stale settings between Send and re-acquire — provisioning runs, then a parallel write by the same user happens, then the current write re-reads and sees the latest. | Low | Behavior is **better** than today (re-read under lock is fresher than the in-memory settings object passed around in the old `SaveUserSettingsAsync`). No mitigation needed. |
| Unbounded growth of `ConcurrentDictionary<string, SemaphoreSlim>`. | Low | Explicitly out of scope per spec NFR-1. Same behavior as today. Document in the lock implementation's class comment. |
| Test fixtures (`AutoTile1`, …, `TestTileWithData`) currently live at the bottom of `DashboardServiceTests.cs` — deleting that file silently breaks any other test file relying on the type names. | Low | Move fixtures into `Features/Dashboard/Fixtures/` before deleting `DashboardServiceTests.cs`. |
| `GetTileData`'s timing-sensitive parallel test (FR-3, < 180 ms with two 100 ms tasks) may be flaky on CI runners under load. | Low | Use a generous upper bound (e.g. `< 250 ms`) and a small lower bound (`> 100 ms` to prove they didn't run instantaneously serialized). |
| OpenAPI client diff after refactor — even a re-ordered DTO property or namespace change can shift the generated TS. | Low | The DTOs (`UserDashboardSettingsDto`, `UserDashboardTileDto`, `DashboardTileDto`) are not touched. Verify with `git diff frontend/src/api/generated/` after `dotnet build` regenerates. |
| `IDashboardService` registration is `Scoped` today; removing it must not leave a dangling `services.AddScoped<…>` call. | Low | Single edit point: `XccModule.cs:23`. Verify with `grep -r IDashboardService backend/src`. |

## Specification Amendments

1. **FR-3 — propagate `CancellationToken` into the parallel loop.** The current `DashboardService.GetTileDataAsync` ignores cancellation. Since this code is being rewritten anyway, the new `GetTileDataHandler` must pass `cancellationToken` into `ParallelOptions.CancellationToken`. Add an acceptance criterion: "If the request's `CancellationToken` is cancelled mid-load, the handler observes `OperationCanceledException` rather than completing." This is a latent bug-fix opportunity that costs nothing.

2. **FR-3 — clarify `GetTileDataRequest.TileParameters` propagation.** The current code forwards `tileParameters` to `_tileRegistry.GetTileDataAsync(tileId, tileParameters)`. The spec should explicitly state the handler must pass `request.TileParameters` through unchanged. (Implicit today; make it explicit.)

3. **FR-4 — clarify `SaveUserSettings` behavior when settings did not previously exist.** Today, `SaveUserSettingsHandler` calls `_dashboardService.GetUserSettingsAsync(userId)` first, which transparently provisions defaults. After the refactor, the spec says to call `IMediator.Send(GetUserSettingsRequest)` first. Make the acceptance criterion explicit: "A `Save` request for a brand-new user provisions defaults via the `GetUserSettings` mediation, then overlays the request's tile changes."

4. **FR-5 — lock implementation contract for cancellation.** Add: "If the `CancellationToken` is cancelled while waiting in `AcquireAsync`, the implementation throws `OperationCanceledException` and does not acquire the semaphore (no corresponding `Release` is needed)."

5. **FR-5 — disposable double-dispose safety.** Add to acceptance criteria: "Calling `DisposeAsync()` more than once on the returned disposable releases the semaphore exactly once." Use `Interlocked.Exchange(ref _released, 1)` guard.

6. **FR-8 — relocate shared test tile fixtures.** Before deleting `DashboardServiceTests.cs`, move `AutoTile1`, `AutoTile2`, `NewAutoShowTile`, `ManualTile`, `TestTileWithData` into a dedicated fixtures file (e.g. `Features/Dashboard/Fixtures/TestTiles.cs`) so the new handler tests can reference them. Add this as a precondition step.

7. **FR-9 / verification step.** Add: "After `dotnet build`, run `git diff --stat frontend/src/api/generated/` and assert zero lines changed in any `dashboard*` file." This makes the acceptance check executable.

## Prerequisites

1. **No infrastructure changes.** No DB migration, no config change, no Key Vault secret, no NuGet package addition.
2. **No deployment ordering concerns.** The refactor is internal; deploys atomically with the rest of the backend image.
3. **Frontend OpenAPI client regenerates on build** — the existing build pipeline already handles this. Just verify zero diff afterward.
4. **Test fixtures relocation** (see Spec Amendment 6) should be the first commit of the implementation so the handler test rewrites can reference shared fixture types from a stable location.
5. **Validation gates** before declaring done (per CLAUDE.md):
   - `dotnet build` succeeds.
   - `dotnet format` clean.
   - `dotnet test` — all Dashboard tests green, total assertion count ≥ pre-refactor.
   - `grep -r IDashboardService backend/src` and `grep -r "class DashboardService" backend/src` both empty.
   - `npm run build` in `frontend/` succeeds with zero diff in generated dashboard client files.