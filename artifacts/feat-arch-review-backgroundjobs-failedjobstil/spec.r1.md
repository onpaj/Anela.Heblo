# Specification: Decouple `FailedJobsTile` from Hangfire Infrastructure

## Summary
`FailedJobsTile` currently lives in the Application layer but takes the concrete `Hangfire.JobStorage` type as a constructor dependency, violating Clean Architecture's dependency rule (Application must not depend on Infrastructure). This feature introduces a thin Application-owned abstraction (`IFailedJobCounter`), implemented in `Anela.Heblo.API/Infrastructure/Hangfire/`, so the tile no longer compiles against Hangfire types. The change mirrors the existing pattern already used for `IHangfireJobEnqueuer` / `IHangfireRecurringJobScheduler` in the same module.

## Background
- The `BackgroundJobs` module in this codebase has previously been refactored so that all Hangfire-touching adapters (`HangfireJobEnqueuer`, `HangfireRecurringJobScheduler`) were relocated to `Anela.Heblo.API/Infrastructure/Hangfire/`. `BackgroundJobsModule.cs` comments this explicitly: *"Hangfire adapter implementations […] are registered in `Anela.Heblo.API.Extensions.ServiceCollectionExtensions.AddHangfireServices` because their implementations live in the API project (Clean Architecture dependency rule)."*
- `FailedJobsTile.cs` (added later for the dashboard) was missed by that refactor. It still has `using Hangfire;` and depends on `JobStorage`, an infrastructure type.
- The repo's `docs/architecture/development_guidelines.md` requires Application to depend only on abstractions, not on Infrastructure. The daily arch-review routine flagged this on 2026-05-28.
- A second, similar finding exists for `HangfireJobRegistrationHelper` (out of scope for this brief, but the team is aware).
- Existing tests (`FailedJobsTileTests`) currently mock `Mock<JobStorage>` + `Mock<IMonitoringApi>` from Hangfire, which only works because Hangfire types happen to be mockable — the brittle coupling is also a testing problem.

## Functional Requirements

### FR-1: Introduce `IFailedJobCounter` abstraction in Application
Add a new interface in `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IFailedJobCounter.cs`. The interface exposes a single method to return the current failed-job count.

**Shape:**
```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

public interface IFailedJobCounter
{
    Task<long> GetFailedCountAsync(CancellationToken cancellationToken = default);
}
```

**Acceptance criteria:**
- File lives in `Application/Features/BackgroundJobs/Services/`, alongside `IHangfireJobEnqueuer` and `IHangfireRecurringJobScheduler`.
- No `using Hangfire;` or any Hangfire type appears in the file.
- The method is async and accepts a `CancellationToken` (per project async conventions).
- The return type is `Task<long>` to match the existing Hangfire `FailedCount()` return type.

### FR-2: Implement `HangfireFailedJobCounter` in API Infrastructure
Add a concrete implementation in `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireFailedJobCounter.cs`. It wraps `JobStorage.GetMonitoringApi().FailedCount()`.

**Shape:**
```csharp
namespace Anela.Heblo.API.Infrastructure.Hangfire;

public sealed class HangfireFailedJobCounter : IFailedJobCounter
{
    private readonly JobStorage _jobStorage;

    public HangfireFailedJobCounter(JobStorage jobStorage)
    {
        _jobStorage = jobStorage ?? throw new ArgumentNullException(nameof(jobStorage));
    }

    public Task<long> GetFailedCountAsync(CancellationToken cancellationToken = default)
    {
        var count = _jobStorage.GetMonitoringApi().FailedCount();
        return Task.FromResult(count);
    }
}
```

**Acceptance criteria:**
- Lives next to `HangfireJobEnqueuer.cs` and `HangfireRecurringJobScheduler.cs`.
- The class is `sealed` (matches existing infra-adapter style in the project where applicable).
- The implementation does not swallow exceptions — error handling stays in `FailedJobsTile` so the tile's tile-shaped error envelope continues to work (FR-4 verifies this).
- `CancellationToken` is accepted at the API surface even though Hangfire's `FailedCount()` is synchronous and has no cancellation support; this future-proofs the signature.

### FR-3: Refactor `FailedJobsTile` to depend on `IFailedJobCounter`
Modify `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs`:
- Remove `using Hangfire;`.
- Replace `private readonly JobStorage _jobStorage;` with `private readonly IFailedJobCounter _failedJobCounter;`.
- Update the constructor signature.
- Replace `_jobStorage.GetMonitoringApi().FailedCount()` with `await _failedJobCounter.GetFailedCountAsync(cancellationToken)`.
- Change `LoadDataAsync` to `async Task<object>` so it can `await` the counter call.

**Acceptance criteria:**
- `grep -r "Hangfire" backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/` returns no matches.
- `Anela.Heblo.Application.csproj` does not gain (and ideally loses, if no other file in Application needs it — see Open Questions) a `<PackageReference Include="Hangfire.*">`.
- The tile still passes the `cancellationToken` argument through to `GetFailedCountAsync` (was previously ignored at the Hangfire call site, but should be honored now).
- The existing tile metadata (Title, Description, Size, Category, DefaultEnabled, AutoShow, RequiredPermissions, ComponentType, drill-down URL `/hangfire/jobs/failed`) is unchanged.

### FR-4: Preserve tile error-envelope behavior
The tile's existing `try`/`catch` block must continue to produce the same `status = "error"` JSON envelope when the counter throws.

**Acceptance criteria:**
- When `IFailedJobCounter.GetFailedCountAsync` throws, the tile logs via `ILogger<FailedJobsTile>` with message `"Failed to load Hangfire failed job count"` (unchanged) and returns the `status = "error"` envelope with `error = "Failed to retrieve job count. See server logs."` and the drill-down URL.
- Successful path returns the same `status = "success"` envelope with `data.count`, `metadata.lastUpdated`, `metadata.source = "Hangfire"`, and the drill-down block.
- Exceptions do not escape `LoadDataAsync`.

### FR-5: Register the new binding in `AddHangfireServices`
In `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs::AddHangfireServices`, add the DI registration adjacent to the existing Hangfire adapter registrations (around line 345).

**Acceptance criteria:**
- New line: `services.AddScoped<IFailedJobCounter, HangfireFailedJobCounter>();`
- Scoped lifetime matches `IHangfireJobEnqueuer` (line 345). `JobStorage` is itself registered as a singleton by Hangfire's own bootstrap, so any lifetime ≥ Scoped is safe; Scoped is chosen for consistency with the sibling adapter.
- The registration is grouped under the same `// Register Hangfire adapter implementations …` comment block.

### FR-6: Update unit tests to mock the new abstraction
Rewrite `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs` so it no longer references `Hangfire` or `Hangfire.Storage`. It must mock `IFailedJobCounter` instead.

**Acceptance criteria:**
- The test file contains no `using Hangfire;` or `using Hangfire.Storage;` directives.
- `Mock<IFailedJobCounter>` replaces `Mock<JobStorage>` + `Mock<IMonitoringApi>`.
- The four existing test cases keep their names and behavior:
  - `LoadDataAsync_ZeroFailures_ReturnsSuccessWithCountZero`
  - `LoadDataAsync_PositiveFailureCount_ReturnsSuccessWithCount`
  - `LoadDataAsync_MonitoringApiThrows_ReturnsErrorAndDoesNotPropagate` (rename the inner setup to mock `GetFailedCountAsync` throwing; the test name may stay or be renamed to `…CounterThrows…` — see Open Questions)
  - `TileMetadata_MatchesSpec`
- All four tests pass under `dotnet test`.

### FR-7: Add a unit test for `HangfireFailedJobCounter`
Add `backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire/HangfireFailedJobCounterTests.cs` that verifies the adapter calls `JobStorage.GetMonitoringApi().FailedCount()` and returns the value.

**Acceptance criteria:**
- A passing test demonstrates a mocked `JobStorage` whose `GetMonitoringApi().FailedCount()` returns `42L`, and `GetFailedCountAsync` returns `42L`.
- A second test demonstrates that an exception from `FailedCount()` propagates out of `GetFailedCountAsync` unchanged (the catch lives in the tile, not the counter).
- Test location mirrors the existing adapter-test pattern in the repo (if one exists; otherwise place under `backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire/`).

## Non-Functional Requirements

### NFR-1: Performance
- The added abstraction layer is one virtual call. No measurable performance impact on the tile (the tile is called per dashboard refresh, not in hot paths).
- The counter call must remain non-blocking on the request thread — `Task.FromResult` wrapping the synchronous Hangfire call is acceptable because `FailedCount()` returns immediately from in-memory monitoring state in all supported storage backends used by this project.

### NFR-2: Architecture / Layering
- After the change, `Anela.Heblo.Application` must not reference Hangfire in any of its `BackgroundJobs/DashboardTiles/` or `BackgroundJobs/Services/` files except for files explicitly out of scope (e.g. `HangfireJobRegistrationHelper.cs`, tracked separately).
- The Application csproj's package references should not increase; ideally Hangfire is removed entirely from Application if `HangfireJobRegistrationHelper` no longer needs it — but that file is out of scope here, so the package may stay until that follow-up.

### NFR-3: Testability
- `FailedJobsTile` must be unit-testable with no Hangfire types or assemblies referenced from the test file.
- `HangfireFailedJobCounter` is the only place where Hangfire mocks remain.

### NFR-4: Backwards compatibility
- The HTTP/JSON shape returned by the tile (`status`, `data.count`, `metadata`, `drillDown`) must be byte-identical to today, since the dashboard frontend parses this payload.
- The DI registration order does not change observable container behavior; `RegisterTile<FailedJobsTile>` in `BackgroundJobsModule` stays as is.

### NFR-5: Security
- No changes to auth or data exposure. The tile already returns only a count; no secrets or PII are involved. `RequiredPermissions` stays `Array.Empty<string>()`.

## Data Model
No persistence changes. Only one new in-memory contract:

| Type | Layer | Purpose |
|---|---|---|
| `IFailedJobCounter` | Application / Services | Abstraction returning the failed-job count |
| `HangfireFailedJobCounter` | API / Infrastructure / Hangfire | Hangfire-backed implementation |

## API / Interface Design

### New interface
```csharp
// backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IFailedJobCounter.cs
namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

public interface IFailedJobCounter
{
    Task<long> GetFailedCountAsync(CancellationToken cancellationToken = default);
}
```

### DI registration
```csharp
// In AddHangfireServices, alongside the existing adapter registrations (~line 345):
services.AddScoped<IFailedJobCounter, HangfireFailedJobCounter>();
```

### Tile constructor (after change)
```csharp
public FailedJobsTile(IFailedJobCounter failedJobCounter, ILogger<FailedJobsTile> logger)
```

### No external HTTP/REST API surface changes
The tile's `LoadDataAsync` returns the same anonymous-object JSON envelope as today.

## Dependencies
- **Hangfire** — still required at the API layer for the implementation. No new Hangfire features are used (`JobStorage.GetMonitoringApi().FailedCount()` is the same call as today).
- **Existing `BackgroundJobsModule`** — `RegisterTile<FailedJobsTile>()` continues to register the tile in the Application module; only the tile's dependencies change.
- **Existing `AddHangfireServices`** — the only place modified in the API project.
- No new NuGet packages.

## Out of Scope
- **`HangfireJobRegistrationHelper`** — has the same layering violation but is tracked as a separate finding. Not refactored here.
- **`metadata.source = "Hangfire"` literal** — the tile still returns the literal string `"Hangfire"` in its metadata. Abstracting that label into the counter or moving it to the infra layer is intentionally not done; it is informational JSON for the dashboard, not a leak of types.
- **Removing the `Hangfire` package reference from `Anela.Heblo.Application.csproj`** — depends on the unrelated `HangfireJobRegistrationHelper` cleanup. Don't attempt it as part of this change.
- **Changing tile metadata, UI, or drill-down URL** (`/hangfire/jobs/failed`).
- **Adding caching or rate-limiting** to the counter call.
- **Option 2 from the brief** (relocating `FailedJobsTile` to `Anela.Heblo.API/Infrastructure/Hangfire/`) — rejected in favor of Option 1, because the tile registration helper `RegisterTile<T>` is invoked from the Application module, and keeping the tile class in Application preserves that module's ownership of its dashboard tiles. (Assumption — see Open Questions.)

## Open Questions
None.

## Status: COMPLETE