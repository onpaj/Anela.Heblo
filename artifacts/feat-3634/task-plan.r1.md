# Eliminate N+1 Queries in RecurringJobConfiguration Seeding - Implementation Plan

**Goal:** Replace the per-job `GetByJobNameAsync` lookup inside `SeedDefaultConfigurationsAsync` (an N+1 query pattern executed at every application startup) with a single projection query that loads all existing `JobName` values once, then filters the default configurations in-memory before a single `SaveChangesAsync`. Behavior is preserved exactly (insert only missing jobs); read round-trips drop from N to 1.

**Architecture:** Backend-only performance fix. The change is confined to one method body in the persistence-layer repository (`RecurringJobConfigurationRepository`). No interface, call-site, entity, DbContext, DI, or schema changes. The existing `RecurringJobConfigurationRepositoryTests` already pin the two behaviors that must be preserved (9 rows from empty; 9 rows with no duplicate when one pre-exists) and are the acceptance gate.

**Tech Stack:** .NET 8, EF Core 8.0.8, xUnit

---

### task: fix-recurring-job-seeding-n-plus-one

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs:52-59` (the `foreach` loop inside `SeedDefaultConfigurationsAsync`)
- Test (existing, no change expected): `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationRepositoryTests.cs`

**Context:**
- The `Microsoft.EntityFrameworkCore` using directive is already present at line 2 of the repository file — no new imports needed.
- EF Core 8.0.8 does **not** provide `ToHashSetAsync` (added in EF Core 9.0). The spec's proposed snippet uses `ToHashSetAsync` and would fail `dotnet build`. Per the architecture review's Decision 1, materialize with `.Select(c => c.JobName).ToListAsync(cancellationToken)` wrapped in `new HashSet<string>(...)`. Do **not** upgrade the EF Core package.
- Do not add a `StringComparer` to the `HashSet` — job names are lowercase-hyphenated constants; ordinal comparison matches both the DB and the EF InMemory test provider (arch review Risk table).

- [ ] **Step 1: Confirm the existing tests currently pass (red/green baseline).** Establish that the test suite is green before changing anything, so any post-change failure is attributable to the edit. Run:
  ```bash
  dotnet test /home/user/worktrees/feature-3634-Arch-Review-Backgroundjobs-N-1-Queries-In-Seeddefa/backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobConfigurationRepositoryTests"
  ```
  Expected: all tests pass, including `SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations` and `SeedDefaultConfigurationsAsync_WhenConfigurationsExist_DoesNotDuplicate`. No test edits are needed — these two tests already pin the required behavior (FR-2) and serve as the acceptance gate unchanged.

- [ ] **Step 2: Replace the N+1 loop with a single-query + in-memory filter.** Edit `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs`. Replace exactly this block (lines 52-59):
  ```csharp
        foreach (var config in defaultConfigurations)
        {
            var existing = await GetByJobNameAsync(config.JobName, cancellationToken);
            if (existing == null)
            {
                await _context.RecurringJobConfigurations.AddAsync(config, cancellationToken);
            }
        }
  ```
  with:
  ```csharp
        // Load all existing job names in a single query (EF Core 8.0.8 has no ToHashSetAsync)
        var existingNames = new HashSet<string>(
            await _context.RecurringJobConfigurations
                .Select(c => c.JobName)
                .ToListAsync(cancellationToken));

        foreach (var config in defaultConfigurations.Where(c => !existingNames.Contains(c.JobName)))
        {
            await _context.RecurringJobConfigurations.AddAsync(config, cancellationToken);
            existingNames.Add(config.JobName); // guard against duplicate JobNames within the same batch (FR-4)
        }
  ```
  The `defaultConfigurations` projection above the block (lines 43-50) and the final `await _context.SaveChangesAsync(cancellationToken);` (line 61) remain unchanged. Result: one read query, in-memory filter, single save.

- [ ] **Step 3: Run the targeted test suite for this file.** Verify the change preserves behavior:
  ```bash
  dotnet test /home/user/worktrees/feature-3634-Arch-Review-Backgroundjobs-N-1-Queries-In-Seeddefa/backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobConfigurationRepositoryTests"
  ```
  Expected: all `RecurringJobConfigurationRepositoryTests` pass, unchanged. In particular `SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations` (9 rows) and `SeedDefaultConfigurationsAsync_WhenConfigurationsExist_DoesNotDuplicate` (9 rows, single `purchase-price-recalculation`) must be green.

- [ ] **Step 4: Build the solution.** Confirm compilation (catches any accidental `ToHashSetAsync` usage or type error):
  ```bash
  dotnet build /home/user/worktrees/feature-3634-Arch-Review-Backgroundjobs-N-1-Queries-In-Seeddefa/backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
  ```
  Expected: build succeeds with no errors.

- [ ] **Step 5: Apply code formatting.** Run:
  ```bash
  dotnet format /home/user/worktrees/feature-3634-Arch-Review-Backgroundjobs-N-1-Queries-In-Seeddefa/backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
  ```
  Expected: no formatting violations remain (command exits 0). If it rewrote whitespace, that is fine — re-run Step 4 to confirm the build still passes.

- [ ] **Step 6: Commit.** Stage only the changed repository file and commit:
  ```bash
  cd /home/user/worktrees/feature-3634-Arch-Review-Backgroundjobs-N-1-Queries-In-Seeddefa && \
  git add backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs && \
  git commit -m "perf(background-jobs): eliminate N+1 queries in RecurringJobConfiguration seeding

Replace per-job GetByJobNameAsync lookup in SeedDefaultConfigurationsAsync
with a single projection query loading all existing JobName values into a
HashSet, then filter in-memory. Read round-trips drop from N to 1; insert-
only-when-missing semantics and single SaveChangesAsync are preserved.
Uses ToListAsync + new HashSet<string> (EF Core 8.0.8 has no ToHashSetAsync).

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01McG68FR6PiDSFhPExiX2Xo"
  ```
  Expected: a single commit containing only `RecurringJobConfigurationRepository.cs`.
