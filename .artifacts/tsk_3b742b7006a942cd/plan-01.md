# Plan: Remove hidden `DateTime.UtcNow` from `RecurringJobConfiguration`

## Summary
`RecurringJobConfiguration` (the BackgroundJobs domain entity) currently stamps `LastModifiedAt = DateTime.UtcNow` inside its constructor and four mutation methods (`Enable`, `Disable`, `UpdateCronExpression`, `UpdateConfiguration`). This is a hidden clock dependency in an otherwise pure domain entity, inconsistent with the module's established `TimeProvider` pattern already used by `GetRecurringJobHandler` and `GetRecurringJobsListHandler`. It forces at least one test (`UpdateRecurringJobStatusHandlerTests`) to assert timestamps with a 5-second tolerance instead of an exact value. This change makes the entity a pure function of its inputs by having callers supply the timestamp explicitly.

## Context
The BackgroundJobs module already decided that time is injected via `TimeProvider`, not read from the system clock inline — both read-side handlers (`GetRecurringJobHandler`, `GetRecurringJobsListHandler`) depend on it to compute `NextRunAt`. The entity is the only remaining piece of the module that reaches for the system clock directly. `TimeProvider` is already registered as a DI singleton (`services.AddSingleton(TimeProvider.System)` in `ServiceCollectionExtensions.cs`), so no new infrastructure is needed — this is purely a refactor of the entity's method signatures and its two write-side callers.

## Functional requirements

**FR-1: Entity mutation methods accept an explicit timestamp.**
`Enable`, `Disable`, `UpdateCronExpression`, and `UpdateConfiguration` each gain a `DateTime modifiedAt` parameter and use it to set `LastModifiedAt` instead of calling `DateTime.UtcNow` internally.
- Acceptance: calling `job.Enable("admin", fixedTimestamp)` sets `job.LastModifiedAt == fixedTimestamp` exactly, regardless of wall-clock time when the test runs.
- Acceptance: no `DateTime.UtcNow` reference remains in `Enable`, `Disable`, `UpdateCronExpression`, or `UpdateConfiguration`.

**FR-2: Constructor accepts an explicit timestamp.**
The public constructor gains a `DateTime lastModifiedAt` parameter (placed alongside the existing `lastModifiedBy` parameter) and uses it instead of `DateTime.UtcNow`.
- Acceptance: `new RecurringJobConfiguration(..., lastModifiedAt: fixedTimestamp, lastModifiedBy: "system")` sets `LastModifiedAt == fixedTimestamp` exactly.
- Acceptance: the private EF Core constructor is untouched (no clock dependency there today).

**FR-3: Write-side handlers own the clock via `TimeProvider`.**
`UpdateRecurringJobStatusHandler` and `UpdateRecurringJobCronHandler` inject `TimeProvider` (constructor parameter, same pattern as the two read handlers) and pass `_timeProvider.GetUtcNow().UtcDateTime` into `Enable`/`Disable`/`UpdateCronExpression`.
- Acceptance: both handlers compile with `TimeProvider` as a constructor dependency; DI resolves it from the existing `TimeProvider.System` singleton registration — no new registration needed.
- Acceptance: `UpdateRecurringJobStatusHandlerTests` and `UpdateRecurringJobCronHandlerTests` can inject a fake/fixed `TimeProvider` and assert `result.LastModifiedAt` equals the fixed value exactly (replacing the `BeCloseTo(..., TimeSpan.FromSeconds(5))` tolerance in `UpdateRecurringJobStatusHandlerTests.cs:76`).

**FR-4: `RecurringJobSeeder` passes `DateTime.UtcNow` directly.**
`RecurringJobSeeder.SeedDefaultConfigurationsAsync` (startup-only code, not under precision-sensitive test) calls the new constructor and `UpdateConfiguration` overload, both times passing `DateTime.UtcNow` inline. No `TimeProvider` injection needed here per the finding's own guidance.
- Acceptance: seeder builds and its existing tests (`RecurringJobSeederTests.cs`) pass unchanged in behavior (timestamps still wall-clock, just passed as an argument instead of set internally).

**FR-5: All call sites updated, no behavior change outside timestamp precision.**
Every call site of the constructor and the four mutation methods (production and test code) is updated to pass an explicit timestamp. No other behavior changes.
- Acceptance: `dotnet build` succeeds with no call-site left on the old signatures.
- Acceptance: all existing BackgroundJobs tests pass, with the one exception of the tolerance-based assertion in FR-3, which is tightened to an exact match.

## Non-functional requirements
- **No behavior change**: entity validation rules (`ValidationException` for blank fields) are unchanged; only the source of `LastModifiedAt` moves from inside the entity to the caller.
- **No new DI registrations**: `TimeProvider.System` is already registered app-wide; only two additional constructor parameters are added to existing handlers.
- **Test determinism**: tests that previously used `BeCloseTo(..., TimeSpan.FromSeconds(5))` for `LastModifiedAt` should be tightened to exact equality once a fixed/fake `TimeProvider` is available, since that's the concrete benefit this change is meant to unlock.

## Data model
No schema or persisted-shape change. `RecurringJobConfiguration.LastModifiedAt` remains a `DateTime` column; only how it gets populated changes (caller-supplied vs. entity-internal `UtcNow`). No new entities or relations.

## Interfaces
- **Entity API change** (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`):
  - `RecurringJobConfiguration(string jobName, string displayName, string description, string cronExpression, string timeZoneId, bool isEnabled, string lastModifiedBy, DateTime lastModifiedAt)`
  - `void Enable(string modifiedBy, DateTime modifiedAt)`
  - `void Disable(string modifiedBy, DateTime modifiedAt)`
  - `void UpdateCronExpression(string cronExpression, string modifiedBy, DateTime modifiedAt)`
  - `void UpdateConfiguration(string displayName, string description, string cronExpression, string timeZoneId, string modifiedBy, DateTime modifiedAt)`
- **Handler constructor changes**: `UpdateRecurringJobStatusHandler` and `UpdateRecurringJobCronHandler` each add a `TimeProvider timeProvider` constructor parameter (mirroring `GetRecurringJobHandler`'s existing pattern) and a `private readonly TimeProvider _timeProvider` field.
- No HTTP/API contract changes — this is entirely internal to the domain/application layers; no MVC controller or DTO is touched.

## Dependencies and scope
- **Depends on**: existing `TimeProvider` DI registration (already present, no action needed).
- **In scope**: `RecurringJobConfiguration.cs`, `RecurringJobSeeder.cs`, `UpdateRecurringJobStatusHandler.cs`, `UpdateRecurringJobCronHandler.cs`, and every test file that constructs `RecurringJobConfiguration` or calls its mutation methods (`RecurringJobConfigurationTests.cs`, `RecurringJobStatusCheckerTests.cs`, `UpdateRecurringJobCronHandlerTests.cs`, `UpdateRecurringJobStatusHandlerTests.cs`, `RecurringJobDiscoveryServiceTests.cs`, `RecurringJobSeederTests.cs`, `GetRecurringJobHandlerTests.cs`, `RecurringJobConfigurationRepositoryTests.cs`, `GetRecurringJobsListHandlerTests.cs`).
- **Out of scope**: `RecurringJobsControllerTests.cs` (constructs DTOs directly with `LastModifiedAt = DateTime.UtcNow`, never touches the entity — unaffected). No changes to `GetRecurringJobHandler`/`GetRecurringJobsListHandler` (already correct). No changes to the `IRecurringJobConfigurationRepository` contract. No changes to cron scheduling logic, job discovery, or the controller/API surface.

## Rough plan
1. Change `RecurringJobConfiguration`: add `DateTime lastModifiedAt`/`modifiedAt` parameters to the constructor and the four mutation methods; remove all internal `DateTime.UtcNow` calls.
2. Update `RecurringJobSeeder` to pass `DateTime.UtcNow` inline at both call sites (construction and `UpdateConfiguration`).
3. Inject `TimeProvider` into `UpdateRecurringJobStatusHandler` and `UpdateRecurringJobCronHandler`; replace the implicit clock with `_timeProvider.GetUtcNow().UtcDateTime` passed into `Enable`/`Disable`/`UpdateCronExpression`.
4. Update all test call sites to pass an explicit timestamp (a fixed constant, e.g. `DateTime.UtcNow` captured once per test or a literal `DateTime`, is fine for constructor/entity-level tests that don't test the clock itself).
5. In `UpdateRecurringJobStatusHandlerTests.cs` and `UpdateRecurringJobCronHandlerTests.cs`, inject a fake/fixed `TimeProvider` (e.g. `Microsoft.Extensions.Time.Testing.FakeTimeProvider` if already used elsewhere in the codebase, or a simple test double) and tighten the `LastModifiedAt` assertion in `UpdateRecurringJobStatusHandlerTests.cs:76` from `BeCloseTo(..., TimeSpan.FromSeconds(5))` to an exact match.
6. Run `dotnet build` and the full `Anela.Heblo.Tests` BackgroundJobs suite; fix any remaining compile errors from missed call sites.
7. `dotnet format` before finishing.

## Open questions
- **Test double for `TimeProvider`**: the codebase doesn't show a shared `FakeTimeProvider` helper in the grep results — confirming whether one already exists (e.g. `Microsoft.Extensions.Time.Testing`) or whether the dev step should add a minimal one is left to the architecture/dev step. Default: use `Microsoft.Extensions.Time.Testing.FakeTimeProvider` (a standard BCL-adjacent NuGet package) if not already referenced; otherwise a trivial `TimeProvider` subclass returning a fixed `DateTimeOffset`.
- **Entity-level unit tests** (`RecurringJobConfigurationTests.cs`) don't need a fake `TimeProvider` at all — they can pass a literal `DateTime` constant directly into the constructor/methods, since the entity itself no longer owns the clock. Only the two handler test files need a controllable `TimeProvider`.
- **Parameter placement**: the finding's example shows `Enable(string modifiedBy, DateTime modifiedAt)` — kept `modifiedBy` first for method calls (matches existing argument order) and put `lastModifiedAt` after `lastModifiedBy` in the constructor to match the entity's declared property order (`LastModifiedAt` before `LastModifiedBy`) as closely as possible while minimizing existing named-argument churn. This is a judgment call for the dev step; not architecturally significant either way.
