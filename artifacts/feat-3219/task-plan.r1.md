# Task Plan: Move Duplicate Daily Run Detection to Persistence Layer

## Overview

Four files change in dependency order. The interface contract changes first (Domain), then the concrete implementation (Persistence), then the consumer (Application), and finally the test double and its tests (Tests). No migrations, no new files, no new packages.

## Tasks

### task: update-repository-interface

**Goal:** Change `IPackingMaterialRepository.AddDailyRunAsync` return type from `Task` to `Task<bool>`.

**Files to change:**
- `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs` — change return type on line 43 and update the XML doc comment

**Implementation steps:**
1. On line 43, replace `Task AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default);` with `Task<bool> AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default);`.
2. Add or update the XML doc comment above the method to document: returns `true` when the row was inserted; returns `false` when a duplicate unique-violation was absorbed (daily run for that date already exists); never throws on duplicate.

**Acceptance criteria:**
- `IPackingMaterialRepository.AddDailyRunAsync` signature is `Task<bool>`.
- `dotnet build` fails on the Persistence, Application, and Tests projects (expected — downstream callers not yet updated), but the Domain project itself builds cleanly.

**Notes:**
- This task must be completed before all other tasks — it is the single source-of-truth change that all downstream tasks conform to.

---

### task: implement-persistence-duplicate-detection

**Goal:** Implement `PackingMaterialRepository.AddDailyRunAsync` to call `SaveChangesAsync` internally, absorb unique-violation exceptions, and return a boolean result.

**Files to change:**
- `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs` — replace the current void-style `AddDailyRunAsync` implementation (lines 51–54) with the bool-returning implementation that owns `SaveChangesAsync` and duplicate detection

**Implementation steps:**
1. Add `using Npgsql;` at the top of the file (check if already present; `using Microsoft.EntityFrameworkCore;` is already on line 4).
2. Replace the current `AddDailyRunAsync` body (lines 51–54):
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
           // Detach the entity so the context is not left in a broken state after the failed save.
           Context.Entry(run).State = EntityState.Detached;
           return false;
       }
   }
   ```
3. Add the private static helper at the bottom of the class (before the closing brace):
   ```csharp
   private static bool IsDuplicateDailyRunViolation(DbUpdateException ex) =>
       ex.InnerException is PostgresException pg
       && pg.SqlState == PostgresErrorCodes.UniqueViolation
       && string.Equals(pg.ConstraintName, "IX_PackingMaterialDailyRuns_Date", StringComparison.Ordinal);
   ```

**Acceptance criteria:**
- `PackingMaterialRepository.AddDailyRunAsync` returns `Task<bool>`.
- `IsDuplicateDailyRunViolation` private helper exists in `PackingMaterialRepository`, not in `ConsumptionCalculationService`.
- `Context.Entry(run).State = EntityState.Detached` is executed in the catch block.
- `dotnet build` on the Persistence project succeeds.
- No new package references are added to `Anela.Heblo.Persistence.csproj` — `Microsoft.EntityFrameworkCore` and `Npgsql` are already referenced.

**Notes:**
- Depends on `update-repository-interface` completing first.
- The `Context` property and `DbSet` pattern follow the existing `BaseRepository` base class — use `Context.Set<PackingMaterialDailyRun>()` and `Context.SaveChangesAsync(...)` exactly as shown; do not call the repository-level `SaveChangesAsync` wrapper.
- Do NOT use `Context.Entry(run).State = EntityState.Detached` after a successful save — only in the catch block.

---

### task: refactor-consumption-calculation-service

**Goal:** Remove the EF Core / Npgsql catch block from `ConsumptionCalculationService` and reorder persistence calls to consume the boolean from `AddDailyRunAsync`.

**Files to change:**
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs` — restructure `ProcessDailyConsumptionAsync`; remove `IsDuplicateDailyRunViolation`; remove infrastructure `using` directives

**Implementation steps:**
1. Delete the two `using` directives that reference infrastructure types. These are implicit via the fully-qualified names on lines 79 and 123–126 (`Microsoft.EntityFrameworkCore.DbUpdateException` and `Npgsql.PostgresException` / `Npgsql.PostgresErrorCodes`). There are no explicit `using` statements at the top of the file for these namespaces — the references appear only in the fully-qualified catch expression and the private helper.
2. In `ProcessDailyConsumptionAsync`, replace the current block at lines 69–84:
   ```csharp
   if (allFactRows.Count > 0)
       await _repository.AddConsumptionRowsAsync(allFactRows, cancellationToken);

   var dailyRun = new PackingMaterialDailyRun(processingDate, processedCount);
   await _repository.AddDailyRunAsync(dailyRun, cancellationToken);

   try
   {
       await _repository.SaveChangesAsync(cancellationToken);
   }
   catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (IsDuplicateDailyRunViolation(ex))
   {
       _logger.LogWarning("PackingMaterialsDailyRunDuplicateDetected: duplicate daily run for {ProcessingDate} detected, rolling back",
           processingDate);
       return new ProcessDailyConsumptionResult(false, 0);
   }
   ```
   with:
   ```csharp
   // Insert the daily run first; SaveChangesAsync is called inside AddDailyRunAsync.
   // NOTE: Splitting into two SaveChangesAsync calls introduces a partial-success window:
   // if the second save (consumption rows) fails after the daily run committed, the date
   // is marked processed but no consumption data is written. This matches the pre-existing
   // behaviour where a non-duplicate DbUpdateException after staging left the daily run
   // uncommitted — the new design makes the daily run commit explicit and first.
   var dailyRun = new PackingMaterialDailyRun(processingDate, processedCount);
   var inserted = await _repository.AddDailyRunAsync(dailyRun, cancellationToken);
   if (!inserted)
   {
       _logger.LogWarning("PackingMaterialsDailyRunDuplicateDetected: duplicate daily run for {ProcessingDate} detected, skipping",
           processingDate);
       return new ProcessDailyConsumptionResult(false, 0);
   }

   if (allFactRows.Count > 0)
       await _repository.AddConsumptionRowsAsync(allFactRows, cancellationToken);

   await _repository.SaveChangesAsync(cancellationToken);
   ```
3. Delete the private static `IsDuplicateDailyRunViolation` method (lines 123–126).

**Acceptance criteria:**
- `ConsumptionCalculationService` contains no reference to `DbUpdateException`, `Microsoft.EntityFrameworkCore`, `Npgsql`, or `IsDuplicateDailyRunViolation`.
- `ProcessDailyConsumptionAsync` calls `AddDailyRunAsync` before `AddConsumptionRowsAsync`.
- The `LogWarning` call for duplicate detection is preserved (log event name `PackingMaterialsDailyRunDuplicateDetected`, level `Warning`).
- `dotnet build` on the Application project succeeds.
- `grep -r "EntityFrameworkCore\|Npgsql" backend/src/Anela.Heblo.Application/` returns no matches.

**Notes:**
- Depends on `update-repository-interface` completing first.
- The `SaveChangesAsync` on the last line persists consumption rows AND material quantity-update logs (the `material.UpdateQuantity` calls mutate domain entities that are tracked by EF Core). This call must remain.
- The log warning text changes from "rolling back" to "skipping" to reflect that there is nothing to roll back at that point (the daily run insertion returned false without committing).

---

### task: update-mock-repository-and-tests

**Goal:** Update `MockPackingMaterialRepository` to implement the new `Task<bool>` signature for `AddDailyRunAsync`, add a per-date configurability mechanism, and update `ConsumptionCalculationServiceTests` to exercise the duplicate path via the new mock API.

**Files to change:**
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs` — change `AddDailyRunAsync` return type; add `_addDailyRunResults` dictionary; add `SetAddDailyRunReturns` method
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs` — update `ProcessDailyConsumptionAsync_PropagatesOtherDbUpdateExceptions` test; optionally add a test for the mock-driven duplicate path

**Implementation steps:**

*In `MockPackingMaterialRepository.cs`:*

1. Add a private field after `_saveChangesException`:
   ```csharp
   private readonly Dictionary<DateOnly, bool> _addDailyRunResults = new();
   ```
2. Add a public configuration method after `SetSaveChangesException`:
   ```csharp
   public void SetAddDailyRunReturns(DateOnly date, bool result)
   {
       _addDailyRunResults[date] = result;
   }
   ```
3. Replace the current `AddDailyRunAsync` implementation (lines 174–178):
   ```csharp
   public Task<bool> AddDailyRunAsync(PackingMaterialDailyRun run, CancellationToken cancellationToken = default)
   {
       AddedDailyRuns.Add(run);
       var result = !_addDailyRunResults.TryGetValue(run.Date, out var configured) || configured;
       return Task.FromResult(result);
   }
   ```
   The default (no configuration for the date) is `true` (insertion succeeded), preserving existing test behaviour.

*In `ConsumptionCalculationServiceTests.cs`:*

4. Update `ProcessDailyConsumptionAsync_PropagatesOtherDbUpdateExceptions` (lines 363–382): the exception is now thrown by the second-phase `SaveChangesAsync` (for consumption rows + quantity updates), not the daily-run phase. The test setup and assertion remain identical — `SetSaveChangesException` on the mock still causes `SaveChangesAsync` to throw, which now fires during the consumption-row phase. Update the inline comment to reflect this:
   ```csharp
   // Set up a non-duplicate DbUpdateException (no inner PostgresException).
   // After the refactor, AddDailyRunAsync returns true (mock default), so execution
   // reaches the second SaveChangesAsync (consumption rows + quantity updates), where
   // this exception is thrown.
   ```
5. Add a new test `ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenAddDailyRunReturnsFalse` that uses `SetAddDailyRunReturns(date, false)` to simulate a concurrent duplicate at the persistence level:
   ```csharp
   [Fact]
   public async Task ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenAddDailyRunReturnsFalse()
   {
       // Arrange — simulate concurrent duplicate: AddDailyRunAsync returns false
       var date = new DateOnly(2025, 6, 15);
       var material = new PackingMaterial("Tape", 3m, ConsumptionType.PerDay, 100m);
       var materialRepo = new MockPackingMaterialRepository();
       materialRepo.SetMaterials(new[] { material });
       materialRepo.SetAddDailyRunReturns(date, false);
       var invoiceSource = new MockInvoiceConsumptionSource();

       var service = BuildService(materialRepo, invoiceSource, _mockLogger);

       // Act
       var result = await service.ProcessDailyConsumptionAsync(date);

       // Assert
       Assert.False(result.WasRun);
       Assert.Equal(0, result.MaterialsProcessed);
       // Consumption rows must NOT have been persisted after duplicate detected
       Assert.Empty(materialRepo.AddedConsumptionRows);
   }
   ```

**Acceptance criteria:**
- `MockPackingMaterialRepository.AddDailyRunAsync` returns `Task<bool>`.
- Default return (no `SetAddDailyRunReturns` call for the date) is `true`.
- `SetAddDailyRunReturns(date, false)` causes the mock to return `false` for that date.
- All existing tests in `ConsumptionCalculationServiceTests` pass without modification to their assertions.
- `ProcessDailyConsumptionAsync_PropagatesOtherDbUpdateExceptions` still passes and now includes a comment explaining the exception fires from the consumption-row save phase.
- New test `ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenAddDailyRunReturnsFalse` passes.
- `dotnet build` on the Tests project succeeds.

**Notes:**
- Depends on `update-repository-interface`, `implement-persistence-duplicate-detection`, and `refactor-consumption-calculation-service` all completing first.
- The existing test `ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenAlreadyProcessed` (line 140) exercises the `HasDailyProcessingBeenRunAsync` pre-check path and does NOT need modification — it remains the primary guard path test.
- `AddedDailyRuns.Add(run)` must still run before checking the configured result, so the assertion `Assert.Single(materialRepo.AddedDailyRuns)` in other tests continues to work. In the new duplicate-path test, `AddedDailyRuns` will contain the run even when `false` is returned, mirroring the persistence implementation which does add the entity before the save attempt.
