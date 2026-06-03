# Specification: Relocate Dashboard Business Logic from Xcc to MediatR Handlers

## Summary
The Dashboard feature's application logic (default-settings creation, AutoShow tile auto-provisioning, visibility/ordering, parallel tile loading, per-user write locking) currently lives in `Anela.Heblo.Xcc.Services.Dashboard.DashboardService`. Xcc is reserved for technical cross-cutting concerns; application logic must live in the Application layer's MediatR handlers. This refactor moves that logic into the existing handlers, introduces a thin locking abstraction owned by Application, and demotes `DashboardService` so Xcc only retains the genuinely cross-cutting `ITileRegistry`.

## Background
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/DashboardService.cs` contains:
  - `GetUserSettingsAsync` (lines 29–102) — reads settings; **also** creates default settings for new users and back-fills AutoShow tiles for existing users (a hidden write-on-read side effect). Wraps the entire operation in a per-user `SemaphoreSlim` (lines 12, 24–27, 31, 100).
  - `SaveUserSettingsAsync` (lines 104–119) — sets `UserId`/`LastModified` and persists, also guarded by the per-user lock.
  - `GetTileDataAsync` (lines 121–187) — calls `GetUserSettingsAsync`, filters to `IsVisible`, orders by `DisplayOrder`, and fans out to `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = DashboardOptions.MaxConcurrentTileLoads`; handles per-tile errors by emitting a synthetic error `TileData`.
- The corresponding MediatR handlers (`GetUserSettingsHandler`, `GetTileDataHandler`, `SaveUserSettingsHandler`, `EnableTileHandler`, `DisableTileHandler`) only delegate to `IDashboardService` and project to DTOs — they carry no business logic. `GetAvailableTilesHandler` already depends on `ITileRegistry` directly (correct pattern).
- Project rule (`CLAUDE.md`, `docs/architecture/development_guidelines.md`): business logic belongs in MediatR handlers, not in controllers, and Xcc is for "technical concerns only".
- Consequences of the current placement:
  1. Handler unit tests assert delegation, not behavior — true business logic (provisioning, ordering, concurrency) is untested at the handler boundary.
  2. Future contributors may treat Xcc as a valid home for new application services.
  3. The write-on-read in `GetUserSettingsAsync` is implicit and hard to trace from a handler reader's perspective.

## Functional Requirements

### FR-1: Move new-user default-settings creation into `GetUserSettingsHandler`
The "no settings row exists for this user" branch (current `DashboardService.cs` lines 42–64) must execute inside `GetUserSettingsHandler.Handle`. The handler must:
1. Query `IUserDashboardSettingsRepository.GetByUserIdAsync(userId)`.
2. If null, build a new `UserDashboardSettings { UserId, LastModified = UtcNow, Tiles = [] }`.
3. Enumerate `ITileRegistry.GetAvailableTiles()`, filter to `DefaultEnabled && AutoShow`, create one `UserDashboardTile` per match with `IsVisible = true`, `DisplayOrder = index`, `LastModified = UtcNow`, `DashboardSettings = settings`.
4. Persist via `IUserDashboardSettingsRepository.AddAsync(settings)`.
5. Project to `UserDashboardSettingsDto` and return inside `GetUserSettingsResponse`.

**Acceptance criteria:**
- A handler unit test with a repository returning `null` for the user asserts that `AddAsync` is invoked once with a settings object whose `Tiles` exactly match the set of registry tiles having `DefaultEnabled = true && AutoShow = true`, in registry enumeration order, with `DisplayOrder` 0..N-1.
- Behavior is byte-identical to the pre-refactor output for the same registry/repository inputs (verified by porting `DashboardServiceTests` assertions onto the handler).

### FR-2: Move AutoShow back-fill for existing users into `GetUserSettingsHandler`
The "settings exist, append any new AutoShow tiles the user doesn't have yet" branch (lines 65–94) must execute inside `GetUserSettingsHandler.Handle`. The handler must:
1. Compute `existingTileIds = settings.Tiles.Select(t => t.TileId).ToHashSet()`.
2. Compute `newAutoShowTiles = autoShowTiles.Where(t => !existingTileIds.Contains(t.GetTileId())).ToList()`.
3. If non-empty: compute `maxOrder = settings.Tiles.Any() ? settings.Tiles.Max(t => t.DisplayOrder) : -1`, append new `UserDashboardTile` entries with `DisplayOrder = maxOrder + i + 1`, set `settings.LastModified = UtcNow`, and call `UpdateAsync(settings)`.
4. If empty: do **not** call `UpdateAsync`.

**Acceptance criteria:**
- Handler unit test: existing settings missing one new AutoShow tile ⇒ `UpdateAsync` invoked once with the appended tile, `DisplayOrder = maxOrder + 1`.
- Handler unit test: existing settings already containing every AutoShow tile ⇒ `UpdateAsync` is **not** invoked.
- Ordering of multiple new tiles preserves registry enumeration order.

### FR-3: Move tile visibility/ordering/parallel loading into `GetTileDataHandler`
The orchestration in `DashboardService.GetTileDataAsync` (lines 121–187) must execute inside `GetTileDataHandler.Handle`. The handler must:
1. Resolve the user's settings by sending an in-process `GetUserSettingsRequest` via `IMediator` so the provisioning logic from FR-1/FR-2 is not duplicated.
2. Filter `settings.Tiles` to `IsVisible == true`, order by `DisplayOrder` ascending.
3. Execute `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = DashboardOptions.MaxConcurrentTileLoads` (read from injected `IOptions<DashboardOptions>`).
4. For each tile: look up via `ITileRegistry.GetTile(tileId)`; if null, emit the synthetic error `TileData` exactly as in the current implementation (same `Title = "Error"`, same `Category = TileCategory.Error`, same `Description` template, same `Data = new { Error = ... }`).
5. Otherwise call `_tileRegistry.GetTileDataAsync(tileId, tileParameters)`, build `TileData` with all current fields (`TileId, Title, Description, Size, Category, DefaultEnabled, AutoShow, ComponentType, RequiredPermissions, Data`).
6. Catch exceptions per tile and emit the synthetic error `TileData` with `ex.Message`.
7. Preserve input-order results via the `(int Index, TileData Data)` pattern.
8. Project to `DashboardTileDto` for `GetTileDataResponse` (unchanged from current handler).

**Note:** `GetTileDataHandler` returns a DTO; `GetUserSettingsHandler` returns the settings projection. The settings projection currently in `GetUserSettingsHandler` drops the underlying `UserDashboardSettings.Tiles[].UserId`/`LastModified`/`DashboardSettings` fields that `GetTileDataAsync` does **not** need (only `TileId`, `IsVisible`, `DisplayOrder` survive the projection — see `UserDashboardTileDto`). The `GetTileDataHandler` only needs those three fields, so going through `IMediator.Send(new GetUserSettingsRequest)` is sufficient.

**Acceptance criteria:**
- Handler unit test: registry returns one missing tile and one throwing tile ⇒ response contains two error `DashboardTileDto`s with matching titles/categories/data shapes.
- Handler unit test: three visible tiles with `DisplayOrder` `[2, 0, 1]` ⇒ response order is the tile with `DisplayOrder 0` first, etc.
- Handler unit test with two slow tiles (each `Task.Delay(100)`) and `MaxConcurrentTileLoads = 2`: total elapsed < 180 ms (i.e. they ran in parallel, not serially).

### FR-4: Move `SaveUserSettings` persistence concerns into its handler
`SaveUserSettingsHandler` must call `IUserDashboardSettingsRepository.UpdateAsync` directly (after acquiring the lock from FR-5), instead of going through `IDashboardService.SaveUserSettingsAsync`. The `UserId` and `LastModified` assignments currently performed inside `DashboardService.SaveUserSettingsAsync` (lines 111–112) must move into the handler. The handler's existing per-tile merge logic is unchanged.

**Acceptance criteria:**
- Handler unit test confirms `UpdateAsync` is invoked once with `settings.UserId == userId` and `settings.LastModified == _timeProvider.GetUtcNow().DateTime`.
- `EnableTileHandler` and `DisableTileHandler` are updated the same way — they currently call `_dashboardService.SaveUserSettingsAsync`; they must instead acquire the lock (FR-5) and call `UpdateAsync` directly.

### FR-5: Introduce `IUserDashboardSettingsLock` in Application layer
Add `IUserDashboardSettingsLock` to `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/` (new folder). Contract:

```csharp
public interface IUserDashboardSettingsLock
{
    Task<IAsyncDisposable> AcquireAsync(string userId, CancellationToken cancellationToken = default);
}
```

Default implementation lives next to the interface and uses a `ConcurrentDictionary<string, SemaphoreSlim>` keyed by `userId` (same semantics as today). The `IAsyncDisposable` returned releases the semaphore on `DisposeAsync`. Register as singleton in the Application module's DI extension method.

Every handler that mutates `UserDashboardSettings` (`GetUserSettingsHandler`, `SaveUserSettingsHandler`, `EnableTileHandler`, `DisableTileHandler`) must wrap its repository read-modify-write in an `await using` of the acquired lock. `GetUserSettingsHandler` must acquire the lock **before** the initial `GetByUserIdAsync` to preserve the existing read-modify-write atomicity on the new-user/back-fill paths.

**Acceptance criteria:**
- Unit test asserts that two concurrent `AcquireAsync(userId)` calls do not return overlapping disposables (the second waits for the first to dispose).
- Unit test asserts concurrent `AcquireAsync` for **different** `userId`s do not block each other.
- All four mutating handlers acquire the lock once per `Handle` invocation (verified by mocking the lock and asserting `AcquireAsync` was called).

### FR-6: Delete `DashboardService` and `IDashboardService`
After FR-1 through FR-5 land:
- Delete `backend/src/Anela.Heblo.Xcc/Services/Dashboard/DashboardService.cs` and `IDashboardService.cs`.
- Remove the corresponding registration from `backend/src/Anela.Heblo.Xcc/XccModule.cs`.
- No production code may reference `IDashboardService` after this change.

If a handler needs to compose "get settings (with provisioning)" from another handler, it must do so via `IMediator.Send(new GetUserSettingsRequest { UserId = userId })` — not via a new shared service. This keeps Application logic addressable through the MediatR pipeline.

**Acceptance criteria:**
- `grep -r IDashboardService backend/src` returns no matches.
- `grep -r "DashboardService" backend/src` returns no matches except inside the Application-layer handler test files (which keep the `DashboardServiceTests` name only if assertions are still being ported; otherwise that file is deleted — see FR-8).
- `dotnet build` passes.

### FR-7: Keep `ITileRegistry` and tile infrastructure in Xcc unchanged
`ITileRegistry`, `TileRegistry`, `ITile`, `TileData`, `TileSize`, `TileCategory`, `TileExtensions`, `TileRegistryExtensions`, `DashboardOptions`, `IUserDashboardSettingsRepository`, and the `Tiles/` folder remain in `Anela.Heblo.Xcc.Services.Dashboard`. These are technical infrastructure (tile contribution, repository contract, configuration) and are correctly placed.

`UserDashboardSettings` and `UserDashboardTile` (in `Anela.Heblo.Xcc.Domain`) also remain where they are; relocating domain entities is out of scope.

**Acceptance criteria:**
- No file in `Anela.Heblo.Xcc/Services/Dashboard/` other than `DashboardService.cs`/`IDashboardService.cs` is moved or deleted by this change.

### FR-8: Update unit tests
- Delete `backend/test/Anela.Heblo.Tests/Features/Dashboard/DashboardServiceTests.cs`. Port every behavioral assertion it contains (default-settings creation, AutoShow back-fill, parallel loading, error-tile shape, per-user lock semantics, etc.) into the relevant handler test class.
- Update `GetUserSettingsHandlerTests`, `GetTileDataHandlerTests`, `SaveUserSettingsHandlerTests`, `EnableTileHandlerTests`, `DisableTileHandlerTests` so each handler test:
  - Uses real or fake `ITileRegistry` and `IUserDashboardSettingsRepository` (mocks), not `IDashboardService`.
  - Injects a deterministic `IUserDashboardSettingsLock` (real implementation is fine — it is already singleton-safe).
  - Asserts business outcomes (e.g. `AddAsync` called with the right `Tiles`) instead of "delegation happened".

**Acceptance criteria:**
- After the refactor, every assertion originally in `DashboardServiceTests.cs` has a named counterpart in a handler test (1:1 traceability).
- `dotnet test` passes; total assertion count for the Dashboard module is ≥ the pre-refactor count.

### FR-9: External behavior must not change
- HTTP routes, request DTOs, and response DTOs are unchanged.
- The frontend (which is generated from the OpenAPI client) must compile against the regenerated client with zero diff in the `dashboard*` files. (Confirmed by `npm run build` after `dotnet build` regenerates the client.)
- Anonymous-user fallback (`userId = "anonymous"` when request `UserId` is null/empty) remains in every handler.

**Acceptance criteria:**
- An end-to-end manual smoke (sign in as a fresh user → dashboard renders with all `AutoShow` tiles → toggle one off → reload → tile stays off) succeeds.
- No diff in `frontend/src/api/generated/` files related to Dashboard endpoints after `dotnet build` regenerates the client.

## Non-Functional Requirements

### NFR-1: Performance
- Parallel tile loading must continue to honor `DashboardOptions.MaxConcurrentTileLoads`. A timing-sensitive unit test (FR-3) guards against accidental serialization.
- The per-user lock must not block reads for different users (FR-5 acceptance criterion).
- Memory footprint of `IUserDashboardSettingsLock` is bounded by the number of distinct users observed since process start (same as today). Eviction of idle locks is out of scope.

### NFR-2: Security
- No authentication or authorization surface change. `RequiredPermissions` continues to be returned in `DashboardTileDto`; per-tile permission enforcement is unchanged by this refactor.
- No new persistence of user identifiers beyond what already exists.

### NFR-3: Testability
- Each handler must be unit-testable in isolation by mocking `ITileRegistry`, `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, and `TimeProvider`.
- `DashboardServiceTests` is deleted (not left as a no-op shell).

### NFR-4: Architectural conformance
- `Anela.Heblo.Xcc` must not reference `Anela.Heblo.Application` after the refactor (existing direction).
- `Anela.Heblo.Application.Features.Dashboard` must not introduce new dependencies on Xcc beyond the existing `ITileRegistry`, `IUserDashboardSettingsRepository`, `DashboardOptions`, `ITile`, `TileData`, and the `Xcc.Domain` entities.

## Data Model
No schema changes. Entities used:
- `Anela.Heblo.Xcc.Domain.UserDashboardSettings { string UserId, DateTime LastModified, ICollection<UserDashboardTile> Tiles }`
- `Anela.Heblo.Xcc.Domain.UserDashboardTile { string UserId, string TileId, bool IsVisible, int DisplayOrder, DateTime LastModified, UserDashboardSettings DashboardSettings }`

Persistence contract `IUserDashboardSettingsRepository` (unchanged): `GetByUserIdAsync(string userId)`, `AddAsync(UserDashboardSettings)`, `UpdateAsync(UserDashboardSettings)`.

## API / Interface Design

### New (Application layer)
- `Anela.Heblo.Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsLock.cs`
- `Anela.Heblo.Application/Features/Dashboard/Infrastructure/UserDashboardSettingsLock.cs` (default `SemaphoreSlim`-per-user implementation)
- DI registration: extend the existing Application module's `AddDashboardFeature` (or equivalent) extension; if no such method exists, add one to keep registrations co-located with the feature.

### Removed (Xcc layer)
- `Anela.Heblo.Xcc/Services/Dashboard/DashboardService.cs`
- `Anela.Heblo.Xcc/Services/Dashboard/IDashboardService.cs`
- DI registration of `IDashboardService` in `XccModule.cs`

### Modified (Application layer handlers)
- `GetUserSettingsHandler` — depends on `ITileRegistry`, `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `TimeProvider`. Implements FR-1 and FR-2.
- `GetTileDataHandler` — depends on `IMediator`, `ITileRegistry`, `IOptions<DashboardOptions>`. Implements FR-3.
- `SaveUserSettingsHandler` — depends on `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `TimeProvider`. Existing tile-merge logic kept; persistence direct.
- `EnableTileHandler` / `DisableTileHandler` — depend on the same trio as `SaveUserSettingsHandler` plus `IMediator`; replace `_dashboardService.GetUserSettingsAsync` with an `IMediator.Send(new GetUserSettingsRequest {...})` call before acquiring the write lock, then re-read via repository inside the lock to merge tile changes, then call `_repository.UpdateAsync(settings)`.

### Unchanged
- HTTP controllers / endpoints, request/response DTOs, OpenAPI surface, `ITileRegistry`, all `ITile` implementations.

## Dependencies
- MediatR (already used).
- `Anela.Heblo.Xcc` (already referenced by Application; reference direction unchanged).
- `Microsoft.Extensions.Options` (for `DashboardOptions`, already used).
- No new NuGet packages.

## Out of Scope
- Adding, removing, or modifying tiles or the `ITileRegistry` contract.
- Moving `UserDashboardSettings` / `UserDashboardTile` out of `Xcc.Domain`.
- Replacing the in-memory `SemaphoreSlim` lock with a distributed lock — the app runs as a single instance per environment (single Docker container, single Azure Web App slot) so the in-memory lock remains correct.
- Frontend changes; the OpenAPI client must regenerate to a byte-identical Dashboard slice (FR-9).
- E2E test changes; existing nightly Playwright suite continues to cover the user-visible behavior.
- Idle-lock eviction / memory cleanup for `UserDashboardSettingsLock`.
- Refactoring `GetAvailableTilesHandler` (already correct).

## Open Questions
None.

## Status: COMPLETE