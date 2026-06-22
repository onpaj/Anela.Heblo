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
