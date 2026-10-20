### task: guard-seeder-audit-field-overwrite

**Goal**

Make `RecurringJobSeeder.SeedDefaultConfigurationsAsync` skip the `UpdateConfiguration`/`UpdateAsync`
call for an existing job row when none of `DisplayName`, `Description`, `TimeZoneId` differ from what
is stored, so `LastModifiedAt`/`LastModifiedBy` are left untouched in that case. When at least one of
those three fields does differ, behavior is unchanged: the row is updated and audit fields are
stamped to `"System"` / current seed time, exactly as today. `CronExpression` and `IsEnabled` remain
excluded from the comparison and remain preserved exactly as stored in all cases (no regression).

**Context** (self-contained — you only read this section)

File to modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`.
Its current full content (62 lines):

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

public class RecurringJobSeeder : IRecurringJobSeeder
{
    private readonly IRecurringJobConfigurationRepository _repository;
    private readonly TimeProvider _timeProvider;

    public RecurringJobSeeder(IRecurringJobConfigurationRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
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
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Create configurations from discovered job metadata
        var defaultConfigurations = jobs.Select(job => new RecurringJobConfiguration(
            job.Metadata.JobName,
            job.Metadata.DisplayName,
            job.Metadata.Description,
            job.Metadata.CronExpression,
            job.Metadata.TimeZoneId,
            job.Metadata.DefaultIsEnabled,
            "System",
            now
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
                    config.TimeZoneId,
                    "System",
                    now);
                await _repository.UpdateAsync(existing, cancellationToken);
            }
        }
    }
}
```

`RecurringJobConfiguration` (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`)
is NOT modified by this task. Its relevant public members (for reference, already correct, do not
touch): `DisplayName`, `Description`, `CronExpression`, `TimeZoneId`, `IsEnabled`, `LastModifiedAt`,
`LastModifiedBy` (all `{ get; private set; }`), and `UpdateConfiguration(displayName, description,
cronExpression, timeZoneId, modifiedBy, modifiedAt)` which unconditionally sets
`DisplayName`/`Description`/`CronExpression`/`TimeZoneId`/`LastModifiedAt`/`LastModifiedBy` when
called — this method's behavior must NOT change; only whether the seeder calls it changes.

- [ ] **Step 1: Write the failing test for the "nothing changed" case**

Open `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs`. Add this new
test method anywhere among the other `[Fact]` methods (e.g. directly after
`SeedDefaultConfigurationsAsync_WhenConfigurationExists_ResyncsTimeZoneIdFromMetadata`, before the
`Dispose()` method):

```csharp
[Fact]
public async Task SeedDefaultConfigurationsAsync_WhenNothingChanged_PreservesLastModifiedAtAndBy()
{
    // Arrange - existing row's seeded fields (DisplayName/Description/TimeZoneId) already match
    // current metadata exactly; LastModifiedBy/At record a prior ADMIN action (e.g. a CRON edit),
    // not the seeder.
    var adminModifiedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    var existingConfig = new RecurringJobConfiguration(
        "purchase-price-recalculation",
        "Purchase Price Recalculation",
        "Recalculates purchase prices for all materials and products",
        "0 2 * * *",
        RecurringJobMetadata.DefaultTimeZoneId,
        true,
        "Admin",
        adminModifiedAt);

    await _context.RecurringJobConfigurations.AddAsync(existingConfig);
    await _context.SaveChangesAsync();

    var mockJobs = CreateMockJobs();

    // Act
    await _seeder.SeedDefaultConfigurationsAsync(mockJobs);

    // Assert - nothing seeded differs, so the admin's audit trail must survive the seed pass
    var updated = await _repository.GetByJobNameAsync("purchase-price-recalculation");
    Assert.NotNull(updated);
    Assert.Equal("Admin", updated!.LastModifiedBy);
    Assert.Equal(adminModifiedAt, updated.LastModifiedAt);
}
```

This test uses the same `"purchase-price-recalculation"` job and `CreateMockJobs()` fixture as the
existing tests in this file (its mock metadata: `DisplayName = "Purchase Price Recalculation"`,
`Description = "Recalculates purchase prices for all materials and products"`, `CronExpression =
"0 2 * * *"`, `TimeZoneId = RecurringJobMetadata.DefaultTimeZoneId`) — the `existingConfig` above is
constructed to match all three seeded fields exactly, so no developer-owned field differs.

- [ ] **Step 2: Run the new test to verify it fails**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobSeederTests.SeedDefaultConfigurationsAsync_WhenNothingChanged_PreservesLastModifiedAtAndBy"
```
Expected: **FAIL** — `Assert.Equal("Admin", updated!.LastModifiedBy)` fails because the current
(unfixed) seeder always calls `UpdateConfiguration(...)`, which sets `LastModifiedBy` to `"System"`
and `LastModifiedAt` to the fake `TimeProvider`'s `FixedTime` regardless of whether anything changed.

- [ ] **Step 3: Fix the test that currently asserts the buggy behavior**

The existing test `SeedDefaultConfigurationsAsync_WhenConfigurationExists_SetsLastModifiedByToSystem`
(lines 157–183) has a fixture where ALL seeded fields already match metadata (`DisplayName =
"Purchase Price Recalculation"`, `Description = "Recalculates purchase prices for all materials and
products"`, `TimeZoneId = "Europe/Prague"` matching `RecurringJobMetadata.DefaultTimeZoneId`,
`CronExpression = "0 2 * * *"`) and only `LastModifiedBy` starts as `"Admin"` instead of `"System"`.
It currently asserts `LastModifiedBy` becomes `"System"` — that assertion encodes the bug (a no-op
seed pass overwriting an admin's `LastModifiedBy`) and must be corrected to assert the field is now
preserved. Replace the whole test method:

Find:
```csharp
    [Fact]
    public async Task SeedDefaultConfigurationsAsync_WhenConfigurationExists_SetsLastModifiedByToSystem()
    {
        // Arrange - add an existing configuration whose last modification was made by an admin
        var existingConfig = new RecurringJobConfiguration(
            "purchase-price-recalculation",
            "Purchase Price Recalculation",
            "Recalculates purchase prices for all materials and products",
            "0 2 * * *",
            "Europe/Prague",
            true,
            "Admin",
            DateTime.UtcNow);

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

Replace with:
```csharp
    [Fact]
    public async Task SeedDefaultConfigurationsAsync_WhenConfigurationExists_AndSeededFieldsUnchanged_PreservesLastModifiedBy()
    {
        // Arrange - add an existing configuration whose last modification was made by an admin,
        // and whose seeded fields (DisplayName/Description/TimeZoneId) already match metadata.
        var existingConfig = new RecurringJobConfiguration(
            "purchase-price-recalculation",
            "Purchase Price Recalculation",
            "Recalculates purchase prices for all materials and products",
            "0 2 * * *",
            "Europe/Prague",
            true,
            "Admin",
            DateTime.UtcNow);

        await _context.RecurringJobConfigurations.AddAsync(existingConfig);
        await _context.SaveChangesAsync();

        var mockJobs = CreateMockJobs();

        // Act
        await _seeder.SeedDefaultConfigurationsAsync(mockJobs);

        // Assert - nothing seeded differs, so LastModifiedBy must stay "Admin", not flip to "System"
        var updated = await _repository.GetByJobNameAsync("purchase-price-recalculation");
        Assert.NotNull(updated);
        Assert.Equal("Admin", updated!.LastModifiedBy);
    }
```

(Renamed to `..._AndSeededFieldsUnchanged_PreservesLastModifiedBy` so the name reflects the corrected,
intended behavior instead of the old bug.)

- [ ] **Step 4: Run both edited/added tests to confirm they still/now fail correctly**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobSeederTests"
```
Expected: the renamed test
`SeedDefaultConfigurationsAsync_WhenConfigurationExists_AndSeededFieldsUnchanged_PreservesLastModifiedBy`
and the new
`SeedDefaultConfigurationsAsync_WhenNothingChanged_PreservesLastModifiedAtAndBy` both **FAIL** (same
reason as Step 2 — the production code hasn't changed yet). The other existing tests in the file
still **PASS** (they aren't touched yet and don't depend on the fix).

- [ ] **Step 5: Implement the fix in `RecurringJobSeeder.cs`**

Modify `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`.

Find the `foreach` loop (lines 41–59):
```csharp
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
                    config.TimeZoneId,
                    "System",
                    now);
                await _repository.UpdateAsync(existing, cancellationToken);
            }
        }
    }
}
```

Replace with:
```csharp
        foreach (var config in defaultConfigurations)
        {
            var existing = await _repository.GetByJobNameAsync(config.JobName, cancellationToken);
            if (existing == null)
            {
                await _repository.AddAsync(config, cancellationToken);
            }
            else if (HasSeededFieldsChanged(existing, config))
            {
                existing.UpdateConfiguration(
                    config.DisplayName,
                    config.Description,
                    existing.CronExpression,   // preserve admin override
                    config.TimeZoneId,
                    "System",
                    now);
                await _repository.UpdateAsync(existing, cancellationToken);
            }
            // else: no developer-owned field changed - leave the row (including
            // LastModifiedAt/LastModifiedBy, which record admin actions) untouched.
        }
    }

    /// <summary>
    /// Compares only the developer-owned, code-sourced fields (DisplayName, Description,
    /// TimeZoneId) between the stored row and the freshly computed metadata. CronExpression
    /// and IsEnabled are intentionally excluded - they are admin-owned and must never trigger
    /// a seeder-initiated write.
    /// </summary>
    private static bool HasSeededFieldsChanged(RecurringJobConfiguration existing, RecurringJobConfiguration config) =>
        existing.DisplayName != config.DisplayName
        || existing.Description != config.Description
        || existing.TimeZoneId != config.TimeZoneId;
}
```

Note the added `else if (HasSeededFieldsChanged(existing, config))` (replacing the bare `else`), the
new comment on the no-op case, and the new private static method placed after the closing brace of
`SeedDefaultConfigurationsAsync` but before the class's final closing brace.

- [ ] **Step 6: Run the full seeder test file to verify all tests pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobSeederTests"
```
Expected: **PASS** — all tests in `RecurringJobSeederTests`, including:
- `SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations`
- `SeedDefaultConfigurationsAsync_WhenConfigurationsExist_DoesNotDuplicate`
- `SeedDefaultConfigurationsAsync_WhenConfigurationExists_UpdatesDisplayNameAndDescription`
- `SeedDefaultConfigurationsAsync_WhenConfigurationExists_PreservesCronExpressionAndIsEnabled`
- `SeedDefaultConfigurationsAsync_WhenConfigurationExists_AndSeededFieldsUnchanged_PreservesLastModifiedBy` (renamed in Step 3)
- `SeedDefaultConfigurationsAsync_WhenConfigurationExists_ResyncsTimeZoneIdFromMetadata`
- `SeedDefaultConfigurationsAsync_WhenNothingChanged_PreservesLastModifiedAtAndBy` (added in Step 1)

If `SeedDefaultConfigurationsAsync_WhenConfigurationExists_PreservesCronExpressionAndIsEnabled` fails:
its fixture has an admin-customized `CronExpression` ("0 0 * * *" vs mock's "0 2 * * *") and
`IsEnabled: false`, with `DisplayName`/`Description`/`TimeZoneId` all matching metadata — so
`HasSeededFieldsChanged` returns `false` and the row is now NOT updated at all. `CronExpression` and
`IsEnabled` were already being preserved before this change (nothing in `UpdateConfiguration`'s
argument list ever touched `IsEnabled`, and `existing.CronExpression` was already passed back
unchanged) so the assertions `Assert.Equal("0 0 * * *", updated!.CronExpression)` and
`Assert.False(updated.IsEnabled)` continue to hold whether or not the row was written — this test
should pass unmodified.

- [ ] **Step 7: Run the full BackgroundJobs test suite**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.BackgroundJobs"
```
Expected: **PASS** — no other test in the `BackgroundJobs` feature area depends on
`RecurringJobSeeder`'s internals (`RecurringJobConfigurationTests`,
`RecurringJobConfigurationRepositoryTests`, `UpdateRecurringJobCronHandlerTests`,
`UpdateRecurringJobStatusHandlerTests`, etc. exercise other classes and are unaffected).

- [ ] **Step 8: Build the full solution**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 9: Run `dotnet format` and re-verify build**

Run:
```bash
dotnet format backend/Anela.Heblo.sln
dotnet build backend/Anela.Heblo.sln
```
Expected: formatting applies cleanly (or reports no changes needed) and the build still succeeds
with 0 errors.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs
git commit -m "fix(backgroundjobs): stop RecurringJobSeeder from overwriting audit fields when nothing changed

RecurringJobSeeder.SeedDefaultConfigurationsAsync unconditionally called
UpdateConfiguration on every existing job row on every restart, which always
stamps LastModifiedAt/LastModifiedBy - silently overwriting an admin's real
CRON/enable-disable audit trail even when no developer-owned field
(DisplayName, Description, TimeZoneId) actually changed. Add
HasSeededFieldsChanged(existing, config) and only call UpdateConfiguration /
UpdateAsync when at least one of those three fields differs.

Fixes #4318"
```

**Verification for this task**

- [ ] `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobSeederTests"` passes (7 tests: the 5 pre-existing ones untouched, 1 renamed/corrected, 1 new).
- [ ] `dotnet build backend/Anela.Heblo.sln` succeeds with 0 errors.
- [ ] `dotnet format backend/Anela.Heblo.sln` reports no unformatted files (or applies cleanly).
- [ ] Manual review: `RecurringJobSeeder.cs`'s `existing == null` branch (`AddAsync`) is byte-for-byte unchanged; only the `else` branch and the new private method were touched.
