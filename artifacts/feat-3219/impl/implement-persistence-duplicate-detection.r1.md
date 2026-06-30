# implement-persistence-duplicate-detection — Implementation Summary

## Task

Replace the stub `AddDailyRunAsync` in `PackingMaterialRepository` with a bool-returning implementation that owns `SaveChangesAsync` and absorbs unique-violation exceptions (duplicate daily run).

## Changes Made

**File:** `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs`

1. Added `using Npgsql;` to the using block (was not previously present).
2. Replaced `public async Task AddDailyRunAsync(...)` with `public async Task<bool> AddDailyRunAsync(...)`:
   - Calls `Context.Set<PackingMaterialDailyRun>().AddAsync(run, cancellationToken)` then `Context.SaveChangesAsync(cancellationToken)`.
   - Returns `true` on success.
   - Catches `DbUpdateException` filtered through `IsDuplicateDailyRunViolation`; detaches the entity from the context and returns `false`.
3. Added private static helper `IsDuplicateDailyRunViolation(DbUpdateException ex)`:
   - Checks that `InnerException` is a `PostgresException` with `SqlState == PostgresErrorCodes.UniqueViolation` and `ConstraintName == "IX_PackingMaterialDailyRuns_Date"`.

## Acceptance Criteria Verified

- [x] `AddDailyRunAsync` returns `Task<bool>`.
- [x] `IsDuplicateDailyRunViolation` private helper exists in `PackingMaterialRepository`.
- [x] `Context.Entry(run).State = EntityState.Detached` is in the catch block.
- [x] `dotnet build backend/src/Anela.Heblo.Persistence/` succeeds — 0 errors, 86 warnings (all pre-existing).
- [x] No new package references added to `Anela.Heblo.Persistence.csproj` (Npgsql was already a transitive dependency via EF Core Npgsql provider).

## Commit

`a661263` — `@claude implement-persistence-duplicate-detection: move duplicate detection into repository`
