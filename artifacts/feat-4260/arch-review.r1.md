# Architecture Review: Complete the `ICronScheduler.UpdateCronSchedule` contract with `timeZoneId`

## Skip Design: true

Backend-only refactor. No new or changed UI components, no screens, no visual decisions. The Background Jobs screen and the generated TypeScript client are untouched because no HTTP contract changes.

## Architectural Fit Assessment

The change fits the existing architecture cleanly and is, in fact, a correction *toward* the documented pattern rather than a deviation from it.

**Verified facts (read from the worktree, not the spec):**

- `ICronScheduler` (`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs`) is a consumer-owned port: declared in the Application slice that consumes it, implemented by an adapter in `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/`, and bound in `AddHangfireServices` (`ServiceCollectionExtensions.cs:376`, `AddSingleton<ICronScheduler, HangfireRecurringJobScheduler>`). This is exactly the "consumer defines the contract, provider implements the adapter, provider registers the binding" rule in `docs/architecture/development_guidelines.md` (Cross-Module Communication section), and matches its siblings `IJobEnqueuer` and `IFailedJobCounter` in the same folder.
- The port is currently **incomplete**: `HangfireRecurringJobScheduler.UpdateCronSchedule` (lines 26–61) opens a DI scope, enumerates `IRecurringJob`, and reads `job.Metadata.TimeZoneId` — a value the sole caller already holds. Adding `timeZoneId` to the port closes that gap. Nothing about the layering, folder placement, or DI wiring needs to move.
- **Single call site, single implementation, single mock.** A repo-wide grep confirms production call sites are exactly: `UpdateRecurringJobCronHandler.cs:74`, plus the adapter itself and the two test files. The only other hits are historical plan docs under `docs/superpowers/plans/` and prior `artifacts/` review documents. Blast radius is genuinely contained.
- `HangfireJobRegistrationHelper.RegisterOrUpdate(Type jobType, string jobName, string cronExpression, string timeZoneId)` (verified, lines 22–85) does require a runtime `Type`, which it closes over `RegisterOrUpdateGeneric<TJob>` by reflection. **The spec's correction to the brief is right**: the DI scope cannot be deleted by this change.
- **Time-zone equivalence verified end-to-end.** `Program.cs:177` calls `SeedRecurringJobConfigurationsAsync()` *after* `app.Build()` but *before* `app.Run()`, i.e. before `RecurringJobDiscoveryService` (a hosted service, `ServiceCollectionExtensions.cs:412`) starts. `RecurringJobSeeder.SeedDefaultConfigurationsAsync` passes `config.TimeZoneId` (from `job.Metadata.TimeZoneId`) into `existing.UpdateConfiguration(...)` on every startup for existing rows, so the DB row's `TimeZoneId` is re-synced from code before discovery registers anything. `RecurringJobConfiguration` rejects null/whitespace `TimeZoneId` in both the constructor and `UpdateConfiguration`. Passing `job.TimeZoneId` is therefore behaviour-preserving, not merely "probably equivalent".

**Integration points:** `UpdateRecurringJobCronHandler` (Application) → `ICronScheduler` (port) → `HangfireRecurringJobScheduler` (adapter) → `HangfireJobRegistrationHelper` → Hangfire storage. Only the port signature, the one call argument, and the adapter body change.

**One factual correction to the spec** (detail below in Specification Amendments): NFR-3's criterion "`Anela.Heblo.Application` gains no reference to Hangfire" is already false at the project level — `Anela.Heblo.Application.csproj:12` carries `<PackageReference Include="Hangfire.Core" Version="1.8.21" />`, and 13 Application files already `using Hangfire`. The constraint that actually matters is narrower and must be restated.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Application  (Features/BackgroundJobs)
┌──────────────────────────────────────────────────────────────────┐
│ UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler    │
│   deps: IRecurringJobConfigurationRepository, ICurrentUserService│
│         ICronScheduler, TimeProvider, ILogger                    │
│                                                                  │
│   holds RecurringJobConfiguration ──► JobName                    │
│                                       CronExpression             │
│                                       TimeZoneId  ◄── NEW source │
│                                                                  │
│ Services/ICronScheduler        (PORT — widened)                  │
│   void UpdateCronSchedule(jobName, cronExpression, timeZoneId)   │
└───────────────────────────────┬──────────────────────────────────┘
                                │ (DI, AddSingleton)
Anela.Heblo.API/Infrastructure/Hangfire
┌───────────────────────────────▼──────────────────────────────────┐
│ HangfireRecurringJobScheduler : ICronScheduler   (ADAPTER)       │
│   deps: IServiceProvider  ← retained, TYPE RESOLUTION ONLY       │
│         ILogger                                                  │
│                                                                  │
│   1. guard jobName, cronExpression, timeZoneId                   │
│   2. scope → GetServices<IRecurringJob>() → jobName → Type       │
│      (metadata.TimeZoneId NO LONGER READ)                        │
│   3. HangfireJobRegistrationHelper.RegisterOrUpdate(             │
│           type, jobName, cron, timeZoneId)   ← caller's value    │
└───────────────────────────────┬──────────────────────────────────┘
                                ▼
                   Hangfire storage (RecurringJob record)
                                ▲
                                │ same helper, startup path
        RecurringJobDiscoveryService (IHostedService)
          cron ← DB row, timeZoneId ← metadata   (unchanged)
```

The pre-change diagram differs in exactly one edge: today the adapter has a second inbound data edge from `IRecurringJob.Metadata` carrying `TimeZoneId`. After the change that edge is gone and the DI edge carries only `Type`.

### Key Design Decisions

#### Decision 1: Widen the port with a positional parameter, not a parameter object

**Options considered:**
- (A) Three positional `string` parameters: `UpdateCronSchedule(jobName, cronExpression, timeZoneId)`.
- (B) A parameter object, e.g. `UpdateCronSchedule(CronScheduleUpdate update)` with a small Application-owned type.
- (C) Pass the whole `RecurringJobConfiguration` entity to the port.

**Chosen approach:** (A), with `timeZoneId` last.

**Rationale:** (C) is wrong outright — it drags a Domain entity across a port whose job is to describe *one infrastructure operation*, and it would let the adapter reach for fields (`IsEnabled`, `LastModifiedBy`) that are none of its business. (B) buys nothing at one call site and one implementation; the sibling ports `IJobEnqueuer` and `IFailedJobCounter` are both bare-parameter interfaces, and introducing a DTO here would be the odd one out. Choose (A) and put `timeZoneId` last so `ICronScheduler.UpdateCronSchedule(name, cron, tz)` reads identically to `HangfireJobRegistrationHelper.RegisterOrUpdate(type, name, cron, tz)` — three adjacent `string` parameters are a swap hazard, and making the two signatures align in the same order is the cheapest real mitigation.

#### Decision 2: Keep `IServiceProvider` in the adapter, scoped strictly to `jobName → Type`

**Options considered:**
- (A) Keep the DI scope solely for type resolution (spec's FR-3).
- (B) Remove `IServiceProvider` and inject `IEnumerable<IRecurringJob>` directly.
- (C) Introduce an `IRecurringJobTypeRegistry` singleton built once at startup, inject that, delete `IServiceProvider`.

**Chosen approach:** (A) for this change. (C) is the right end state, as a separate issue.

**Rationale:** (B) is not viable and it is worth recording why, so nobody "simplifies" it later: `HangfireRecurringJobScheduler` is registered `AddSingleton` while `IRecurringJob` implementations are registered `AddScoped` (see `HangfireRecurringJobSchedulerTests` fixture and the production job registrations). Injecting `IEnumerable<IRecurringJob>` into a singleton is a captive-dependency bug. The existing `CreateScope()` is the *correct* pattern for a singleton consuming scoped services, not an accident.

(C) is genuinely better but carries a non-obvious cost that this change should not absorb: `RecurringJobMetadata.JobName` is an **instance** property on `IRecurringJob` (verified — `required string JobName { get; init; }`, no attribute, no static), so a `jobName → Type` map cannot be built from `ServiceDescriptor.ImplementationType` alone. Building it requires instantiating every job once inside a startup scope. That is a legitimate design (`RecurringJobDiscoveryService` already does exactly this enumeration at startup and could publish the map as a by-product), but it adds a new abstraction, new DI wiring, new lifetime reasoning and new tests — all unrelated to the DIP violation this issue is about. Fixing the port and fixing the type-resolution mechanism are two separable improvements; conflating them makes the diff harder to review and the regression risk harder to bound.

**Binding requirement:** the retained `IServiceProvider` must carry a code comment naming precisely what it is still for. Without it, the next arch-review pass re-files this same finding.

#### Decision 3: The DB row is authoritative on the runtime path; the startup path is untouched

**Options considered:**
- (A) Runtime path uses the caller's DB value; startup path keeps reading metadata.
- (B) Make DB authoritative everywhere — change `RecurringJobDiscoveryService` to use `dbConfig.TimeZoneId`.
- (C) Keep metadata authoritative everywhere — have the adapter resolve the time zone from metadata (status quo).

**Chosen approach:** (A). Answers spec **Q3: yes, confirmed.**

**Rationale:** (C) is the bug being fixed. (B) is *architecturally tidier* — one source of truth for a field — but it is currently a distinction without a difference, because the startup ordering verified above guarantees `dbConfig.TimeZoneId == metadata.TimeZoneId` at the moment discovery runs. It also touches a component the spec correctly declares out of scope and risks a startup-path regression for zero observable gain. Take (A), and record in the adapter's XML doc that the caller owns the time zone, so that if `TimeZoneId` ever becomes admin-editable the follow-up work (flip discovery to the DB value, drop the seeder's re-sync of that field) is already identified.

#### Decision 4: `UpdateCronSchedule` stays `void` and stays fire-and-forget

**Options considered:**
- (A) Keep `void`; the unregistered-job case stays a warning log and HTTP 200.
- (B) Return a result (`enum`/small result type) so the handler can report "saved, applies after restart".
- (C) Throw when the job type is not registered.

**Chosen approach:** (A) for this change. Answers spec **Q2: confirmed out of scope**, with (B) as the recommended shape of the follow-up.

**Rationale:** (C) is actively harmful and must not be smuggled in. The handler's `try/catch` (`UpdateRecurringJobCronHandler.cs:88–101`) wraps the scheduler call, which sits **after** `await _repository.UpdateAsync(...)`. A throw would be converted into `RecurringJobUpdateFailed` *after the DB write has already committed* — the API would report failure for a persisted change. That is strictly worse than today's silent success. (B) is the correct eventual answer because it lets the handler distinguish "applied live" from "saved, applies on restart" without lying about the write, but it changes the response DTO and therefore the generated TypeScript client and the frontend's success handling — a separate issue with its own UI work. Improving the warning log's text (FR-3) is the right amount of change here.

## Implementation Guidance

### Directory / Module Structure

No new files, no moves, no new DI registrations. Four files change, all in place:

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/ICronScheduler.cs` | Add third parameter + XML docs |
| `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/UpdateRecurringJobCronHandler.cs` | Line 74 only |
| `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs` | Signature, guard, use passed tz, log text, `IServiceProvider` justification comment |
| `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/UpdateRecurringJobCronHandlerTests.cs` | Lines 61, 78, 125 |
| `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs` | Lines 42, 67, 107 + three new tests |

`ServiceCollectionExtensions.cs:376` stays exactly as is — lifetime does not change.

Do **not** create a `Contracts/` folder for this port. `docs/architecture/development_guidelines.md` names `Contracts/` for cross-*module* consumer contracts; `ICronScheduler` is an intra-module port over infrastructure and belongs where its siblings live, in `Features/BackgroundJobs/Services/`.

### Interfaces and Contracts

```csharp
// Anela.Heblo.Application.Features.BackgroundJobs.Services
public interface ICronScheduler
{
    void UpdateCronSchedule(string jobName, string cronExpression, string timeZoneId);
}
```

Contract rules the implementation and any future implementation must honour, and which the XML docs must state:

1. **Fire-and-forget.** Returns `void`. Scheduling failures (unknown job, unresolvable time zone, storage error) are logged by the implementation and never surfaced to the caller. The caller must not treat the call as a transactional step.
2. **Arguments are validated eagerly.** Null/empty/whitespace in any parameter throws `ArgumentException` (or a subclass) **before** any side effect, including before a DI scope is created. This is the one exception to rule 1.
3. **The caller owns the time zone.** The implementation must not re-derive it from any other source. `timeZoneId` must be resolvable by `TimeZoneInfo.FindSystemTimeZoneById` on the host.
4. **`jobName` is simultaneously the Hangfire recurring-job id.**
5. **No infrastructure types in the signature.** No `Type`, no Hangfire type, no `IServiceProvider`. If a future implementation needs a runtime type, it resolves it itself — that is adapter-internal.

Guard order in the adapter must follow parameter order (`jobName`, `cronExpression`, `timeZoneId`) so the failure a caller gets matches the argument they got wrong.

The adapter's retained `IServiceProvider` must carry a comment of roughly this substance, placed on the field or the `CreateScope()` call:

> Resolves the runtime `Type` for `jobName`, which `HangfireJobRegistrationHelper.RegisterOrUpdate` requires to close its generic `RecurringJob.AddOrUpdate<TJob>` overload. A scope is used (not a direct `IEnumerable<IRecurringJob>` injection) because this adapter is a singleton and `IRecurringJob` implementations are scoped. Job **metadata** is deliberately not read here — the schedule's time zone comes from the caller.

### Data Flow

**Runtime CRON edit (the path that changes):**

```
POST /api/recurring-jobs/{jobName}/cron
 → RecurringJobsController → MediatR
   → UpdateRecurringJobCronHandler
      1. NCrontab.Advanced validates cron        → InvalidCronExpression on failure
      2. repository.GetByJobNameAsync(jobName)   → RecurringJobNotFound on null
      3. job.UpdateCronExpression(cron, user, now)
      4. repository.UpdateAsync(job)             ── COMMIT POINT
      5. scheduler.UpdateCronSchedule(job.JobName, job.CronExpression, job.TimeZoneId)
         → HangfireRecurringJobScheduler
            a. guards (throw before any side effect)
            b. scope → GetServices<IRecurringJob>() → first by JobName → .GetType()
               ├─ not found → LogWarning, return          (DB row already saved)
               └─ found ─► RegisterOrUpdate(type, name, cron, timeZoneId)
                            ├─ throws → LogError(jobName, timeZoneId), return
                            └─ ok     → LogInformation(name, cron, tz)
      6. return 200 { JobName, CronExpression, LastModifiedAt, LastModifiedBy }
```

Step 5 is deliberately after the commit point and deliberately cannot fail the request. Any change to that ordering is out of scope and would need its own review.

**Startup (unchanged, shown for the invariant it maintains):**

```
app.Build()
 → SeedRecurringJobConfigurationsAsync()
      per job: existing.UpdateConfiguration(display, desc, existing.Cron, metadata.TimeZoneId, ...)
      ⇒ DB TimeZoneId := metadata TimeZoneId        ── the invariant Decision 3 relies on
 → app.Run() → RecurringJobDiscoveryService.StartAsync()
      per job: RegisterOrUpdate(type, name, dbConfig.Cron ?? metadata.Cron, metadata.TimeZoneId)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Three adjacent `string` parameters invite a `cron`/`timeZoneId` transposition at a future call site | Medium | Order `(jobName, cronExpression, timeZoneId)` to mirror `HangfireJobRegistrationHelper.RegisterOrUpdate`. Handler test asserts the **concrete** forwarded values, never `It.IsAny<string>()` on the happy path. A transposed cron also fails `RecurringJobConfiguration.ValidateCronFormat` upstream. |
| Silent no-op when the job type is missing from DI persists; HTTP 200 still returned | Medium | Accepted and explicitly scoped out (Decision 4). Mitigated only by the improved warning text, which must name the job and state that the DB row was saved and the schedule applies on next start. File the `202`-style follow-up issue at merge time so it is not lost. |
| Behavioural drift if DB and metadata `TimeZoneId` ever diverge | Low | Verified impossible today (seeder runs before discovery and re-syncs the field). Record the dependency in the adapter's docs so a future "admin-editable time zone" feature knows it must also change the discovery path. |
| `Assert.Throws<ArgumentException>` fails on the `null` case | Low but will break the build | `ArgumentException.ThrowIfNullOrWhiteSpace(null)` throws `ArgumentNullException`; xUnit's `Assert.Throws<T>` is exact-type. Use `Assert.ThrowsAny<ArgumentException>`. See amendments. |
| New time-zone test flakes across hosts | Low | `"UTC"` resolves on Linux containers and on Windows without ICU. Confirmed as the right choice (Q4). |
| Cross-test pollution in shared `JobStorage.Current` | Low | Already handled: `[CollectionDefinition("Hangfire", DisableParallelization = true)]` and the test class `Dispose()` removes every recurring job. New tests must not introduce their own storage configuration. |
| `dotnet format` churn on touched files | Low | Run `dotnet build` + `dotnet format` per the repo's completion checklist before declaring done. |

## Specification Amendments

The spec is accurate and well-grounded. Five amendments, one of which is a build-breaker.

**A-1 (must fix — NFR-3 acceptance criterion is factually wrong).**
"`Anela.Heblo.Application` gains no reference to Hangfire" cannot be satisfied or violated by this change: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj:12` already declares `<PackageReference Include="Hangfire.Core" Version="1.8.21" />`, and 13 Application files already `using Hangfire` (job classes under `Features/*/Infrastructure/Jobs/`, `HangfireMarketingPerformanceRecomputeEnqueuer`, two MindMaps handlers). Replace the criterion with the one that is actually checkable and actually matters:

> - `ICronScheduler.cs` contains no `using` directive beyond what it has today, and no Hangfire, ASP.NET Core, or `IServiceProvider` type appears in its signature.
> - No file under `Features/BackgroundJobs/` in `Anela.Heblo.Application` gains a Hangfire `using`.

**A-2 (must fix — the null case will not compile/pass as specified).**
FR-5's `UpdateCronSchedule_WithMissingTimeZoneId_ThrowsArgumentException` is a `[Theory]` over `null`, `""`, `"   "`. Two problems: (i) `ArgumentException.ThrowIfNullOrWhiteSpace(null)` throws `ArgumentNullException`, and xUnit's `Assert.Throws<ArgumentException>` matches the **exact** type, so the null row fails; (ii) the parameter is non-nullable `string`, so the theory parameter needs `string?` with the appropriate nullable handling. Specify `Assert.ThrowsAny<ArgumentException>(...)` (or split null into its own `[Fact]` asserting `ArgumentNullException`). Same applies to any equivalent guard test added for `jobName`/`cronExpression`.

**A-3 (clarification — FR-4's acceptance criteria become tautological).**
FR-4 requires that `UpdateCronSchedule_AfterDiscoveryRegistration_UpdatesCronInStorage` still asserts `Assert.Equal("Europe/Prague", job.TimeZoneId)` and that `UpdateCronSchedule_ProducesIdenticalRecordStructureToDiscoveryRegistration` still passes. Both will pass — but only because FR-5 has the tests pass `"Europe/Prague"` in as the third argument. After this change those two assertions no longer prove that the runtime path derives the same time zone as the startup path; they prove the adapter forwards what it was given. State this explicitly in FR-4 and name where the parity guarantee actually lives afterwards:
- `UpdateCronSchedule_UsesPassedTimeZone_NotJobMetadataTimeZone` (adapter forwards the argument, not metadata), **and**
- the strengthened `UpdateRecurringJobCronHandlerTests` happy-path verification (handler forwards the entity's value), **and**
- the `RecurringJobSeeder` invariant that keeps entity and metadata equal — which deserves one covering assertion. If `RecurringJobSeederTests` does not already assert that an existing row's `TimeZoneId` is re-synced from metadata, add that assertion; it is now the load-bearing link in the parity argument and nothing else guards it.

**A-4 (addition).** Add to FR-3's acceptance criteria: the log message emitted on success includes `{TimeZoneId}`. Today's success log (`HangfireRecurringJobScheduler.cs:58–60`) logs only job name and cron. Since the time zone's provenance is the whole point of this change, an operator debugging a mis-scheduled job should see the applied zone in the same line. The spec's illustrative body already does this; make it a criterion rather than an accident of the sample code.

**A-5 (scope note).** FR-6 concludes "no doc changes". Confirmed: a repo-wide grep finds the two-parameter signature only in `docs/superpowers/plans/2026-03-29-db-driven-cron-config.md` and `docs/superpowers/plans/2026-05-27-consolidate-hangfire-recurringjob-registration.md`, both point-in-time plan records, plus prior `artifacts/` review documents. Leave all of them alone. No `docs/architecture/*` or `docs/features/*` file documents this signature.

**Answers to the spec's open questions:**
- **Q1 — keep `IServiceProvider`.** Confirmed. The brief's suggestion is not achievable without introducing a type registry; see Decision 2 for why the registry is real work and why the metadata-is-instance-level constraint makes it non-trivial. File it as a follow-up issue; do not grow this scope.
- **Q2 — silent HTTP 200 stays.** Confirmed out of scope, and throwing instead is explicitly rejected (post-commit throw would report failure for a persisted write). The follow-up should take the return-value shape, not the exception shape.
- **Q3 — DB value is authoritative on the runtime path.** Confirmed, with the startup ordering verified above as the reason it is safe.
- **Q4 — use `"UTC"`.** Confirmed. CI runs Linux containers; `"UTC"` is also safe on a Windows dev box without ICU, unlike other IANA ids.

## Prerequisites

None. Specifically:

- **No EF Core migration** — no schema change; `RecurringJobConfiguration.TimeZoneId` already exists and is already populated and non-empty for every row.
- **No configuration, feature flag, or Key Vault secret.**
- **No OpenAPI / TypeScript client regeneration** — no controller, route, DTO or status code changes. The auto-generated client build step will produce an empty diff.
- **No new NuGet package** — `Microsoft.Extensions.DependencyInjection.Abstractions`, Hangfire and the test stack are all already referenced.
- **No DI or lifetime change** — `AddSingleton<ICronScheduler, HangfireRecurringJobScheduler>()` at `ServiceCollectionExtensions.cs:376` is unchanged.
- **No deployment sequencing concern** — the break is source-level and entirely within this solution; there is no out-of-process consumer of `ICronScheduler`.

Validation before declaring done, per the repo checklist: `dotnet build` + `dotnet format`, then `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~BackgroundJobs"`. No frontend build and no E2E run are required for this change.
