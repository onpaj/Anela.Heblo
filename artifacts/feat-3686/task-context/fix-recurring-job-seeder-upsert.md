### task: fix-recurring-job-seeder-upsert

**Goal:** Change `RecurringJobSeeder.SeedDefaultConfigurationsAsync` so that when a `RecurringJobConfiguration` row already exists for a discovered job, the seeder updates the row's developer-owned fields (`DisplayName`, `Description`) from `job.Metadata` via `UpdateConfiguration`, while preserving the admin-owned fields (`CronExpression`, `IsEnabled`) exactly as stored. The insert path for missing rows is unchanged. Add test coverage in the existing test file proving: (1) stale `DisplayName`/`Description` get corrected, (2) an admin-customized `CronExpression` and `IsEnabled` survive seeding, (3) `LastModifiedBy` becomes `"System"` after the update.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs`

- [ ] Step 1: Add three new failing tests to `RecurringJobSeederTests.cs`. Open `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs` and locate the existing test `SeedDefaultConfigurationsAsync_WhenConfigurationsExist_DoesNotDuplicate` (ends just before the `public void Dispose()` method). Insert the following three test methods immediately after that test's closing brace and before `public void Dispose()`:

  ```csharp
      [Fact]
      public async Task SeedDefaultConfigurationsAsync_WhenConfigurationExists_UpdatesDisplayNameAndDescription()
      {
          // Arrange - add an existing configuration with stale DisplayName/Description
          var existingConfig = new RecurringJobConfiguration(
              "purchase-price-recalculation",
              "Old Display Name",
              "Old description that no longer matches the code",
              "0 2 * * *",
              true,
              "System");

          await _context.RecurringJobConfigurations.AddAsync(existingConfig);
          await _context.SaveChangesAsync();

          var mockJobs = CreateMockJobs();

          // Act
          await _seeder.SeedDefaultConfigurationsAsync(mockJobs);

          // Assert
          var updated = await _repository.GetByJobNameAsync("purchase-price-recalculation");
          Assert.NotNull(updated);
          Assert.Equal("Purchase Price Recalculation", updated!.DisplayName);
          Assert.Equal("Recalculates purchase prices for all materials and products", updated.Description);
      }

      [Fact]
      public async Task SeedDefaultConfigurationsAsync_WhenConfigurationExists_PreservesCronExpressionAndIsEnabled()
      {
          // Arrange - add an existing configuration with an admin-customized CronExpression and IsEnabled
          var existingConfig = new RecurringJobConfiguration(
              "purchase-price-recalculation",
              "Purchase Price Recalculation",
              "Recalculates purchase prices for all materials and products",
              "0 0 * * *", // admin-customized cron, differs from mock job's "0 2 * * *"
              false,       // admin-disabled, differs from mock job's DefaultIsEnabled: true
              "System");

          await _context.RecurringJobConfigurations.AddAsync(existingConfig);
          await _context.SaveChangesAsync();

          var mockJobs = CreateMockJobs();

          // Act
          await _seeder.SeedDefaultConfigurationsAsync(mockJobs);

          // Assert
          var updated = await _repository.GetByJobNameAsync("purchase-price-recalculation");
          Assert.NotNull(updated);
          Assert.Equal("0 0 * * *", updated!.CronExpression);
          Assert.False(updated.IsEnabled);
      }

      [Fact]
      public async Task SeedDefaultConfigurationsAsync_WhenConfigurationExists_SetsLastModifiedByToSystem()
      {
          // Arrange - add an existing configuration whose last modification was made by an admin
          var existingConfig = new RecurringJobConfiguration(
              "purchase-price-recalculation",
              "Purchase Price Recalculation",
              "Recalculates purchase prices for all materials and products",
              "0 2 * * *",
              true,
              "Admin");

          await _context.RecurringJobConfigurations.AddAsync(existingConfig);
          await _context.SaveChangesAsync();

          var mockJobs = CreateMockJobs();

          // Act
          await _seeder.SeedDefaultConfigurationsAsync(mockJobs);

          // Assert
          var updated = await _repository.GetByJobNameAsync("purchase-price-recalculation");
          Assert.NotNull(updated);
          Assert.Equal("System", updated!.LastModifiedBy);
      }

  ```

  Do not modify anything else in the file at this step.

- [ ] Step 2: Run the new tests and confirm they fail against the current (unfixed) seeder. From the repository root run:

  ```
  dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~RecurringJobSeederTests"
  ```

  Expected: the three new tests (`SeedDefaultConfigurationsAsync_WhenConfigurationExists_UpdatesDisplayNameAndDescription`, `SeedDefaultConfigurationsAsync_WhenConfigurationExists_PreservesCronExpressionAndIsEnabled`, `SeedDefaultConfigurationsAsync_WhenConfigurationExists_SetsLastModifiedByToSystem`) FAIL. The first fails because `DisplayName`/`Description` remain `"Old Display Name"` / `"Old description that no longer matches the code"` (seeder currently no-ops on existing rows). The third fails because `LastModifiedBy` remains `"Admin"`. The second test (`PreservesCronExpressionAndIsEnabled`) is expected to PASS even before the fix, since the current seeder never touches an existing row at all — that's fine; it will continue to pass after the fix too, and it exists to lock in the preservation behavior going forward. The two pre-existing tests (`SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations`, `SeedDefaultConfigurationsAsync_WhenConfigurationsExist_DoesNotDuplicate`) continue to PASS.

- [ ] Step 3: Implement the fix in `RecurringJobSeeder.cs`. Open `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`. Replace the entire file contents with:

  ```csharp
  using Anela.Heblo.Domain.Features.BackgroundJobs;

  namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

  public class RecurringJobSeeder : IRecurringJobSeeder
  {
      private readonly IRecurringJobConfigurationRepository _repository;

      public RecurringJobSeeder(IRecurringJobConfigurationRepository repository)
      {
          _repository = repository;
      }

      /// <summary>
      /// Seeds database with configurations from discovered IRecurringJob implementations.
      /// Creates configurations for jobs that don't already exist in the database. For jobs
      /// that already have a configuration row, updates the developer-owned fields
      /// (DisplayName, Description) to match the current code, while preserving the
      /// admin-owned fields (CronExpression, IsEnabled) exactly as stored.
      /// </summary>
      /// <param name="jobs">Collection of discovered recurring jobs</param>
      /// <param name="cancellationToken">Cancellation token</param>
      public async Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default)
      {
          // Create configurations from discovered job metadata
          var defaultConfigurations = jobs.Select(job => new RecurringJobConfiguration(
              job.Metadata.JobName,
              job.Metadata.DisplayName,
              job.Metadata.Description,
              job.Metadata.CronExpression,
              job.Metadata.DefaultIsEnabled,
              "System"
          )).ToArray();

          foreach (var config in defaultConfigurations)
          {
              var existing = await _repository.GetByJobNameAsync(config.JobName, cancellationToken);
              if (existing == null)
              {
                  await _repository.AddAsync(config, cancellationToken);
              }
              else
              {
                  existing.UpdateConfiguration(
                      config.DisplayName,
                      config.Description,
                      existing.CronExpression,   // preserve admin override
                      "System");
                  await _repository.UpdateAsync(existing, cancellationToken);
              }
          }
      }
  }
  ```

  This changes only the `foreach` loop body (adding the `else` branch) and the XML doc comment above `SeedDefaultConfigurationsAsync`; everything else in the file is unchanged from its current state.

- [ ] Step 4: Run the full seeder test file again and confirm all tests pass:

  ```
  dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~RecurringJobSeederTests"
  ```

  Expected: all 5 tests pass — `SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations`, `SeedDefaultConfigurationsAsync_WhenConfigurationsExist_DoesNotDuplicate`, `SeedDefaultConfigurationsAsync_WhenConfigurationExists_UpdatesDisplayNameAndDescription`, `SeedDefaultConfigurationsAsync_WhenConfigurationExists_PreservesCronExpressionAndIsEnabled`, `SeedDefaultConfigurationsAsync_WhenConfigurationExists_SetsLastModifiedByToSystem`.

- [ ] Step 5: Run the full backend build and formatting check to make sure nothing else broke:

  ```
  dotnet build backend/Anela.Heblo.sln
  dotnet format backend/Anela.Heblo.sln --verify-no-changes
  ```

  Expected: build succeeds with no errors; `dotnet format --verify-no-changes` reports no formatting violations. If `dotnet format` reports violations introduced by this change, run `dotnet format backend/Anela.Heblo.sln` (without `--verify-no-changes`) to apply fixes, then re-run the test command from Step 4 to confirm tests still pass.

- [ ] Step 6: Run the full test project (not just the filtered subset) to confirm no regressions elsewhere in the solution:

  ```
  dotnet test backend/Anela.Heblo.sln
  ```

  Expected: all tests in the solution pass, with no failures introduced outside `RecurringJobSeederTests`.

- [ ] Step 7: Commit the change. Stage exactly the two touched files and commit:

  ```
  git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs
  git commit -m "Fix RecurringJobSeeder to upsert developer-owned metadata on existing job configs"
  ```

## Self-Review

- FR-1 (upsert developer-owned metadata, preserve existing `CronExpression` argument, `modifiedBy = "System"`): covered by Step 3 implementation and verified by the `UpdatesDisplayNameAndDescription` and `PreservesCronExpressionAndIsEnabled` tests in Step 1.
- FR-1's `LastModifiedBy == "System"` acceptance criterion: covered by the `SetsLastModifiedByToSystem` test.
- FR-1's "insert path unchanged" acceptance criterion: covered by the pre-existing, untouched `SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations` test, which continues to pass since the `existing == null` branch is byte-for-byte unchanged.
- FR-2 (`IsEnabled` never touched by the update path): covered by `PreservesCronExpressionAndIsEnabled`, and structurally guaranteed since `UpdateConfiguration` has no `isEnabled` parameter (unchanged domain method, not modified by this plan).
- NFR-1 (unconditional update, no diff-and-skip, no measurable startup delay): satisfied by construction — the `else` branch always calls `UpdateConfiguration` + `UpdateAsync` with no equality check; no batching/optimization added, matching the spec's explicit preference for simplicity.
- NFR-2 (no new auth/security surface, `modifiedBy` hardcoded to `"System"` for both paths): satisfied — `"System"` literal reused unchanged from the insert path.
- No new interfaces, repository methods, or domain methods were introduced; `UpdateConfiguration` and `UpdateAsync` are reused exactly as they exist today, matching the architecture review's Decision 1 and the design document.
- No placeholders: every step contains complete, literal code (full replacement file for the seeder, complete test method bodies) and exact runnable commands with stated expected outcomes.
- Out-of-scope items from the spec (orphaned configuration cleanup, admin UI/controller changes, `UpdateConfiguration` validation changes, diff-and-skip optimization) are not touched by any step, consistent with the spec's "Out of Scope" section.
