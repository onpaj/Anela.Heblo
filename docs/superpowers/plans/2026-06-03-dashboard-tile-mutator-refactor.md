# Dashboard Tile Mutator Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the duplicated provision/lock/load/stamp/persist scaffold between `EnableTileHandler` and `DisableTileHandler` by extracting it into a single `UserDashboardSettingsMutator` collaborator parameterized by per-handler diff delegates, with zero changes to MediatR contracts, HTTP surface, or the generated TypeScript client.

**Architecture:** Introduce `internal sealed class UserDashboardSettingsMutator` behind `internal interface IUserDashboardSettingsMutator` under `Application/Features/Dashboard/Infrastructure/`. The mutator owns the four infrastructure dependencies (`IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`, `TimeProvider`, `IMediator`), the `userId` → `"anonymous"` normalization, the pre-lock `GetUserSettingsRequest` provisioning call, lock acquisition, repository load + null-guard, single-read `LastModified` stamping (both `tile.LastModified` and `settings.LastModified`), and `UpdateAsync`. Each handler shrinks to a `TileId` guard plus a `MutateAsync` call with two tiny closures (`onTileFound`, `onTileMissing`). Both handlers are flipped to `internal sealed` to match the mutator's visibility (CS0051 requires it; matches the existing precedent in `Features/FeatureFlags/**/*Handler.cs`).

**Tech Stack:** .NET 8, C# 12, MediatR 12, Moq + FluentAssertions + xUnit, EF Core change-tracking on `UserDashboardSettings`/`UserDashboardTile` entities.

---

## Scope and Non-Scope

**In scope (this plan):**
- New types: `IUserDashboardSettingsMutator`, `UserDashboardSettingsMutator`, `UserDashboardSettingsMutationResult` (co-located with interface).
- DI registration: one `AddScoped` line in `DashboardModule.AddDashboardModule`.
- `EnableTileHandler.cs` rewritten as a thin shell with one constructor dependency.
- `DisableTileHandler.cs` rewritten as a thin shell with one constructor dependency.
- Handler visibility lowered from `public` → `internal sealed`.
- Test fixture wiring in `EnableTileHandlerTests` and `DisableTileHandlerTests` updated so each test class constructs the handler around a *real* `UserDashboardSettingsMutator` wrapping the existing infrastructure mocks. **All `[Fact]` bodies (Arrange/Act/Assert outside the ctor) must remain byte-identical.**

**Out of scope (do not touch in this PR):**
- `EnableTileRequest`, `EnableTileResponse`, `DisableTileRequest`, `DisableTileResponse` — zero-line diff.
- `DashboardController.cs` — zero-line diff.
- `frontend/src/api-client/**` — must be byte-identical before vs. after `npm run build` (FR-5).
- `SaveUserSettingsHandler`, `GetUserSettingsHandler`, `GetTileDataHandler`, `GetAvailableTilesHandler` — known follow-up opportunity for `SaveUserSettingsHandler`; do not absorb it here.
- `IUserDashboardSettingsLock` / `UserDashboardSettingsLock` — unchanged.
- Database schema, EF migrations, `appsettings.*.json`, Key Vault entries.
- Any frontend change.

---

## File Structure

```
backend/src/Anela.Heblo.Application/Features/Dashboard/
├── DashboardModule.cs                                  MODIFY  +1 AddScoped line
├── Infrastructure/
│   ├── IUserDashboardSettingsLock.cs                   UNCHANGED
│   ├── UserDashboardSettingsLock.cs                    UNCHANGED
│   ├── IUserDashboardSettingsMutator.cs                CREATE  contains interface + result record struct
│   └── UserDashboardSettingsMutator.cs                 CREATE  internal sealed implementation
└── UseCases/
    ├── EnableTile/
    │   ├── EnableTileHandler.cs                        REWRITE thin shell, 1 dep, internal sealed
    │   ├── EnableTileRequest.cs                        UNCHANGED
    │   └── EnableTileResponse.cs                       UNCHANGED
    └── DisableTile/
        ├── DisableTileHandler.cs                       REWRITE thin shell, 1 dep, internal sealed
        ├── DisableTileRequest.cs                       UNCHANGED
        └── DisableTileResponse.cs                      UNCHANGED

backend/test/Anela.Heblo.Tests/Features/Dashboard/
├── EnableTileHandlerTests.cs                           MODIFY  ctor only — wire real mutator
└── DisableTileHandlerTests.cs                          MODIFY  ctor only — wire real mutator
```

Responsibility map per new/modified file:

- **`IUserDashboardSettingsMutator.cs`** — interface + `UserDashboardSettingsMutationResult` record struct co-located (single declaration site, small types). Contains the `<remarks>` invariants (provisioning before lock, single timestamp read, delegates must not stamp `LastModified`).
- **`UserDashboardSettingsMutator.cs`** — `internal sealed` implementation. Owns userId normalization, provisioning send, lock acquire, repo load, null-guard, single `TimeProvider` read, delegate dispatch, `tile.LastModified` + `settings.LastModified` stamping, conditional `UpdateAsync`.
- **`DashboardModule.cs`** — adds one `services.AddScoped<IUserDashboardSettingsMutator, UserDashboardSettingsMutator>()` registration.
- **`EnableTileHandler.cs`** — `internal sealed`. Validates `TileId`. Calls `MutateAsync` with `onTileFound: (_, tile) => tile.IsVisible = true` and `onTileMissing: (settings, resolvedUserId) => new UserDashboardTile { ... DisplayOrder = max+1 ... }`. Returns `EnableTileResponse`.
- **`DisableTileHandler.cs`** — `internal sealed`. Validates `TileId`. Calls `MutateAsync` with `onTileFound: (_, tile) => tile.IsVisible = false` and `onTileMissing: null`. Returns `DisableTileResponse`.
- **`EnableTileHandlerTests.cs` / `DisableTileHandlerTests.cs`** — only the constructor changes: build a real `UserDashboardSettingsMutator` around the existing four mocks, then pass it to the handler.

---

### Visibility decision (one-time call-out)

`IUserDashboardSettingsMutator` and `UserDashboardSettingsMutator` are `internal` per spec FR-1 and arch-review Decision 3. Because a `public` handler with an `internal` constructor parameter triggers CS0051 ("Inconsistent accessibility"), both `EnableTileHandler` and `DisableTileHandler` are declared `internal sealed`. This matches the existing precedent in `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/**/*Handler.cs` (all `internal sealed`). MediatR registration in `ApplicationModule.cs:61` uses `RegisterServicesFromAssembly`, which discovers internal handlers via reflection — no DI changes required. `Anela.Heblo.Tests` already has `InternalsVisibleTo` (verified in `Anela.Heblo.Application.csproj` and `AssemblyInfo.cs`), so the existing test classes can keep instantiating handlers directly.

---

## Task 1: Add `IUserDashboardSettingsMutator` interface and result record struct

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsMutator.cs`

- [ ] **Step 1: Create the interface file**

Write `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsMutator.cs` with the following exact content:

```csharp
using Anela.Heblo.Domain.Features.Dashboard;

namespace Anela.Heblo.Application.Features.Dashboard.Infrastructure;

/// <summary>
/// Encapsulates the shared scaffold for per-user UserDashboardSettings mutations:
/// userId normalization, pre-lock provisioning, lock acquisition, repository load,
/// LastModified stamping, and conditional persistence.
/// </summary>
/// <remarks>
/// <para>
/// <b>Provisioning order:</b> The mutator issues <c>GetUserSettingsRequest</c> via MediatR
/// BEFORE acquiring <see cref="IUserDashboardSettingsLock"/>. The underlying lock is
/// non-reentrant — invoking GetUserSettingsHandler from inside the lock would deadlock.
/// </para>
/// <para>
/// <b>Timestamping:</b> The mutator reads <c>TimeProvider.GetUtcNow()</c> exactly once per
/// invocation and reuses the value for both <c>UserDashboardTile.LastModified</c> (touched
/// or appended tile) and <c>UserDashboardSettings.LastModified</c>. Callers must NOT mutate
/// <c>LastModified</c> in the supplied delegates; the mutator owns those fields.
/// </para>
/// <para>
/// <b>Persistence:</b> <c>UpdateAsync</c> is called only when a tile was found or
/// appended. The "tile missing + <paramref name="onTileMissing"/> is null or returns null"
/// branch performs no write — preserving today's DisableTileHandler semantics.
/// </para>
/// </remarks>
internal interface IUserDashboardSettingsMutator
{
    /// <param name="userId">
    /// Caller-supplied user id. Null or empty is normalized to <c>"anonymous"</c> inside
    /// the mutator — handlers must not pre-normalize.
    /// </param>
    /// <param name="tileId">Identifier of the tile to mutate. Must be non-empty.</param>
    /// <param name="onTileFound">
    /// Invoked when a tile with <paramref name="tileId"/> already exists. Mutate domain
    /// fields (e.g. <c>IsVisible</c>) only; do NOT touch <c>LastModified</c>.
    /// </param>
    /// <param name="onTileMissing">
    /// Optional. When the tile is missing and this delegate is supplied, it is invoked
    /// with the loaded <see cref="UserDashboardSettings"/> and the resolved user id; it
    /// returns a new <see cref="UserDashboardTile"/> to append, or <c>null</c> to skip
    /// persistence. When this delegate is <c>null</c>, no write occurs and the result's
    /// <c>TileAppended</c> is <c>false</c>.
    /// </param>
    Task<UserDashboardSettingsMutationResult> MutateAsync(
        string? userId,
        string tileId,
        Action<UserDashboardSettings, UserDashboardTile> onTileFound,
        Func<UserDashboardSettings, string, UserDashboardTile?>? onTileMissing,
        CancellationToken cancellationToken);
}

internal readonly record struct UserDashboardSettingsMutationResult(
    bool SettingsLoaded,
    bool TileFound,
    bool TileAppended);
```

- [ ] **Step 2: Verify the project compiles**

Run from the worktree root:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` The new file declares two unused internal types; no consumers yet, so no warnings.

- [ ] **Step 3: Run the existing dashboard tests to confirm nothing regressed**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Dashboard" --no-restore
```

Expected: all existing tests pass (including 7 `EnableTileHandlerTests` + 7 `DisableTileHandlerTests`).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsMutator.cs
git commit -m "refactor(dashboard): add IUserDashboardSettingsMutator contract"
```

---

## Task 2: Implement `UserDashboardSettingsMutator`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/UserDashboardSettingsMutator.cs`

- [ ] **Step 1: Create the implementation file**

Write `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/UserDashboardSettingsMutator.cs` with the following exact content:

```csharp
using Anela.Heblo.Application.Features.Dashboard.UseCases.GetUserSettings;
using Anela.Heblo.Domain.Features.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.Infrastructure;

internal sealed class UserDashboardSettingsMutator : IUserDashboardSettingsMutator
{
    private readonly IUserDashboardSettingsRepository _repository;
    private readonly IUserDashboardSettingsLock _lock;
    private readonly TimeProvider _timeProvider;
    private readonly IMediator _mediator;

    public UserDashboardSettingsMutator(
        IUserDashboardSettingsRepository repository,
        IUserDashboardSettingsLock @lock,
        TimeProvider timeProvider,
        IMediator mediator)
    {
        _repository = repository;
        _lock = @lock;
        _timeProvider = timeProvider;
        _mediator = mediator;
    }

    public async Task<UserDashboardSettingsMutationResult> MutateAsync(
        string? userId,
        string tileId,
        Action<UserDashboardSettings, UserDashboardTile> onTileFound,
        Func<UserDashboardSettings, string, UserDashboardTile?>? onTileMissing,
        CancellationToken cancellationToken)
    {
        var resolvedUserId = string.IsNullOrEmpty(userId) ? "anonymous" : userId;

        // Trigger provisioning outside the write lock (lock is non-reentrant).
        await _mediator.Send(new GetUserSettingsRequest { UserId = resolvedUserId }, cancellationToken);

        await using var lockHandle = await _lock.AcquireAsync(resolvedUserId, cancellationToken);

        var settings = await _repository.GetByUserIdAsync(resolvedUserId);
        if (settings == null)
        {
            return new UserDashboardSettingsMutationResult(
                SettingsLoaded: false,
                TileFound: false,
                TileAppended: false);
        }

        var now = _timeProvider.GetUtcNow().DateTime;
        var existingTile = settings.Tiles.FirstOrDefault(t => t.TileId == tileId);

        if (existingTile != null)
        {
            onTileFound(settings, existingTile);
            existingTile.LastModified = now;
            settings.LastModified = now;
            await _repository.UpdateAsync(settings);
            return new UserDashboardSettingsMutationResult(
                SettingsLoaded: true,
                TileFound: true,
                TileAppended: false);
        }

        if (onTileMissing == null)
        {
            return new UserDashboardSettingsMutationResult(
                SettingsLoaded: true,
                TileFound: false,
                TileAppended: false);
        }

        var newTile = onTileMissing(settings, resolvedUserId);
        if (newTile == null)
        {
            return new UserDashboardSettingsMutationResult(
                SettingsLoaded: true,
                TileFound: false,
                TileAppended: false);
        }

        newTile.LastModified = now;
        settings.Tiles.Add(newTile);
        settings.LastModified = now;
        await _repository.UpdateAsync(settings);
        return new UserDashboardSettingsMutationResult(
            SettingsLoaded: true,
            TileFound: false,
            TileAppended: true);
    }
}
```

Behavior notes (verify by reading the code, not by adding new tests):

- `userId` normalization happens exactly once, at the top.
- The pre-lock `_mediator.Send` uses the normalized `resolvedUserId` — identical to what `EnableTileHandler.cs:37` and `DisableTileHandler.cs:37` do today.
- `_timeProvider.GetUtcNow()` is invoked exactly once per `MutateAsync` call (`var now = ...`) and reused for both timestamp assignments. This is a stricter invariant than today's handlers (which read it 2–3 times); the existing tests don't care because the mocked TimeProvider returns a constant.
- `existingTile.LastModified` is stamped by the mutator *after* `onTileFound` returns — guaranteeing the mutator-owned timestamp, even if a future delegate accidentally sets it.
- For the appended-tile path, the mutator overwrites whatever `LastModified` the delegate may have left on the new `UserDashboardTile` instance before adding it to the collection.
- When `onTileMissing` is `null` OR returns `null`, no `UpdateAsync` call is made. This preserves Disable's no-op-on-missing behavior.

- [ ] **Step 2: Verify the project compiles**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` The new class is unused by handlers but satisfies the interface — Roslyn will not warn about an unused internal class because the interface is `internal` and the implementation is registered later via DI.

- [ ] **Step 3: Run the existing dashboard tests to confirm nothing regressed**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Dashboard" --no-restore
```

Expected: all existing tests still pass. The mutator is not yet wired into any handler, so handler behavior is unchanged.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/UserDashboardSettingsMutator.cs
git commit -m "refactor(dashboard): implement UserDashboardSettingsMutator"
```

---

## Task 3: Register `IUserDashboardSettingsMutator` in `DashboardModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs:18-20`

- [ ] **Step 1: Add the scoped registration**

Open `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs`. Locate the line registering `IUserDashboardSettingsLock` (line 18). Insert a new registration directly after it. The resulting `AddDashboardModule` method body must read exactly:

```csharp
public static IServiceCollection AddDashboardModule(this IServiceCollection services)
{
    // MediatR handlers are automatically registered by the ApplicationModule

    // Hangfire storage singleton — resolved lazily after Hangfire is configured
    services.AddSingleton(_ => JobStorage.Current);

    // Per-user async lock for serializing concurrent UserDashboardSettings mutations
    services.AddSingleton<IUserDashboardSettingsLock, UserDashboardSettingsLock>();

    // Shared scaffold for Enable/Disable tile (and future) mutations
    services.AddScoped<IUserDashboardSettingsMutator, UserDashboardSettingsMutator>();

    return services;
}
```

Use `Edit` with `old_string` matching the trailing `services.AddSingleton<IUserDashboardSettingsLock, UserDashboardSettingsLock>();\n\n        return services;` block, and `new_string` inserting the new line + comment + blank line before `return services;`.

- [ ] **Step 2: Verify the project compiles**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Run the existing dashboard tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Dashboard" --no-restore
```

Expected: all existing tests still pass. The DI registration is unused by current handlers, so behavior is unchanged.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs
git commit -m "refactor(dashboard): register IUserDashboardSettingsMutator as scoped"
```

---

## Task 4: Refactor `EnableTileHandler` and update its test fixture

**Files:**
- Rewrite: `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/EnableTile/EnableTileHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Dashboard/EnableTileHandlerTests.cs` (constructor only — `[Fact]` bodies stay byte-identical)

This task is atomic: the handler's constructor signature changes from four parameters to one, so the test fixture must be updated in the same commit to keep the build green.

- [ ] **Step 1: Rewrite `EnableTileHandler.cs`**

Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/EnableTile/EnableTileHandler.cs` with:

```csharp
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Dashboard;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.EnableTile;

internal sealed class EnableTileHandler : IRequestHandler<EnableTileRequest, EnableTileResponse>
{
    private readonly IUserDashboardSettingsMutator _mutator;

    public EnableTileHandler(IUserDashboardSettingsMutator mutator)
    {
        _mutator = mutator;
    }

    public async Task<EnableTileResponse> Handle(EnableTileRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.TileId))
        {
            return new EnableTileResponse(ErrorCodes.RequiredFieldMissing);
        }

        await _mutator.MutateAsync(
            request.UserId,
            request.TileId,
            onTileFound: static (_, tile) => tile.IsVisible = true,
            onTileMissing: (settings, resolvedUserId) =>
            {
                var maxOrder = settings.Tiles.Any() ? settings.Tiles.Max(t => t.DisplayOrder) : -1;
                return new UserDashboardTile
                {
                    UserId = resolvedUserId,
                    TileId = request.TileId,
                    IsVisible = true,
                    DisplayOrder = maxOrder + 1,
                    DashboardSettings = settings
                };
            },
            cancellationToken);

        return new EnableTileResponse();
    }
}
```

Key invariants preserved (cross-check against `EnableTileHandler.cs:27-72` before the change):

- `TileId` null/empty short-circuit returns `EnableTileResponse(ErrorCodes.RequiredFieldMissing)` *before* any mediator/lock/repo interaction. (Required by `Handle_WhenTileIdIsNull_ShouldReturnFailure` and `Handle_WhenTileIdIsEmpty_ShouldReturnFailure`.)
- `onTileFound` flips `IsVisible = true` only — it does NOT touch `LastModified` (mutator owns it).
- `onTileMissing` computes `maxOrder = settings.Tiles.Any() ? settings.Tiles.Max(t => t.DisplayOrder) : -1` and assigns `DisplayOrder = maxOrder + 1`. This arithmetic exactly mirrors `EnableTileHandler.cs:55,61` pre-refactor. (Required by `Handle_WhenTileDoesNotExist_ShouldAddNewTile` line `newTile.DisplayOrder.Should().Be(originalTileCount)`.)
- The closure uses `resolvedUserId` (passed in by the mutator), so the appended tile's `UserId` is `"anonymous"` when the request's `UserId` was null. (Required by `Handle_WhenUserIdIsNull_ShouldUseAnonymous`.)
- The `DashboardSettings = settings` back-reference is preserved (matches `EnableTileHandler.cs:63`).
- Return value is always `new EnableTileResponse()` after the `MutateAsync` call — the mutator's `MutationResult` is intentionally discarded because the existing public response surface conveys success/failure only via the `ErrorCodes` it doesn't carry.

- [ ] **Step 2: Update the `EnableTileHandlerTests` constructor**

Open `backend/test/Anela.Heblo.Tests/Features/Dashboard/EnableTileHandlerTests.cs`. The only change is in the test class's constructor (line 22–53). Replace the existing `_handler = new EnableTileHandler(...)` block with one that builds a real `UserDashboardSettingsMutator` and passes it to the handler.

Use `Edit` to replace this exact block:

```csharp
        _handler = new EnableTileHandler(
            _repositoryMock.Object,
            _lockMock.Object,
            _timeProvider,
            _mediatorMock.Object);
```

with:

```csharp
        var mutator = new UserDashboardSettingsMutator(
            _repositoryMock.Object,
            _lockMock.Object,
            _timeProvider,
            _mediatorMock.Object);

        _handler = new EnableTileHandler(mutator);
```

**Do not change any other line in this file.** Every `[Fact]` body, the `CreateSampleUserSettings` helper, the `FixedUtcNow` constant, and the mock setup block stay byte-identical.

(The `using Anela.Heblo.Application.Features.Dashboard.Infrastructure;` import already exists at line 1, so `UserDashboardSettingsMutator` resolves.)

- [ ] **Step 3: Build the solution**

```bash
dotnet build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` The atomic rewrite of handler + test ctor keeps the test assembly compiling.

- [ ] **Step 4: Run the `EnableTileHandlerTests` class**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~EnableTileHandlerTests" --no-build
```

Expected output: 7 tests passed (`Handle_WhenUserIdIsNull_ShouldUseAnonymous`, `Handle_WhenTileExists_ShouldEnableTile`, `Handle_WhenTileDoesNotExist_ShouldAddNewTile`, `Handle_WhenTileIdIsNull_ShouldReturnFailure`, `Handle_WhenTileIdIsEmpty_ShouldReturnFailure`, `Handle_AcquiresLockOncePerCall`, `Handle_SendsGetUserSettingsBeforeAcquiringLock`).

If any test fails:

- `Handle_SendsGetUserSettingsBeforeAcquiringLock` failure → the mutator is calling `_lock.AcquireAsync` before `_mediator.Send`. Re-read `MutateAsync` ordering.
- `Handle_WhenTileExists_ShouldEnableTile` / `…ShouldDisableTile` failure on `enabledTile.LastModified.Should().Be(FixedUtcNow)` → the mutator is not stamping `existingTile.LastModified` after the delegate call.
- `Handle_WhenTileDoesNotExist_ShouldAddNewTile` failure on `DisplayOrder` → the closure arithmetic is wrong; verify `maxOrder + 1`.
- `Handle_WhenTileDoesNotExist_ShouldAddNewTile` failure on `UserId` → the closure used `request.UserId` instead of `resolvedUserId`.
- `Handle_WhenTileIdIsNull_ShouldReturnFailure` / `…IsEmpty…` failure → the handler is invoking `MutateAsync` before the `TileId` guard.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/EnableTile/EnableTileHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Dashboard/EnableTileHandlerTests.cs
git commit -m "refactor(dashboard): rewrite EnableTileHandler against mutator"
```

---

## Task 5: Refactor `DisableTileHandler` and update its test fixture

**Files:**
- Rewrite: `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/DisableTile/DisableTileHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Dashboard/DisableTileHandlerTests.cs` (constructor only)

Same atomic-rewrite pattern as Task 4. The Disable handler is even simpler because it has no `onTileMissing` branch.

- [ ] **Step 1: Rewrite `DisableTileHandler.cs`**

Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/DisableTile/DisableTileHandler.cs` with:

```csharp
using Anela.Heblo.Application.Features.Dashboard.Infrastructure;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.DisableTile;

internal sealed class DisableTileHandler : IRequestHandler<DisableTileRequest, DisableTileResponse>
{
    private readonly IUserDashboardSettingsMutator _mutator;

    public DisableTileHandler(IUserDashboardSettingsMutator mutator)
    {
        _mutator = mutator;
    }

    public async Task<DisableTileResponse> Handle(DisableTileRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.TileId))
        {
            return new DisableTileResponse(ErrorCodes.RequiredFieldMissing);
        }

        await _mutator.MutateAsync(
            request.UserId,
            request.TileId,
            onTileFound: static (_, tile) => tile.IsVisible = false,
            onTileMissing: null,
            cancellationToken);

        return new DisableTileResponse();
    }
}
```

Key invariants preserved (cross-check against `DisableTileHandler.cs:27-59` before the change):

- `TileId` null/empty short-circuit. (Required by `Handle_WhenTileIdIsNull_ShouldReturnFailure` / `…IsEmpty…`.)
- `onTileFound` flips `IsVisible = false` only — does NOT touch `LastModified`.
- `onTileMissing` is `null` → mutator skips `UpdateAsync` when the tile doesn't exist. (Required by `Handle_WhenTileDoesNotExist_ShouldNotCallUpdate`.)
- Always returns `new DisableTileResponse()` on the success path.
- The `using Anela.Heblo.Domain.Features.Dashboard;` import is no longer needed (no `UserDashboardTile` reference in this file post-refactor) — drop it for cleanliness; `dotnet format` would otherwise flag it.

- [ ] **Step 2: Update the `DisableTileHandlerTests` constructor**

Open `backend/test/Anela.Heblo.Tests/Features/Dashboard/DisableTileHandlerTests.cs`. Replace this exact block (around line 48–52):

```csharp
        _handler = new DisableTileHandler(
            _repositoryMock.Object,
            _lockMock.Object,
            _timeProvider,
            _mediatorMock.Object);
```

with:

```csharp
        var mutator = new UserDashboardSettingsMutator(
            _repositoryMock.Object,
            _lockMock.Object,
            _timeProvider,
            _mediatorMock.Object);

        _handler = new DisableTileHandler(mutator);
```

**Do not change any other line.** All seven `[Fact]` methods, the `CreateSampleUserSettings` helper, the `FixedUtcNow` constant, and the mock setup stay byte-identical.

- [ ] **Step 3: Build the solution**

```bash
dotnet build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Run the `DisableTileHandlerTests` class**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DisableTileHandlerTests" --no-build
```

Expected output: 7 tests passed.

If `Handle_WhenTileDoesNotExist_ShouldNotCallUpdate` fails, the mutator is calling `UpdateAsync` even when `onTileMissing` is `null` — re-check the "if (onTileMissing == null) return …" branch in `UserDashboardSettingsMutator.MutateAsync`.

If `Handle_WhenTileExists_ShouldDisableTile` fails on `capturedSettings.LastModified.Should().Be(FixedUtcNow)`, the mutator isn't stamping `settings.LastModified` on the tile-found path.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/DisableTile/DisableTileHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Dashboard/DisableTileHandlerTests.cs
git commit -m "refactor(dashboard): rewrite DisableTileHandler against mutator"
```

---

## Task 6: Full verification — build, format, all tests, controller surface, OpenAPI byte-identity

**Files (verification only):**
- Read: `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs` (must have zero diff vs `main`)
- Read: `frontend/src/api-client/**` (must be byte-identical after `npm run build`)

- [ ] **Step 1: Format the touched backend files**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes --include \
  backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsMutator.cs \
  backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/UserDashboardSettingsMutator.cs \
  backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs \
  backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/EnableTile/EnableTileHandler.cs \
  backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/DisableTile/DisableTileHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/Dashboard/EnableTileHandlerTests.cs \
  backend/test/Anela.Heblo.Tests/Features/Dashboard/DisableTileHandlerTests.cs
```

Expected: exit code 0, no diff. If non-zero, re-run without `--verify-no-changes` to apply fixes, then re-verify.

- [ ] **Step 2: Confirm `DashboardController.cs` is unchanged**

```bash
git diff main -- backend/src/Anela.Heblo.API/Controllers/DashboardController.cs
```

Expected: empty output. The HTTP surface (`POST /api/dashboard/tiles/{tileId}/enable` and `/disable`) is unchanged per FR-5.

- [ ] **Step 3: Confirm the request/response contracts are unchanged**

```bash
git diff main -- \
  backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/EnableTile/EnableTileRequest.cs \
  backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/EnableTile/EnableTileResponse.cs \
  backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/DisableTile/DisableTileRequest.cs \
  backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/DisableTile/DisableTileResponse.cs
```

Expected: empty output.

- [ ] **Step 4: Run the full dashboard test set + controller tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Dashboard|FullyQualifiedName~Controllers.DashboardController" \
  --no-build
```

Expected: all tests in `EnableTileHandlerTests`, `DisableTileHandlerTests`, and `DashboardControllerTests` pass. No new flaky tests.

- [ ] **Step 5: Run the full backend test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build
```

Expected: green. This catches accidental regressions outside the Dashboard slice (e.g. if `RegisterServicesFromAssembly` discovery somehow stopped picking up the now-internal handlers — extremely unlikely, but cheap to confirm).

- [ ] **Step 6: Confirm OpenAPI / TypeScript client byte-identity**

```bash
cd frontend
npm run build
cd ..
git diff --stat -- frontend/src/api-client/
```

Expected: empty `git diff` output. The Application-layer refactor must not perturb the OpenAPI document (controller routes, request/response shapes are unchanged), so the generated TypeScript client is byte-identical.

If the diff is non-empty, something in the public surface drifted — inspect the diff and trace back to whichever file regenerated. Most likely cause: the handler files were accidentally not made `internal` and OpenAPI metadata picked them up (it shouldn't, but verify).

- [ ] **Step 7: Final combined verification**

```bash
dotnet build && dotnet format --verify-no-changes
```

Expected: both commands exit 0.

- [ ] **Step 8: Commit any formatting deltas (if Step 1 re-ran format)**

If `dotnet format` made changes in Step 1, commit them:

```bash
git add -u backend/
git commit -m "chore(dashboard): apply dotnet format"
```

Otherwise skip this step.

---

## Acceptance Mapping (spec FR / NFR → tasks)

| Requirement | Covered by |
|---|---|
| FR-1 (introduce `UserDashboardSettingsMutator`) | Tasks 1 + 2 + 3 |
| FR-2 (provisioning before lock) | Task 2 (mutator implementation) — guarded by existing `Handle_SendsGetUserSettingsBeforeAcquiringLock` tests run in Tasks 4 & 5 |
| FR-3 (rewrite `EnableTileHandler`) | Task 4 |
| FR-4 (rewrite `DisableTileHandler`) | Task 5 |
| FR-5 (public contracts unchanged) | Task 6 Steps 2, 3, 6 |
| FR-6 (update existing handler tests; preferred = real mutator + mocked infra) | Tasks 4 & 5 (Step 2 of each) |
| FR-7 (controller tests unchanged) | Task 6 Step 4 |
| NFR-1 (no extra repo/lock/mediator calls) | Mutator design (Task 2) — code review |
| NFR-2 (security: `[Authorize]` unchanged, `anonymous` fallback preserved) | Task 6 Step 2 (controller diff) + Task 2 (normalization in mutator) |
| NFR-3 (combined handler LOC < 50% of pre-refactor) | Task 4 + 5 — `EnableTileHandler.cs` reduces from 72 → ~38 lines; `DisableTileHandler.cs` from 59 → ~25 lines; combined drops from 131 → ~63 (~48%) |
| NFR-3 (mutator < 150 LOC) | Task 2 — implementation is ~75 LOC |
| NFR-3 (`dotnet format` clean) | Task 6 Steps 1 & 7 |
| NFR-4 (DB schema, appsettings, KV unchanged) | Nothing in this plan touches those |

---

## Risk-Specific Verifications (from arch-review)

| Risk (from arch-review) | Verification step |
|---|---|
| Closure inside `onTileMissing` needs resolved `userId` | Task 4 Step 1 (closure uses `resolvedUserId` parameter, not `request.UserId`) — exercised by `Handle_WhenUserIdIsNull_ShouldUseAnonymous` in Task 4 Step 4 |
| Mutator forgets to stamp `tile.LastModified` for appended tile | Task 2 implementation stamps `newTile.LastModified = now` before `settings.Tiles.Add(newTile)` — verified by `Handle_WhenTileDoesNotExist_ShouldAddNewTile` |
| Two `TimeProvider.GetUtcNow()` reads return different values | Task 2 reads once into `var now` — verified by `Handle_WhenTileExists_ShouldDisableTile` (`disabledTile.LastModified == capturedSettings.LastModified`) |
| Test refactor accidentally weakens assertion | Tasks 4 & 5 Step 2 instructions explicitly mandate "do not change any other line" — reviewer must `git diff main -- backend/test/.../EnableTileHandlerTests.cs` and confirm only the ctor block changed |
| Mutator `Scoped` registration conflicts with singleton lock | None — DI tolerates scoped depending on singleton |

---

## Out-of-Scope Reminder

Do NOT, in this PR:

- Absorb `SaveUserSettingsHandler` into the mutator (known follow-up; needs loop-shaped invocation).
- Replace the static-state-equivalent `UserDashboardSettingsLock` (tracked separately under `feat-arch-review-dashboard-static-userlocks-d`).
- Touch `userId` resolution / `GetCurrentUserId` (tracked separately under `feat-arch-review-dashboard-getcurrentuserid-d`).
- Add `UserDashboardSettingsMutatorTests` — coverage is achieved transitively per FR-6 preferred option.
- Change `UserDashboardSettings` / `UserDashboardTile` to immutable types (EF Core change-tracking depends on in-place mutation).
