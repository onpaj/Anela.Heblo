# Architecture Review: Move Duplicate Daily Run Detection to Persistence Layer

## Skip Design: true

## Architectural Fit Assessment

This is a narrow, well-scoped refactor. The violation is genuine: `ConsumptionCalculationService` (Application layer) imports `Microsoft.EntityFrameworkCore.DbUpdateException` and `Npgsql.PostgresException` directly, both of which are infrastructure types. Removing that coupling is correct.

**Key contextual findings from codebase exploration:**

1. **The Application project already references Persistence.** `Anela.Heblo.Application.csproj` has a `<ProjectReference>` to `Anela.Heblo.Persistence`. This means the Application layer technically has transitive access to EF Core and Npgsql today via the Persistence project, not via its own `<PackageReference>`. The Application `.csproj` itself does **not** list `Microsoft.EntityFrameworkCore` or `Npgsql` as direct package references — those live in `Anela.Heblo.Persistence.csproj`. The usages of EF Core types visible in Application `.cs` files (Smartsupp, Photobank, Catalog, Bank) rely on this transitive reference, not a direct package reference.

2. **FR-5 (removing the package reference) will not apply.** There is no direct EF Core / Npgsql `<PackageReference>` in `Anela.Heblo.Application.csproj` to remove. The build will remain clean after removing the `using` statements from `ConsumptionCalculationService`. The Application project will continue to have transitive access to those types via the Persistence reference — this is an existing, wider architectural debt unrelated to this ticket.

3. **The existing `PostgresExceptionTranslator` pattern is the right precedent.** It wraps infrastructure exceptions at the Persistence boundary and surfaces domain-typed exceptions to the Application layer. However, for this feature the spec chooses a boolean return value over a domain exception, which is equally valid and arguably simpler (no new exception type required).

4. **The current `AddDailyRunAsync` in `PackingMaterialRepository` does not call `SaveChangesAsync` — it only stages the entity.** `SaveChangesAsync` is called later by the service via `_repository.SaveChangesAsync(...)`. The spec correctly identifies that the refactor must move `SaveChangesAsync` for the daily run inside the repository method (FR-2/FR-3 are coupled).

5. **The `PropagatesOtherDbUpdateExceptions` test in `ConsumptionCalculationServiceTests` currently injects a `DbUpdateException` via `MockPackingMaterialRepository.SetSaveChangesException`.** After the refactor, the service's direct `SaveChangesAsync` call is split: the daily-run save moves inside the repository, and a separate `SaveChangesAsync` call handles consumption rows. The mock's `SetSaveChangesException` would fire on the consumption-rows save path, which preserves the intent of the test (non-duplicate exceptions propagate). This is the correct resolution of the open question in the spec.

6. **EF Core change-tracker poisoning is a real risk.** After a `DbUpdateException`, the EF Core context is in a faulted state for that entity. The spec's implementation sketch correctly detaches the `run` entity after catching the duplicate violation. This must not be omitted.

## Proposed Architecture

### Component Overview

```
DailyConsumptionJob
    └── ProcessDailyConsumptionHandler (MediatR)
            └── ConsumptionCalculationService (Application)
                    ├── IPackingMaterialRepository.HasDailyProcessingBeenRunAsync()   [guard, unchanged]
                    ├── IPackingMaterialRepository.AddDailyRunAsync()                 [NOW: Task<bool>]
                    │       └── PackingMaterialRepository (Persistence)
                    │               ├── Context.Set<PackingMaterialDailyRun>().AddAsync()
                    │               ├── Context.SaveChangesAsync()   ← moved here
                    │               ├── catch DbUpdateException / Npgsql.PostgresException
                    │               │       └── Detach entity, return false
                    │               └── return true
                    ├── IPackingMaterialRepository.AddConsumptionRowsAsync()          [unchanged]
                    └── IPackingMaterialRepository.SaveChangesAsync()                 [consumption rows + quantity updates only]
```

The two-phase save is intentional and necessary: the duplicate-detection logic requires a committed daily run row before continuing.

### Key Design Decisions

#### Decision 1: Boolean Return vs. Domain Exception

**Options considered:**
- `Task<bool>` return from `AddDailyRunAsync` — the spec's chosen approach
- Throw a domain exception (e.g., `DailyRunAlreadyProcessedException`) analogous to `GridLayoutPersistenceException`

**Chosen approach:** `Task<bool>` return.

**Rationale:** The boolean return is appropriate here because the duplicate is an expected, non-exceptional outcome of a race condition. A domain exception would signal a programming error or unrecoverable failure, which this is not. The `GridLayoutPersistenceException` pattern is better suited to unexpected infrastructure failures that must propagate upward; this scenario is a successful detection of a known idempotency scenario.

#### Decision 2: EF Core Context Repair After Duplicate Catch

**Options considered:**
- Detach the entity (`Context.Entry(run).State = EntityState.Detached`)
- Reset the full context
- Create a new unit of work scope

**Chosen approach:** Detach only the `run` entity immediately after catching the unique-violation.

**Rationale:** Resetting or replacing the context is disproportionate. The duplicate exception is caught within the same repository method that staged the entity — no other entities have been added to the change tracker at this point (consumption rows and quantity updates are staged later, after `AddDailyRunAsync` returns). Detaching the single entity is safe and minimal.

#### Decision 3: Test Coverage for PropagatesOtherDbUpdateExceptions

**Options considered:**
- Keep the test as-is (exception injected via `SetSaveChangesException`)
- Rewrite to simulate `AddDailyRunAsync` itself throwing

**Chosen approach:** Keep `SetSaveChangesException` on the mock; the test now exercises that non-duplicate `DbUpdateException` from the consumption-rows `SaveChangesAsync` (second phase) propagates to the caller.

**Rationale:** This test verifies that `ConsumptionCalculationService` does not swallow unrelated exceptions from its own persistence path. After FR-3, the service still calls `_repository.SaveChangesAsync(...)` for the second phase. The `MockPackingMaterialRepository.SetSaveChangesException` fires on that call, which is the correct failure path to test. The test comment should be updated to reflect the new context (exception comes from the consumption-row phase), but the mechanism and assertion remain valid.

## Implementation Guidance

### Directory / Module Structure

No new files required. Changes touch exactly four existing files:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs` | Change `AddDailyRunAsync` return type to `Task<bool>` |
| `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs` | Implement `AddDailyRunAsync` with `SaveChangesAsync`, catch, detach, return bool |
| `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs` | Remove `try/catch` block; consume boolean from `AddDailyRunAsync`; reorder persistence calls |
| `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs` | Change `AddDailyRunAsync` to `Task<bool>`; add `SetAddDailyRunReturns` configuration method |

The `ConsumptionCalculationServiceTests` file will also need its `PropagatesOtherDbUpdateExceptions` test comment updated.

### Interfaces and Contracts

**Domain interface — `IPackingMaterialRepository`:**
```csharp
// Before
Task AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default);

// After
Task<bool> AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default);
// true  → row inserted (new date)
// false → unique-violation absorbed (duplicate; already processed)
// throws → any other DbUpdateException, propagated unchanged
```

**Mock configuration — `MockPackingMaterialRepository`:**

The mock currently returns `Task.CompletedTask` unconditionally. After the change it must return `Task.FromResult(true)` by default and support per-date override:

```csharp
// New state
private readonly Dictionary<DateOnly, bool> _addDailyRunReturns = new();

// New configuration method
public void SetAddDailyRunReturns(DateOnly date, bool result)
    => _addDailyRunReturns[date] = result;

// Updated implementation
public Task<bool> AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default)
{
    AddedDailyRuns.Add(run);
    var result = _addDailyRunReturns.TryGetValue(run.Date, out var configured) ? configured : true;
    return Task.FromResult(result);
}
```

**Application service — `ConsumptionCalculationService.ProcessDailyConsumptionAsync`:**

Revised persistence section (replaces lines 69–84 in the current file):

```csharp
var dailyRun = new PackingMaterialDailyRun(processingDate, processedCount);
var inserted = await _repository.AddDailyRunAsync(dailyRun, cancellationToken);
if (!inserted)
{
    _logger.LogWarning(
        "PackingMaterialsDailyRunDuplicateDetected: duplicate daily run for {ProcessingDate} detected, skipping",
        processingDate);
    return new ProcessDailyConsumptionResult(false, 0);
}

// NOTE: AddDailyRunAsync has already committed the daily run row.
// If the second SaveChangesAsync below fails, the date is marked processed
// but no consumption data will have been written. This is acceptable:
// the job is idempotent via HasDayAlreadyBeenProcessedAsync, and the
// consumption job can be re-run manually if the consumption phase fails.
if (allFactRows.Count > 0)
    await _repository.AddConsumptionRowsAsync(allFactRows, cancellationToken);

await _repository.SaveChangesAsync(cancellationToken);
```

The `IsDuplicateDailyRunViolation` private method and the `try/catch (DbUpdateException)` block are removed in their entirety from this class.

**Repository implementation — `PackingMaterialRepository.AddDailyRunAsync`:**

```csharp
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
        Context.Entry(run).State = EntityState.Detached;
        return false;
    }
}

private static bool IsDuplicateDailyRunViolation(DbUpdateException ex) =>
    ex.InnerException is Npgsql.PostgresException pg
    && pg.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation
    && string.Equals(pg.ConstraintName, "IX_PackingMaterialDailyRuns_Date", StringComparison.Ordinal);
```

`using Microsoft.EntityFrameworkCore;` is already present in `PackingMaterialRepository.cs`. No new `using` directives are needed.

### Data Flow

**Happy path (first run for a date):**
1. Service: `HasDailyProcessingBeenRunAsync` → `false` → continue
2. Service: compute fact rows and quantity decrements (in memory)
3. Service: `AddDailyRunAsync(dailyRun)` → Repository: AddAsync + SaveChangesAsync → returns `true`
4. Service: `AddConsumptionRowsAsync(allFactRows)` → Repository: AddRangeAsync (stages only)
5. Service: `SaveChangesAsync()` → Repository: commits consumption rows + quantity updates → returns
6. Service: logs success, returns `ProcessDailyConsumptionResult(true, processedCount)`

**Duplicate path (race condition):**
1. Service: `HasDailyProcessingBeenRunAsync` → `false` (row not yet committed by concurrent run)
2. Service: compute fact rows (in memory)
3. Service: `AddDailyRunAsync(dailyRun)` → Repository: AddAsync + SaveChangesAsync → throws `DbUpdateException` with unique-violation → detaches entity → returns `false`
4. Service: logs warning, returns `ProcessDailyConsumptionResult(false, 0)`

**Already-processed path (normal repeat):**
1. Service: `HasDailyProcessingBeenRunAsync` → `true` → logs info, returns `ProcessDailyConsumptionResult(false, 0)` immediately

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Forgetting to detach the `run` entity after catching the duplicate violation, leaving the EF Core context in an error state | High | The implementation sketch in the spec and in this review both include the detach. Code review must verify it is present. |
| `PropagatesOtherDbUpdateExceptions` test silently passes for the wrong reason (it now tests the consumption-row phase, not the daily-run phase) | Low | Update the test comment to state explicitly that the exception now comes from the consumption-row `SaveChangesAsync`. The assertion logic is unchanged and remains correct. |
| FR-5 misapplied: developer attempts to remove the Persistence project reference from Application.csproj | High | FR-5 is a no-op for this project. There is no direct EF Core or Npgsql `<PackageReference>` in `Anela.Heblo.Application.csproj`. Do not attempt to remove the `<ProjectReference>` to Persistence — this would break many other Application features. The task is only to remove the two `using` directives from `ConsumptionCalculationService.cs`. |
| Partial-success window introduced by two-phase save: daily run committed, consumption-row save fails | Low | This failure mode existed implicitly before (uncommitted change tracker entities). Now it is explicit and documented in a code comment. The nightly job can be re-run manually; `HasDailyProcessingBeenRunAsync` will return `true` and skip gracefully. Accept and document; do not attempt atomic compensation. |
| Existing tests that assert on `AddedDailyRuns` break because `AddDailyRunAsync` now returns a value | Medium | All callers in tests that use `MockPackingMaterialRepository` compile against `IPackingMaterialRepository`. Once the interface is updated, the compiler will flag every call site. Update `MockPackingMaterialRepository.AddDailyRunAsync` and every test that configures or inspects daily run behaviour. |

## Specification Amendments

### Amendment 1: FR-5 scope correction

FR-5 states "verify whether `Anela.Heblo.Application` has a `PackageReference` to `Microsoft.EntityFrameworkCore` or `Npgsql`." **It does not.** The application project has a `<ProjectReference>` to `Anela.Heblo.Persistence`, which transitively provides EF Core. The spec's verification step is still worth running (`grep -r "EntityFrameworkCore\|Npgsql" backend/src/Anela.Heblo.Application/` on `.cs` files), but the success criterion should be: the two `using` directives in `ConsumptionCalculationService.cs` are removed, and the grep returns no matches in PackingMaterials. Other Application features (Smartsupp, Photobank, Catalog, Bank, Invoices) reference EF Core types via the same transitive route and are pre-existing violations outside this ticket's scope.

### Amendment 2: Open question resolution

The open question about `PropagatesOtherDbUpdateExceptions` is resolved as follows: keep the `SetSaveChangesException` mechanism and the existing assertion. The test's `DbUpdateException` will now fire on the consumption-row `SaveChangesAsync` (second phase of FR-3), which is the remaining path where the service makes a direct `SaveChangesAsync` call. Update the test's comment to read:

> "SaveChangesAsync throws a DbUpdateException unrelated to the daily run constraint — verifies the exception propagates from the consumption-row save phase."

No structural change to the test is required.

## Prerequisites

None. This is a pure code change:
- No database migrations
- No new packages
- No configuration changes
- No infrastructure setup

The change is entirely self-contained within the four files listed above.
