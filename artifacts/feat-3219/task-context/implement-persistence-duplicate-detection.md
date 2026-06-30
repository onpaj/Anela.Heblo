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
