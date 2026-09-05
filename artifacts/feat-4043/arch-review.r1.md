# Architecture Review: RecurringJobSeeder uses TimeProvider instead of DateTime.UtcNow

## Skip Design: true

## Architectural Fit Assessment
This is a pure internal-consistency fix that aligns `RecurringJobSeeder` with an existing, already-proven module convention — it introduces no new pattern. Every MediatR handler in `Anela.Heblo.Application.Features.BackgroundJobs.UseCases.*` (verified in `UpdateRecurringJobCronHandler`, and the same pattern exists in `GetRecurringJobHandler`, `GetRecurringJobsListHandler`, `UpdateRecurringJobStatusHandler`) takes `TimeProvider` as a constructor dependency and computes `var now = _timeProvider.GetUtcNow().UtcDateTime;` once per `Handle` call. `RecurringJobSeeder` is a `Scoped` service in the same `Anela.Heblo.Application.Features.BackgroundJobs.Services` namespace, called once at startup from `ServiceCollectionExtensions.cs:478` inside a DI scope — the exact same resolution context handlers run in. `TimeProvider` is already registered application-wide as `services.AddSingleton(TimeProvider.System)` in `ServiceCollectionExtensions.cs:135`, so `RecurringJobSeeder` picking up a `TimeProvider` constructor parameter requires zero DI registration change — it is resolved automatically by the container the same way `IRecurringJobConfigurationRepository` already is.

No architectural boundary is crossed: `TimeProvider` is a BCL type with no Clean-Architecture layering concern (Application layer is already free to depend on it, as proven by every handler above).

## Proposed Architecture

### Component Overview
```
ServiceCollectionExtensions.cs (API layer, startup)
        │  scope.ServiceProvider.GetRequiredService<IRecurringJobSeeder>()
        ▼
RecurringJobSeeder : IRecurringJobSeeder      (Application layer, Services/)
   ctor(IRecurringJobConfigurationRepository, TimeProvider)   <- TimeProvider added here
        │
        │  var now = _timeProvider.GetUtcNow().UtcDateTime;   <- computed once per seed pass
        ▼
   new RecurringJobConfiguration(..., lastModifiedAt: now)     (create path)
   existing.UpdateConfiguration(..., modifiedAt: now)          (update path)
        │
        ▼
IRecurringJobConfigurationRepository (Persistence layer, unchanged)
```
No new components. One existing class gains one existing, already-registered dependency.

### Key Design Decisions

#### Decision 1: Reuse the framework `TimeProvider` singleton — no wrapper, no new abstraction
**Options considered:**
- (a) Inject the BCL `TimeProvider` directly, matching every handler in the module.
- (b) Introduce a custom `IClock`/`ISystemClock` wrapper.
- (c) Pass a `DateTime` parameter into `SeedDefaultConfigurationsAsync` from the caller instead of injecting a clock.

**Chosen approach:** (a) — inject `TimeProvider` via constructor, exactly as the issue's suggested fix specifies and as every sibling handler in this module already does.

**Rationale:** (b) would introduce a second time abstraction alongside `TimeProvider`, which is precisely the inconsistency this issue exists to eliminate — rejected. (c) would change the `IRecurringJobSeeder` public interface and its one call site in `ServiceCollectionExtensions.cs`, which the issue and spec explicitly scope out ("no logic change required", interface unchanged) — rejected. (a) is a two-line production diff, matches the module's established convention 1:1, and needs no new DI registration since `TimeProvider.System` is already a singleton in the container.

#### Decision 2: Compute `now` once per seeding pass, not once per job in the loop
**Options considered:**
- (a) Call `_timeProvider.GetUtcNow().UtcDateTime` once before the `Select`/`foreach`, matching how `UpdateRecurringJobCronHandler.Handle` computes `now` once at the top of its single-request handling.
- (b) Call it fresh at each of the two use sites (inside the `Select` projection, and again inside the `foreach` for the update branch).

**Chosen approach:** (a).

**Rationale:** All configurations touched within one seeding pass are semantically "modified at the same moment" (a single startup seeding operation) — a single `now` value keeps that invariant and matches the FR-2 acceptance criterion in `spec.r1.md`. It's also marginally cheaper and trivially easier to assert on in tests (one fixed value, not near-equal timestamps across iterations).

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Two files change:
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` — production change (constructor + 2 call sites).
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs` — test change (constructor call + new timestamp assertions).

No change needed to `BackgroundJobsModule.cs` (DI registration `services.AddScoped<IRecurringJobSeeder, RecurringJobSeeder>();` already resolves `TimeProvider` from the container automatically) or to `ServiceCollectionExtensions.cs:478` (caller uses `GetRequiredService<IRecurringJobSeeder>()`, unaffected by the seeder's own constructor dependencies).

### Interfaces and Contracts
- `IRecurringJobSeeder` — **unchanged**.
- `RecurringJobSeeder` constructor — new signature:
  ```csharp
  public RecurringJobSeeder(
      IRecurringJobConfigurationRepository repository,
      TimeProvider timeProvider)
  {
      _repository = repository;
      _timeProvider = timeProvider;
  }
  ```
  Note: existing handlers in this module (`UpdateRecurringJobCronHandler`) null-guard constructor params with `?? throw new ArgumentNullException(...)`. `RecurringJobSeeder`'s existing constructor does *not* currently null-guard `repository`. To keep this a surgical, scope-matching change (per `spec.r1.md` Out of Scope and CLAUDE.md's "surgical changes" rule), do **not** add null-guards to either parameter — match the existing unguarded style of this specific class rather than the guarded style of the handlers.
- `RecurringJobConfiguration` constructor and `UpdateConfiguration(...)` — **unchanged**; only the `DateTime` value passed as `lastModifiedAt`/`modifiedAt` changes source.

### Data Flow
1. `SeedDefaultConfigurationsAsync` computes `var now = _timeProvider.GetUtcNow().UtcDateTime;` once, before building `defaultConfigurations`.
2. The `Select(...)` projection uses `now` (was `DateTime.UtcNow`) as the `lastModifiedAt` argument to `new RecurringJobConfiguration(...)`.
3. Inside the `foreach` update branch, `existing.UpdateConfiguration(..., "System", now)` (was `DateTime.UtcNow`) uses the same `now`.
4. No change to persistence calls (`AddAsync`/`UpdateAsync`) or to the create-vs-update branching logic.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing test constructs `new RecurringJobSeeder(_repository)` with one arg — will fail to compile once the constructor changes | Low | Update the test's `RecurringJobSeeder` instantiation in the same commit as the production change; both files are in scope per `spec.r1.md`. |
| `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) availability | None | Already a `PackageReference` in `Anela.Heblo.Tests.csproj` (v8.1.0) and already used in this exact pattern by `SubmitManufactureHandlerTests` and 9+ other test files — no new package needed. |
| Computing `now` once vs. per-iteration could theoretically be seen as a behavior change | Low | Spec (FR-2) explicitly calls for single computation per pass; this matches the module's handler convention and is the more correct semantics for a single atomic seeding operation. |

## Specification Amendments
None. `spec.r1.md` is implementable as written; this review confirms `TimeProvider` is already DI-registered (no new registration step needed, superseding any implicit assumption otherwise) and confirms `Microsoft.Extensions.TimeProvider.Testing`'s `FakeTimeProvider` is already available in the test project, resolving the "Dependencies" section's open fallback question in `spec.r1.md` — use `FakeTimeProvider`, not a hand-rolled fake.

## Prerequisites
None. No migration, config, or infrastructure changes are required — `TimeProvider.System` is already registered as a singleton in the DI container (`ServiceCollectionExtensions.cs:135`), and `FakeTimeProvider` is already a test-project dependency.
