# Specification: RecurringJobSeeder uses TimeProvider instead of DateTime.UtcNow

## Summary
`RecurringJobSeeder.SeedDefaultConfigurationsAsync` currently calls `DateTime.UtcNow` directly in two places when creating or updating `RecurringJobConfiguration` audit timestamps. Every other class in the BackgroundJobs module (e.g. `UpdateRecurringJobCronHandler`) injects `TimeProvider` and calls `_timeProvider.GetUtcNow().UtcDateTime` instead. This spec covers bringing `RecurringJobSeeder` in line with that convention: inject `TimeProvider`, replace both `DateTime.UtcNow` call sites, and update its unit tests to use a fake/fixed `TimeProvider` so the audit timestamp can be asserted exactly.

## Background
`RecurringJobSeeder` runs at application startup (invoked from `ServiceCollectionExtensions.cs:478`) to seed/update `RecurringJobConfiguration` rows for every discovered `IRecurringJob`. It is registered as `Scoped` in `BackgroundJobsModule.cs`. It is the one remaining service in the BackgroundJobs module that reads wall-clock time via the static `DateTime.UtcNow` instead of the injected `TimeProvider` abstraction the rest of the module standardizes on. This makes exact-timestamp assertions in `RecurringJobSeederTests` impossible without mocking static time, and leaves two different "get current time" code paths in one feature. This is a pure internal-consistency/testability fix — no behavior visible to users or the DB schema changes.

## Functional Requirements

### FR-1: Inject TimeProvider into RecurringJobSeeder
`RecurringJobSeeder`'s constructor accepts a `TimeProvider` parameter (framework-provided abstraction, same type used by `UpdateRecurringJobCronHandler` and other module handlers) alongside the existing `IRecurringJobConfigurationRepository`, and stores it in a private readonly field (`_timeProvider`).

**Acceptance criteria:**
- Constructor signature is `RecurringJobSeeder(IRecurringJobConfigurationRepository repository, TimeProvider timeProvider)`.
- `TimeProvider` is stored in a `private readonly TimeProvider _timeProvider` field.
- No DI registration changes are needed beyond the constructor signature — `TimeProvider.System` is already registered/resolvable framework-wide (confirmed by existing handlers in the same module resolving it successfully today).

### FR-2: Replace both DateTime.UtcNow call sites with TimeProvider
Both current uses of `DateTime.UtcNow` in `SeedDefaultConfigurationsAsync` are replaced with a value obtained from the injected `TimeProvider`.

**Acceptance criteria:**
- Line ~34 (constructing a new `RecurringJobConfiguration`, `lastModifiedAt` parameter): no longer references `DateTime.UtcNow`.
- Line ~51 (`existing.UpdateConfiguration(...)`, `modifiedAt` parameter): no longer references `DateTime.UtcNow`.
- Both call sites use a single `var now = _timeProvider.GetUtcNow().UtcDateTime;` computed once per `SeedDefaultConfigurationsAsync` invocation (matching the pattern in `UpdateRecurringJobCronHandler.Handle`), not once per job/loop iteration, so all configurations seeded/updated within one seeding pass carry the same timestamp.
- No other logic in `SeedDefaultConfigurationsAsync` changes (job discovery, create-vs-update branching, preserved-field behavior for `CronExpression`/`IsEnabled` on update, and the "System" `lastModifiedBy`/`modifiedBy` value are all unchanged).

### FR-3: Update existing unit tests to construct RecurringJobSeeder with a controllable TimeProvider
`RecurringJobSeederTests` currently does `new RecurringJobSeeder(_repository)`. This must be updated for the new constructor, and should take the opportunity the issue calls out — asserting the exact audit timestamp using a fixed/fake clock instead of a loose approximation (today's tests do not assert `LastModifiedAt` at all).

**Acceptance criteria:**
- Test setup constructs `RecurringJobSeeder` with a `TimeProvider` — either `TimeProvider.System` (minimal, existing behavior preserved) or, preferably, a fixed fake (e.g. `Microsoft.Extensions.Time.Testing.FakeTimeProvider`, if already used elsewhere in the test suite, else a minimal in-test `TimeProvider` subclass returning a fixed `DateTimeOffset`) so timestamp assertions are exact rather than approximate.
- At least one existing or new test asserts that `LastModifiedAt` on a newly created configuration, and on an updated configuration, equals the fixed time the fake `TimeProvider` returns.
- All four existing test methods in `RecurringJobSeederTests` continue to pass unmodified in intent (same assertions on `DisplayName`, `Description`, preserved `CronExpression`/`IsEnabled`, and `LastModifiedBy == "System"`), only their constructor call and (optionally) added timestamp assertions change.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact — `TimeProvider.GetUtcNow()` is a trivial call with equivalent cost to `DateTime.UtcNow`; the change is a like-for-like substitution.

### NFR-2: Security
Not applicable — no auth, data-sensitivity, or trust-boundary changes.

### NFR-3: Testability (the actual driver of this change)
After the change, `RecurringJobSeeder`'s time-dependent behavior (the `LastModifiedAt`/audit timestamp written to `RecurringJobConfiguration` rows) must be fully controllable and assertable from unit tests via `TimeProvider`, with no reliance on mocking `DateTime.UtcNow` or approximate time-window assertions.

## Data Model
No schema or entity changes. `RecurringJobConfiguration.LastModifiedAt` (`DateTime`, existing column) continues to be populated exactly as before — only the source of the value passed into the constructor and `UpdateConfiguration(...)` changes, from `DateTime.UtcNow` to `_timeProvider.GetUtcNow().UtcDateTime`.

## API / Interface Design
No public API, controller, or MediatR request/response changes. This is an internal service constructor and implementation change only:
- `IRecurringJobSeeder` interface (`Task SeedDefaultConfigurationsAsync(...)`) is unchanged.
- Caller in `ServiceCollectionExtensions.cs:478` (`scope.ServiceProvider.GetRequiredService<IRecurringJobSeeder>()`) needs no change — DI resolves the new constructor parameter automatically since `TimeProvider` is already registered application-wide.

## Dependencies
- `System.TimeProvider` (BCL, .NET 8) — already used throughout the BackgroundJobs module and already registered in DI (proven by existing handlers such as `UpdateRecurringJobCronHandler` resolving it successfully).
- No new NuGet packages required for the production code change. If the test update adopts `Microsoft.Extensions.Time.Testing.FakeTimeProvider`, confirm during implementation whether that package is already a test-project dependency elsewhere in the solution; if not, a minimal hand-rolled fake `TimeProvider` subclass is an acceptable substitute and keeps the change two-line-scoped on the production side as the issue requests.

## Out of Scope
- Any change to `RecurringJobConfiguration`, `IRecurringJobConfigurationRepository`, `IRecurringJobSeeder`, or any other BackgroundJobs handler.
- Any change to which fields are preserved vs. overwritten on update (`CronExpression`/`IsEnabled` preservation logic is untouched).
- Any change to `ServiceCollectionExtensions.cs` or `BackgroundJobsModule.cs` DI registration (no explicit `TimeProvider` registration is expected to be needed).
- Introducing `Microsoft.Extensions.Time.Testing` as a new solution-wide dependency if it isn't already present — implementer should check first and fall back to a minimal local fake if absent, rather than adding a new package for a two-line production fix.

## Open Questions
None.

## Status: COMPLETE
