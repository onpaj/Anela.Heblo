```markdown
# Architecture Review: Decouple `FailedJobsTile` from Hangfire Infrastructure

## Skip Design: true

Backend-only refactor. No UI, no JSON contract change, no new endpoints. The tile keeps the same anonymous-object payload shape, the same drill-down URL, the same metadata — only the construction dependency moves behind an Application-owned interface.

## Architectural Fit Assessment

The spec aligns exactly with the established pattern already proven twice in this module: `IHangfireJobEnqueuer` (interface in `Application/Features/BackgroundJobs/Services/`, implementation in `API/Infrastructure/Hangfire/`, DI in `AddHangfireServices`) and `IHangfireRecurringJobScheduler` (same shape). `BackgroundJobsModule.cs:14-16` documents this convention explicitly:

> "Hangfire adapter implementations […] are registered in `Anela.Heblo.API.Extensions.ServiceCollectionExtensions.AddHangfireServices` because their implementations live in the API project (Clean Architecture dependency rule)."

`FailedJobsTile` is the odd one out — added after the relocation refactor and never reconciled. Introducing `IFailedJobCounter` simply closes that gap.

**Integration points (all already present):**
- `BackgroundJobsModule.AddBackgroundJobsModule` → calls `RegisterTile<FailedJobsTile>()`. Unchanged.
- `Anela.Heblo.API.Extensions.ServiceCollectionExtensions.AddHangfireServices` (ServiceCollectionExtensions.cs:342–346) → the existing adapter-registration block. One new line.
- `Anela.Heblo.Xcc.Services.Dashboard.ITile` contract → unchanged.

**One caveat:** removing Hangfire from `Anela.Heblo.Application.csproj` is **not possible** in this change. Six other Application files still `using Hangfire;` (`HangfireJobRegistrationHelper`, `DashboardModule`, `GenerateArticleHandler`, `GenerateArticleJob`, `PlaudPollingJob`, `ProductExportDownloadJob`). The spec correctly scopes this out, but FR-3's acceptance criterion *"`Anela.Heblo.Application.csproj` does not gain (and ideally loses, if no other file in Application needs it) a `<PackageReference Include="Hangfire.*">`"* should be tightened to "does not gain" — losing it is not achievable here. See **Specification Amendments**.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│  Anela.Heblo.Application                                            │
│                                                                     │
│  Features/BackgroundJobs/                                           │
│    DashboardTiles/                                                  │
│      FailedJobsTile  ──depends on──►  IFailedJobCounter             │
│                                       (NEW, Services/)              │
│                                                                     │
│  (No `using Hangfire;` in either of these files after the change.)  │
└─────────────────────────────────────────────────────────────────────┘
                                              ▲
                                              │ implements
                                              │
┌─────────────────────────────────────────────│───────────────────────┐
│  Anela.Heblo.API                            │                       │
│                                             │                       │
│  Infrastructure/Hangfire/                   │                       │
│    HangfireFailedJobCounter (NEW)  ─────────┘                       │
│      └── ctor(JobStorage)                                           │
│      └── _jobStorage.GetMonitoringApi().FailedCount()               │
│                                                                     │
│  Extensions/ServiceCollectionExtensions.cs                          │
│    AddHangfireServices()                                            │
│      services.AddScoped<IFailedJobCounter, HangfireFailedJobCounter>│
└─────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Mirror the `IHangfireJobEnqueuer` pattern, not Option 2 (move the tile)

**Options considered:**
- (a) **Thin interface in Application, implementation in API/Infrastructure** (spec's choice).
- (b) **Relocate `FailedJobsTile` to `API/Infrastructure/Hangfire/`** (brief's Option 2).

**Chosen approach:** (a).

**Rationale:** Dashboard tile *registration* (`RegisterTile<T>`) lives in `BackgroundJobsModule.AddBackgroundJobsModule`, which is an Application-layer concern (module composition). Moving the tile out of the Application project would mean either (i) the API project starts registering its own tiles — inconsistent with how every other tile in the codebase is wired — or (ii) the Application module registers a tile defined in the API project, which inverts the dependency. (a) keeps the module's ownership of *what it shows* intact while pushing only the *how it queries Hangfire* into infrastructure. This is the same trade-off already made for `IHangfireJobEnqueuer`.

#### Decision 2: Return shape — `Task<long>`, not `ValueTask<long>` or sync `long`

**Options considered:**
- (a) `Task<long> GetFailedCountAsync(CancellationToken)` — spec.
- (b) `long GetFailedCount()` synchronous (Hangfire's call is sync anyway).
- (c) `ValueTask<long>` to avoid Task allocation.

**Chosen approach:** (a).

**Rationale:** `ITile.LoadDataAsync` is already async; the tile awaits the result regardless. `Task<long>` matches the async-with-`CancellationToken` convention used everywhere in this codebase (csharp-coding-style: "Pass `CancellationToken` through public async APIs"). It also future-proofs the interface for any future implementation that genuinely needs to do I/O (e.g., querying a remote dashboard service). The `Task.FromResult` allocation is irrelevant at dashboard-refresh frequency.

#### Decision 3: DI lifetime — Scoped, matching `IHangfireJobEnqueuer`

**Options considered:**
- (a) `Scoped` — spec's choice, matches `IHangfireJobEnqueuer`.
- (b) `Singleton` — would be technically purer since `HangfireFailedJobCounter` is stateless and `JobStorage` is itself a singleton.

**Chosen approach:** (a) `Scoped`.

**Rationale:** Consistency with the sibling adapter dominates a micro-optimization. `RegisterTile<FailedJobsTile>` itself registers the tile as scoped (the `ITileRegistry` extension chooses lifetime), so consumers will create one per resolution anyway. Singleton would not deliver any observable benefit here, and Scoped removes a foot-gun if the implementation ever grows a scoped dependency (e.g., a request-scoped logger context).

#### Decision 4: Where to keep the `try`/`catch` — in the tile, not the counter

**Options considered:**
- (a) Tile catches, counter rethrows (spec).
- (b) Counter catches and returns `0` (or some sentinel) on failure.

**Chosen approach:** (a).

**Rationale:** The tile owns the *presentation* concern of error envelopes (`status = "error"`). The counter's job is to answer "how many failed?" — silently returning `0` on a Hangfire outage would falsely report green on a broken background-job system. Letting exceptions surface to the tile preserves the existing observable behavior (logged error + error envelope) and keeps the abstraction honest. The spec captures this in FR-2 ("does not swallow exceptions") and FR-7 (explicit test that exceptions propagate).

#### Decision 5: `sealed` class for the adapter

`HangfireJobEnqueuer` is not sealed; `HangfireFailedJobCounter` per spec will be `sealed`. This is fine — the project rules prefer sealing types that aren't designed for inheritance, and there is no reason a counter adapter would ever be subclassed. The inconsistency with `HangfireJobEnqueuer` is acceptable (the older class predates the convention; not in scope to fix here).

## Implementation Guidance

### Directory / Module Structure

New files:

```
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/
  └── IFailedJobCounter.cs                                    (NEW)

backend/src/Anela.Heblo.API/Infrastructure/Hangfire/
  └── HangfireFailedJobCounter.cs                             (NEW)

backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/
  └── HangfireFailedJobCounterTests.cs                        (NEW — see amendment below)
```

Modified files:

```
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/
  └── FailedJobsTile.cs                                       (constructor + async)

backend/src/Anela.Heblo.API/Extensions/
  └── ServiceCollectionExtensions.cs                          (one new DI line)

backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/
  └── FailedJobsTileTests.cs                                  (rewritten mocks)
```

### Interfaces and Contracts

**Application contract:**
```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

public interface IFailedJobCounter
{
    Task<long> GetFailedCountAsync(CancellationToken cancellationToken = default);
}
```

**Infrastructure adapter (signature only):**
```csharp
namespace Anela.Heblo.API.Infrastructure.Hangfire;

public sealed class HangfireFailedJobCounter : IFailedJobCounter
{
    public HangfireFailedJobCounter(JobStorage jobStorage);
    public Task<long> GetFailedCountAsync(CancellationToken cancellationToken = default);
}
```

**Tile constructor (after change):**
```csharp
public FailedJobsTile(IFailedJobCounter failedJobCounter, ILogger<FailedJobsTile> logger);
```

**DI registration (one new line at ServiceCollectionExtensions.cs:~346, in the existing adapter block):**
```csharp
services.AddScoped<IFailedJobCounter, HangfireFailedJobCounter>();
```

**No HTTP, OpenAPI, or front-end client changes.** The tile's JSON envelope is byte-identical.

### Data Flow

```
Dashboard refresh tick
  └─► ITileRegistry resolves FailedJobsTile (scoped)
        └─► DI injects IFailedJobCounter ──► HangfireFailedJobCounter (scoped)
                                              └─► JobStorage (singleton, registered by Hangfire)
        └─► FailedJobsTile.LoadDataAsync(ct)
              ├─ try
              │   └─ await _failedJobCounter.GetFailedCountAsync(ct)
              │       └─ jobStorage.GetMonitoringApi().FailedCount()  // sync, in-memory
              │   └─ return { status: "success", data: { count }, metadata, drillDown }
              └─ catch
                  └─ logger.LogError(...)
                  └─ return { status: "error", data: null, error, drillDown }
```

Single virtual call added vs. today. No new I/O, no allocation hotspot, no caching needed.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Developer also tries to remove `Hangfire.Core` from `Application.csproj`, which will break six other Application files. | Medium | Explicitly call out in the spec amendment that the package reference stays. Add a comment in `FailedJobsTile.cs` if helpful, or rely on the spec's Out-of-Scope section. |
| Test file placement diverges from existing convention (spec proposes `test/Infrastructure/Hangfire/`, but the existing sibling `HangfireJobEnqueuerTests.cs` lives in `test/Features/BackgroundJobs/`). | Low | Place `HangfireFailedJobCounterTests.cs` next to `HangfireJobEnqueuerTests.cs` under `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/`. See **Specification Amendments**. |
| Mocking `JobStorage` with Moq relies on virtual members — same fragility the refactor is trying to escape. | Low | The unit test for `HangfireFailedJobCounter` is the *one place* where mocking Hangfire is acceptable (it is the seam); tests elsewhere become Hangfire-free. Accept the trade. |
| `LoadDataAsync` switched from `Task<object>` (sync body) to `async Task<object>`. If any caller awaits the tile synchronously by relying on `Task.FromResult` being completed, behavior changes subtly. | Low | `ITile.LoadDataAsync` is invoked through the dashboard pipeline which already awaits; this is not a public API. No mitigation needed beyond the existing tests. |
| `CancellationToken` is now actually propagated to the counter (previously ignored). If Hangfire's `FailedCount()` were ever to hang, cancellation still won't interrupt it (synchronous call). | Low | Document in the adapter (one-line comment) that the token is accepted for interface conformance but Hangfire's call is synchronous. No mitigation in code. |

## Specification Amendments

1. **FR-3 acceptance criterion 2** — drop the "(and ideally loses)" clause. The Hangfire package reference must stay in `Anela.Heblo.Application.csproj` because six other Application files still use Hangfire. Rewrite as: *"`Anela.Heblo.Application.csproj` does not gain a new `<PackageReference Include="Hangfire.*">`. The existing `Hangfire.Core 1.8.21` reference is retained — removing it depends on the separately-tracked `HangfireJobRegistrationHelper` cleanup."*

2. **FR-7 test file location** — change from `backend/test/Anela.Heblo.Tests/Infrastructure/Hangfire/HangfireFailedJobCounterTests.cs` to `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireFailedJobCounterTests.cs`. Rationale: that is where `HangfireJobEnqueuerTests.cs` and `HangfireRecurringJobSchedulerTests.cs` already live. The repo does **not** use a top-level `Infrastructure/Hangfire/` test folder — its `Infrastructure/` folder is currently for `Authentication/` only. Following the existing per-module test layout is the right call.

3. **FR-6 test rename** — resolve the open phrasing in the spec. The test currently named `LoadDataAsync_MonitoringApiThrows_ReturnsErrorAndDoesNotPropagate` should be renamed to `LoadDataAsync_CounterThrows_ReturnsErrorAndDoesNotPropagate`. The test no longer knows anything about `IMonitoringApi`; keeping the old name would re-couple the test to the abstraction it just escaped.

4. **FR-2 — comment hint** — add a single-line comment in `HangfireFailedJobCounter.GetFailedCountAsync` next to the unused token parameter, e.g. `// Hangfire's FailedCount() is synchronous; token accepted for interface conformance.` This earns its keep because a future reader will otherwise assume the token is plumbed and look for the propagation path.

5. **Spec's "Status: COMPLETE"** — flip to "AMENDED" once items 1–3 above are folded in. (Cosmetic; not blocking.)

## Prerequisites

None. All required infrastructure exists:
- `JobStorage` is already registered as a singleton by Hangfire's own bootstrap inside `AddHangfireServices` (ServiceCollectionExtensions.cs:~314–328).
- `AddHangfireServices` already runs unconditionally for the API project.
- `ITileRegistry` / `RegisterTile<T>` extension is in place and used.
- No database migrations, no configuration changes, no Key Vault entries.

Implementation can start immediately.
```