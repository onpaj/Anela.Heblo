# Specification: Move Duplicate Daily Run Detection to Persistence Layer

## Summary

`ConsumptionCalculationService` in the Application layer currently catches `Microsoft.EntityFrameworkCore.DbUpdateException` and pattern-matches on `Npgsql.PostgresException` to detect duplicate daily run inserts, violating Clean Architecture's dependency rule. This spec defines the work to relocate that detection to the Persistence layer so the Application layer reacts only to domain-meaningful signals — keeping it free of EF Core and Npgsql types.

## Background

The nightly daily consumption job (`DailyConsumptionJob` → `ProcessDailyConsumptionHandler` → `ConsumptionCalculationService`) inserts a `PackingMaterialDailyRun` row keyed on `Date` (unique index `IX_PackingMaterialDailyRuns_Date`). If two runs race for the same date, the second fails with a PostgreSQL unique-violation error. The service currently catches `DbUpdateException`, inspects the inner `Npgsql.PostgresException`, and compares the constraint name — all infrastructure details that have no place in the Application layer.

The codebase already has a precedent for this pattern: `PostgresExceptionTranslator` in `Anela.Heblo.Persistence/Infrastructure/` performs the same kind of translation for `GridLayout` operations, converting `NpgsqlException` into domain-typed `GridLayoutPersistenceException` before it crosses the Persistence→Application boundary.

## Functional Requirements

### FR-1: Change `IPackingMaterialRepository.AddDailyRunAsync` to return a boolean

Change the signature of `AddDailyRunAsync` from `Task` to `Task<bool>`. The return value `true` means the row was successfully inserted; `false` means a daily run for that date already existed (duplicate detected). The method must not throw on duplicate — it absorbs the unique-violation and surfaces the outcome through the return value.

**Acceptance criteria:**
- `IPackingMaterialRepository.AddDailyRunAsync` returns `Task<bool>`.
- Calling `AddDailyRunAsync` with a date that already has a `PackingMaterialDailyRun` row returns `false` without throwing.
- Calling `AddDailyRunAsync` with a new date inserts the row and returns `true`.
- Any `DbUpdateException` whose inner exception is **not** a unique-violation on `IX_PackingMaterialDailyRuns_Date` is re-thrown unchanged.

### FR-2: Implement duplicate detection inside `PackingMaterialRepository.AddDailyRunAsync`

In the concrete `PackingMaterialRepository`, implement `AddDailyRunAsync` to:
1. Add the `PackingMaterialDailyRun` entity to the change tracker.
2. Call `SaveChangesAsync` immediately (saving only the daily run — see FR-3 for context).
3. Catch `DbUpdateException` where `IsDuplicateDailyRunViolation` is true (move the existing private helper here verbatim) and return `false`.
4. On success return `true`.
5. On any other `DbUpdateException`, re-throw.

**Note:** The Npgsql / EF Core catch logic moved here is identical to what exists in `ConsumptionCalculationService` today — it is not rewritten, only relocated.

**Acceptance criteria:**
- The private helper `IsDuplicateDailyRunViolation(DbUpdateException)` (checking `Npgsql.PostgresException`, `SqlState == UniqueViolation`, `ConstraintName == "IX_PackingMaterialDailyRuns_Date"`) exists in `PackingMaterialRepository` (or a dedicated internal helper), not in `ConsumptionCalculationService`.
- `PackingMaterialRepository` references `Microsoft.EntityFrameworkCore` and `Npgsql` (already a Persistence-layer dependency); the Application layer acquires no new package references.

### FR-3: Restructure `ConsumptionCalculationService.ProcessDailyConsumptionAsync` to use the boolean return

Currently `SaveChangesAsync` is called once at the end, after both consumption rows and the daily run have been staged. Because `AddDailyRunAsync` must call `SaveChangesAsync` internally (FR-2), the calling order in `ProcessDailyConsumptionAsync` must change:

1. Compute fact rows and quantity updates (unchanged).
2. Call `_repository.AddDailyRunAsync(dailyRun, cancellationToken)` — which now calls `SaveChangesAsync` internally.
   - If it returns `false`, log a warning and return `new ProcessDailyConsumptionResult(false, 0)`.
   - If it returns `true`, continue.
3. Persist consumption rows and quantity updates: call `_repository.AddConsumptionRowsAsync(...)` then `_repository.SaveChangesAsync(...)`.

**Acceptance criteria:**
- `ConsumptionCalculationService` no longer catches `DbUpdateException` or references any EF Core / Npgsql type.
- The `IsDuplicateDailyRunViolation` private method is removed from `ConsumptionCalculationService`.
- The duplicate-detected log warning message is preserved (text may be adjusted for the new code location, but the log event is retained at `LogWarning` level).
- All existing unit tests in `ConsumptionCalculationServiceTests` pass after the change (test coverage remains complete).

### FR-4: Update `MockPackingMaterialRepository` to return bool from `AddDailyRunAsync`

Update `MockPackingMaterialRepository.AddDailyRunAsync` to match the new `Task<bool>` signature. Default behaviour: always return `true` (row inserted). The test helper must also expose a way to simulate a duplicate (return `false`) so the existing test for duplicate detection (`ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenAlreadyProcessed`) can continue to exercise the service-level duplicate path without relying on setting `HasDailyProcessingBeenRunAsync`.

**Note:** The pre-check `HasDayAlreadyBeenProcessedAsync` at the top of `ProcessDailyConsumptionAsync` (line 28) is the primary guard for already-processed dates. The duplicate detection in `AddDailyRunAsync` is a race-condition safety net for concurrent executions. Both paths must continue to work.

**Acceptance criteria:**
- `MockPackingMaterialRepository.AddDailyRunAsync` returns `Task<bool>`.
- A `SetAddDailyRunReturns(DateOnly date, bool result)` (or equivalent) method allows tests to configure the return for a given date.
- The test `ProcessDailyConsumptionAsync_PropagatesOtherDbUpdateExceptions` must be updated: since `ConsumptionCalculationService` no longer calls `SaveChangesAsync` directly for the daily run, the mock approach of injecting a `DbUpdateException` via `SetSaveChangesException` must be reconsidered. The test may instead verify that exceptions from the repository's own `SaveChangesAsync` (for consumption rows) still propagate. See Open Questions.

### FR-5: Remove `Microsoft.EntityFrameworkCore` and `Npgsql` package references from the Application project (if applicable)

Verify whether `Anela.Heblo.Application` has a `PackageReference` to `Microsoft.EntityFrameworkCore` or `Npgsql`. If the daily run catch block was the only usage, remove the reference(s) to prevent future accidental coupling.

**Acceptance criteria:**
- After the change, `dotnet build` succeeds with no references to `Microsoft.EntityFrameworkCore` or `Npgsql` in `Anela.Heblo.Application.csproj`, unless other legitimate uses exist.
- `grep -r "EntityFrameworkCore\|Npgsql" backend/src/Anela.Heblo.Application/` returns no matches (excluding `.csproj` if legitimate indirect uses remain).

## Non-Functional Requirements

### NFR-1: Performance

No performance impact expected. The restructuring moves a `SaveChangesAsync` call earlier in the sequence (for the daily run only) rather than batching everything. The daily run row is tiny; the split into two `SaveChangesAsync` calls is acceptable. The job runs once per night.

### NFR-2: Correctness / Atomicity

Splitting `SaveChangesAsync` into two calls introduces a partial-success window: if the second `SaveChangesAsync` (for consumption rows + quantity updates) fails after the daily run row was committed, the date is marked processed but no consumption data was written. This was already the case in the original code when a non-duplicate `DbUpdateException` occurred after staging — the daily run entity was in the change tracker but never committed. The new design makes the daily run commit explicit and first, which is functionally equivalent for the happy path and preserves the existing behaviour for failure paths. Document this trade-off in a code comment inside `ProcessDailyConsumptionAsync`.

### NFR-3: Testability

The Application layer must remain fully unit-testable via the existing in-memory `MockPackingMaterialRepository`. No integration test with a real PostgreSQL instance is required for this change (though the Persistence-layer unit test `PackingMaterialLogPersistenceTests` may be extended to cover the duplicate path if a suitable in-memory or SQLite fixture is available).

### NFR-4: Build validation

After the change: `dotnet build` must succeed and `dotnet format --verify-no-changes` must produce no diff.

## Data Model

No schema changes. `PackingMaterialDailyRun` and its unique index `IX_PackingMaterialDailyRuns_Date` remain unchanged. No migration is required.

## API / Interface Design

### Repository interface change

```csharp
// Before (IPackingMaterialRepository)
Task AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default);

// After
Task<bool> AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default);
// Returns true  → row inserted (first run for this date)
// Returns false → unique-violation absorbed (duplicate; already processed)
```

### Persistence implementation sketch

```csharp
// PackingMaterialRepository
public async Task<bool> AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default)
{
    await Context.Set<PackingMaterialDailyRun>().AddAsync(run, cancellationToken);
    try
    {
        await Context.SaveChangesAsync(cancellationToken);
        return true;
    }
    catch (DbUpdateException ex) when (IsDuplicateDailyRunViolation(ex))
    {
        // Detach the entity so the context is not left in a broken state
        Context.Entry(run).State = EntityState.Detached;
        return false;
    }
}

private static bool IsDuplicateDailyRunViolation(DbUpdateException ex) =>
    ex.InnerException is Npgsql.PostgresException pg
    && pg.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation
    && string.Equals(pg.ConstraintName, "IX_PackingMaterialDailyRuns_Date", StringComparison.Ordinal);
```

**Important:** After catching the unique-violation, the EF Core change tracker entry for `run` must be detached or the context state reset, otherwise the context remains poisoned for subsequent operations.

### Application service change (ConsumptionCalculationService)

The `try/catch (DbUpdateException)` block around `SaveChangesAsync` at lines 75–84 is removed. Instead, after computing fact rows and before persisting them, the service calls `AddDailyRunAsync` and acts on the boolean:

```csharp
var dailyRun = new PackingMaterialDailyRun(processingDate, processedCount);
var inserted = await _repository.AddDailyRunAsync(dailyRun, cancellationToken);
if (!inserted)
{
    _logger.LogWarning("PackingMaterialsDailyRunDuplicateDetected: duplicate daily run for {ProcessingDate} detected, skipping", processingDate);
    return new ProcessDailyConsumptionResult(false, 0);
}

if (allFactRows.Count > 0)
    await _repository.AddConsumptionRowsAsync(allFactRows, cancellationToken);

await _repository.SaveChangesAsync(cancellationToken);
```

## Dependencies

- `Anela.Heblo.Domain` — `IPackingMaterialRepository` interface (signature change).
- `Anela.Heblo.Persistence` — `PackingMaterialRepository` (implementation change); already depends on `Microsoft.EntityFrameworkCore` and `Npgsql`.
- `Anela.Heblo.Application` — `ConsumptionCalculationService` (catch block removal); `IConsumptionCalculationService` interface unchanged.
- `Anela.Heblo.Tests` — `MockPackingMaterialRepository` and `ConsumptionCalculationServiceTests` (test updates).

No new libraries required. No new tables or migrations.

## Out of Scope

- Replacing `SaveChangesAsync` with an idempotent `INSERT ... ON CONFLICT DO NOTHING` raw-SQL approach (option 2 from the brief). The boolean-return approach (option 1) is chosen because it reuses existing EF change tracking patterns and is consistent with how `GridLayoutPersistenceException` translation is handled elsewhere.
- Adding integration tests for `PackingMaterialRepository.AddDailyRunAsync` against a real PostgreSQL instance. Unit coverage via `MockPackingMaterialRepository` is sufficient for this change.
- Changing the duplicate-detection behaviour for consumption rows (`AddConsumptionRowsAsync`); those have no unique constraint.
- Any change to the scheduler / `DailyConsumptionJob` call site.
- Any change to the API response shape or controller.

## Open Questions

1. **Test for `PropagatesOtherDbUpdateExceptions`:** The existing test injects a `DbUpdateException` via `MockPackingMaterialRepository.SetSaveChangesException`. After FR-3, the daily run's `SaveChangesAsync` is called inside `AddDailyRunAsync` (in the concrete repository), not in the service. The mock's `SaveChangesAsync` would still be called for the consumption rows. Should the test be updated to: (a) inject the exception via `SetSaveChangesException` and verify it propagates from the consumption-row save, or (b) simulate `AddDailyRunAsync` itself throwing a non-duplicate `DbUpdateException`? Clarify which failure scenario this test is intended to cover before implementation.

## Status: HAS_QUESTIONS
