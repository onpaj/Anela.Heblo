# Specification: Refactor EnableTileHandler / DisableTileHandler duplication

## Summary

`EnableTileHandler` and `DisableTileHandler` in the Dashboard module are structurally identical except for the `IsVisible` flag they set and the "add-if-missing" behavior on Enable. This refactor eliminates that duplication by extracting the shared scaffold — provisioning, lock acquisition, repository load/null-guard, `LastModified` stamping, and persistence — into a single private mutator collaborator (`UserDashboardSettingsMutator`) that both handlers inject and parameterize with their tile-specific diff. The HTTP surface (`POST /api/dashboard/tiles/{tileId}/enable` and `/disable`), MediatR request/response contracts, and observable behavior remain unchanged.

## Background

The arch-review routine flagged two MediatR handlers as having real, load-bearing duplication:

- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/EnableTile/EnableTileHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/DisableTile/DisableTileHandler.cs`

Both handlers share:

1. Identical 4-dependency constructors (`IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `TimeProvider`, `IMediator`).
2. Identical `TileId` validation returning `ErrorCodes.RequiredFieldMissing`.
3. Identical `userId` fallback to `"anonymous"`.
4. Identical "provision-then-lock" pattern: a pre-lock `_mediator.Send(new GetUserSettingsRequest { UserId = userId }, cancellationToken)` to trigger first-time provisioning **outside** the lock, because `IUserDashboardSettingsLock` is non-reentrant.
5. Identical lock acquisition via `await using var lockHandle = await _lock.AcquireAsync(userId, cancellationToken)`.
6. Identical repository load + null-guard (`if (settings == null) return new ...Response();`).
7. Identical "no-op when tile is missing" semantics on Disable; identical "add row with `DisplayOrder = max+1`" semantics on Enable.
8. Identical `settings.LastModified = _timeProvider.GetUtcNow().DateTime` + `UpdateAsync` finalization.

The only semantic differences are:

| Aspect | Enable | Disable |
|---|---|---|
| `IsVisible` set on existing tile | `true` | `false` |
| Tile not found | append new tile with `IsVisible = true` | no-op (no `UpdateAsync` call) |
| `settings.LastModified` + `UpdateAsync` when tile is missing | yes (after append) | no (early exit before any write) |

Two existing test classes (`EnableTileHandlerTests`, `DisableTileHandlerTests`) lock down this behavior, including the **call order** assertion `mediator → lock` (FR-2 below).

### Why act now

The same scaffold will be copied a third time when "move tile", "pin tile", or reorder operations land. The cost of consolidation is small now and grows as more sibling handlers are added.

### Approach selected

The brief offers two options. **Option A (shared collaborator)** is chosen over **Option B (single `SetTileVisibilityHandler` with a `bool isVisible` request property)**:

- Option A preserves the existing MediatR request types (`EnableTileRequest`, `DisableTileRequest`), the existing `EnableTileResponse` / `DisableTileResponse` types, and the existing controller methods — zero call-site changes anywhere in the codebase or in the generated TypeScript client.
- Option A keeps the diff localized to the Application layer and avoids touching the OpenAPI surface.
- Option A is the strategy explicitly endorsed by the arch-review brief ("Extract the shared scaffold … into a private base class or a package-private `UserDashboardSettingsMutator` service").
- Option B collapses two semantically distinct intents ("turn on", which may create; "turn off", which never creates) into a single handler whose behavior depends on a flag — that's a worse model of the domain, not a better one.

## Functional Requirements

### FR-1: Introduce `UserDashboardSettingsMutator` collaborator

Create a new `internal sealed class UserDashboardSettingsMutator` (and matching `internal interface IUserDashboardSettingsMutator`) in `Anela.Heblo.Application/Features/Dashboard/Infrastructure/`. It owns the shared scaffold: provisioning, locking, repository load, null-guard, `LastModified` stamping, and `UpdateAsync` invocation. The diff logic — what to do with an existing tile, what to do when the tile is missing — is supplied by the caller as a delegate.

Proposed surface:

```csharp
internal interface IUserDashboardSettingsMutator
{
    Task<UserDashboardSettingsMutationResult> MutateAsync(
        string userId,
        string tileId,
        Action<UserDashboardSettings, UserDashboardTile> onTileFound,
        Func<UserDashboardSettings, UserDashboardTile?>? onTileMissing,
        CancellationToken cancellationToken);
}

internal readonly record struct UserDashboardSettingsMutationResult(
    bool SettingsLoaded,
    bool TileFound,
    bool TileAppended);
```

Semantics:

- `userId` resolution to `"anonymous"` on null/empty happens **inside** the mutator (single source of truth).
- The mutator runs `_mediator.Send(new GetUserSettingsRequest { UserId = userId }, cancellationToken)` **before** acquiring the lock.
- Acquires the per-user lock via `IUserDashboardSettingsLock.AcquireAsync(userId, ct)` using `await using`.
- Loads `settings = await _repository.GetByUserIdAsync(userId)`. If null, returns `SettingsLoaded = false` without writing.
- Locates the tile by `TileId`:
  - If found: invokes `onTileFound(settings, tile)`. Caller mutates `IsVisible` (and may set `tile.LastModified` itself, but see FR-3 — the mutator stamps `tile.LastModified` for it).
  - If missing: invokes `onTileMissing(settings)` if supplied. Caller returns a `UserDashboardTile` to append, or `null` to no-op.
- After any mutation (tile updated or appended), the mutator stamps both `tile.LastModified` (for the touched/appended tile) and `settings.LastModified` with `_timeProvider.GetUtcNow().DateTime`, then calls `_repository.UpdateAsync(settings)`.
- If `onTileMissing` returned `null` (the Disable case), the mutator performs **no** write — preserving today's `DisableTileHandler` behavior.

Registration: `DashboardModule.AddDashboardModule` registers `IUserDashboardSettingsMutator` as **scoped** (it holds no state beyond its dependencies; matches the lifetime of `IUserDashboardSettingsRepository`).

**Acceptance criteria:**

- A new `IUserDashboardSettingsMutator` + `UserDashboardSettingsMutator` pair exists under `Application/Features/Dashboard/Infrastructure/`.
- Both types are `internal` (consumed only by handlers in the same assembly).
- The mutator has exactly 4 dependencies: `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `TimeProvider`, `IMediator`.
- `DashboardModule.AddDashboardModule` registers it with `services.AddScoped<IUserDashboardSettingsMutator, UserDashboardSettingsMutator>()`.
- The mutator stamps `tile.LastModified` for the touched-or-appended tile and `settings.LastModified` on `UserDashboardSettings` from a single `_timeProvider.GetUtcNow().DateTime` read (or two reads with the same value — the existing handlers already read twice).

### FR-2: Preserve provisioning-before-lock ordering

The pre-lock `GetUserSettingsRequest` call **must remain outside** the `IUserDashboardSettingsLock` scope. The lock is documented as non-reentrant; calling it from inside `GetUserSettingsHandler` (which itself takes the lock) would deadlock.

**Acceptance criteria:**

- `UserDashboardSettingsMutator.MutateAsync` issues `_mediator.Send(new GetUserSettingsRequest { … }, ct)` strictly before the first `_lock.AcquireAsync(userId, ct)` call.
- The existing call-order tests (`Handle_SendsGetUserSettingsBeforeAcquiringLock` in both `EnableTileHandlerTests` and `DisableTileHandlerTests`) continue to pass against the refactored handlers without modification.

### FR-3: Rewrite `EnableTileHandler` against the mutator

`EnableTileHandler` becomes a thin shell: validate `TileId`, delegate to the mutator with diff logic that sets `IsVisible = true` on an existing tile, or returns a new `UserDashboardTile` (with `DisplayOrder = settings.Tiles.Any() ? settings.Tiles.Max(t => t.DisplayOrder) + 1 : 0`, `IsVisible = true`, `UserId = userId`, `DashboardSettings = settings`) for the missing case.

The handler no longer injects `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `TimeProvider`, or `IMediator`. It injects only `IUserDashboardSettingsMutator`.

**Acceptance criteria:**

- `EnableTileHandler` has exactly **one** constructor dependency: `IUserDashboardSettingsMutator`.
- The handler still returns `new EnableTileResponse(ErrorCodes.RequiredFieldMissing)` when `TileId` is null or empty.
- The handler still returns a successful empty `EnableTileResponse()` when settings cannot be loaded (`SettingsLoaded == false`).
- The handler still appends a new `UserDashboardTile` with `DisplayOrder = max(existing) + 1` (or `0` when there are no existing tiles) when the tile is missing. Note: the current code computes `maxOrder = settings.Tiles.Any() ? settings.Tiles.Max(t => t.DisplayOrder) : -1` and then uses `maxOrder + 1`. The refactor must preserve this exact arithmetic.
- All existing tests in `EnableTileHandlerTests` pass without modification to their `Assert` blocks. The `Arrange` blocks may be updated to construct the new collaborator graph.

### FR-4: Rewrite `DisableTileHandler` against the mutator

`DisableTileHandler` becomes a thin shell: validate `TileId`, delegate to the mutator with diff logic that sets `IsVisible = false` on an existing tile, and supplies `null` (or a no-op delegate that returns `null`) for the missing case.

**Acceptance criteria:**

- `DisableTileHandler` has exactly **one** constructor dependency: `IUserDashboardSettingsMutator`.
- Returns `new DisableTileResponse(ErrorCodes.RequiredFieldMissing)` when `TileId` is null or empty.
- Returns a successful empty `DisableTileResponse()` in all other cases.
- When the tile is missing, **no** `_repository.UpdateAsync` call is made (verified by the existing `Handle_WhenTileDoesNotExist_ShouldNotCallUpdate` test).
- All existing tests in `DisableTileHandlerTests` pass without modification to their `Assert` blocks.

### FR-5: Public contracts and HTTP surface unchanged

`EnableTileRequest`, `EnableTileResponse`, `DisableTileRequest`, `DisableTileResponse`, and the `DashboardController` endpoints (`POST /api/dashboard/tiles/{tileId}/enable`, `POST /api/dashboard/tiles/{tileId}/disable`) are **not modified**. No OpenAPI regeneration is required for the frontend TypeScript client.

**Acceptance criteria:**

- Diff in `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs` is zero lines.
- Diff in the four `*Request.cs` / `*Response.cs` files under `EnableTile/` and `DisableTile/` is zero lines.
- The generated TypeScript client under `frontend/src/api-client/` is byte-identical before vs. after `npm run build`.

### FR-6: Update existing handler tests

The existing tests in `backend/test/Anela.Heblo.Tests/Features/Dashboard/{Enable,Disable}TileHandlerTests.cs` exercise the handlers through their constructors. Those test classes must be updated so the test fixtures construct handlers around the new collaborator while retaining every existing `[Fact]` assertion. Two options are acceptable:

- **Preferred:** keep the existing test surface (each test method body unchanged) but rebuild the fixture to inject a real `UserDashboardSettingsMutator` wired against the existing `Mock<IUserDashboardSettingsRepository>`, `Mock<IUserDashboardSettingsLock>`, `TimeProvider`, and `Mock<IMediator>`. The tests then assert behavior end-to-end through the handler → mutator → mocks, exactly as today.
- **Alternative:** mock `IUserDashboardSettingsMutator` directly in handler tests, and add a separate `UserDashboardSettingsMutatorTests` test class that covers the mutator's behavior with mocked infrastructure.

The preferred option is selected because it preserves the existing call-order, lock-acquired-once, and "uses anonymous when UserId is null" assertions verbatim without splitting them across two test classes.

**Acceptance criteria:**

- `EnableTileHandlerTests` has the same `[Fact]` methods with the same assertions as today, all passing.
- `DisableTileHandlerTests` has the same `[Fact]` methods with the same assertions as today, all passing.
- No existing assertion is weakened, removed, or relaxed.
- Coverage on the new mutator type is ≥ 80% line coverage (achieved transitively through the handler tests if the preferred option is taken).

### FR-7: No behavior change observable from controller layer

All existing tests in `backend/test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs` must continue to pass without modification. No new flaky tests are introduced.

**Acceptance criteria:**

- All existing tests in `DashboardControllerTests.cs` pass without modification.
- No new flaky tests are introduced.

## Non-Functional Requirements

### NFR-1: Performance

The mutator path must add **zero** additional round-trips to the repository or the lock. Compared to today's handlers, the refactor introduces exactly one extra in-process method call (handler → mutator) and one extra delegate invocation per request. No measurable latency impact.

**Acceptance criteria:**

- No additional `_repository.*` calls in the post-refactor code path versus the pre-refactor code path on any branch.
- No additional `_lock.AcquireAsync` calls.
- No additional `_mediator.Send` calls.

### NFR-2: Security

No new attack surface. The mutator does not change auth: the controller still applies `[Authorize]` and resolves `userId` via `GetCurrentUserId()`. The mutator continues to fall back to `"anonymous"` on null/empty `userId` exactly as today.

**Acceptance criteria:**

- The `[Authorize]` attribute on `DashboardController` is unchanged.
- `userId` resolution in the mutator behaves identically to the existing handlers (null/empty → `"anonymous"`).
- No new public API surface introduced.

### NFR-3: Maintainability

**Acceptance criteria:**

- Combined LOC of `EnableTileHandler.cs` + `DisableTileHandler.cs` post-refactor is < 50% of pre-refactor combined LOC.
- The new mutator file is < 150 LOC.
- `dotnet format` produces no diff on touched files.

### NFR-4: Backward compatibility

**Acceptance criteria:**

- Database schema unchanged.
- No new entries in `appsettings.*.json`.
- No new entries in `kv-heblo-stg`.

## Data Model

Unchanged.

- `UserDashboardSettings` (entity, `Domain.Features.Dashboard`): `UserId`, `LastModified`, `Tiles : List<UserDashboardTile>`.
- `UserDashboardTile` (entity): `UserId`, `TileId`, `IsVisible`, `DisplayOrder`, `LastModified`, `DashboardSettings` (back-reference).

Both entities are mutated in-place by the existing code (consistent with EF Core change tracking). The refactor preserves this pattern; introducing immutability here is out of scope.

## API / Interface Design

### HTTP surface (unchanged)

- `POST /api/dashboard/tiles/{tileId}/enable` → `200 OK`
- `POST /api/dashboard/tiles/{tileId}/disable` → `200 OK`

### MediatR contracts (unchanged)

- `EnableTileRequest { string? UserId; string TileId; } → EnableTileResponse : BaseResponse`
- `DisableTileRequest { string? UserId; string TileId; } → DisableTileResponse : BaseResponse`

### New internal contract

```csharp
namespace Anela.Heblo.Application.Features.Dashboard.Infrastructure;

internal interface IUserDashboardSettingsMutator
{
    Task<UserDashboardSettingsMutationResult> MutateAsync(
        string userId,
        string tileId,
        Action<UserDashboardSettings, UserDashboardTile> onTileFound,
        Func<UserDashboardSettings, UserDashboardTile?>? onTileMissing,
        CancellationToken cancellationToken);
}

internal readonly record struct UserDashboardSettingsMutationResult(
    bool SettingsLoaded,
    bool TileFound,
    bool TileAppended);
```

## Dependencies

- `MediatR` (existing) — no version change.
- `Moq` + `FluentAssertions` + `xUnit` (existing test stack) — no version change.
- `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `TimeProvider` (existing) — no changes.
- No new NuGet packages.

## Out of Scope

- **Option B (single `SetTileVisibilityHandler`)** — rejected in favor of preserving the two HTTP endpoints and two MediatR request types.
- **Converting `UserDashboardSettings` / `UserDashboardTile` to immutable types.** EF Core change-tracking relies on in-place mutation.
- **Replacing the static-state `IUserDashboardSettingsLock` implementation.** Tracked separately under `feat-arch-review-dashboard-static-userlocks-d`.
- **Reworking `userId` resolution / `"anonymous"` fallback.** Tracked separately under `feat-arch-review-dashboard-getcurrentuserid-d`.
- **Adding "move tile" / "pin tile" / "reorder tile" handlers.** The refactor enables them; it does not deliver them.
- **Touching `GetUserSettingsHandler`, `GetTileDataHandler`, `SaveUserSettingsHandler`, or `GetAvailableTilesHandler`.**
- **Frontend changes of any kind.**
- **OpenAPI spec changes.**

## Open Questions

None.

## Status: COMPLETE