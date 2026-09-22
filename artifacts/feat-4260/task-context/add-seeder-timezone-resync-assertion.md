### task: add-seeder-timezone-resync-assertion


**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs`

Rationale (arch-review amendment A-3): with the adapter now forwarding whatever it is given, the guarantee that the runtime CRON-edit path applies the *same* time zone as the startup discovery path rests entirely on `RecurringJobSeeder` re-syncing each existing row's `TimeZoneId` from `IRecurringJob.Metadata.TimeZoneId` on every boot. `RecurringJobSeederTests` currently asserts that `TimeZoneId` is seeded correctly for **new** rows (lines 55–60) but never asserts the re-sync for an **existing** row. Nothing else in the suite guards that link.

- [ ] **Step 1: Add the re-sync test**

Insert this test into `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs` immediately after `SeedDefaultConfigurationsAsync_WhenConfigurationExists_SetsLastModifiedByToSystem` (currently ends at line 183) and before `public void Dispose()` (currently line 185).

The `CreateMockJobs()` helper at the bottom of the file already builds `invoice-classification` with `timeZoneId: "America/New_York"` (line 200), while every other mock job uses `RecurringJobMetadata.DefaultTimeZoneId` (`"Europe/Prague"`). That makes `invoice-classification` the right job for this test: the arranged row starts on `"Europe/Prague"` and must end on `"America/New_York"`.

No new `using` directive is needed — `Anela.Heblo.Domain.Features.BackgroundJobs` and `Xunit` are already imported at the top of the file.

```csharp
    [Fact]
    public async Task SeedDefaultConfigurationsAsync_WhenConfigurationExists_ResyncsTimeZoneIdFromMetadata()
    {
        // Arrange - existing row carries a stale TimeZoneId that no longer matches the code metadata
        // ("invoice-classification" metadata says "America/New_York")
        var existingConfig = new RecurringJobConfiguration(
            "invoice-classification",
            "Invoice Classification",
            "Classifies and categorizes incoming invoices",
            "0 * * * *",
            "Europe/Prague", // stale - developer-owned field, must be overwritten from metadata
            true,
            "System",
            DateTime.UtcNow);

        await _context.RecurringJobConfigurations.AddAsync(existingConfig);
        await _context.SaveChangesAsync();

        var mockJobs = CreateMockJobs();

        // Act
        await _seeder.SeedDefaultConfigurationsAsync(mockJobs);

        // Assert - TimeZoneId is developer-owned and re-synced from metadata on every seed run.
        // This invariant is what makes the runtime CRON-update path (which reads TimeZoneId from
        // the DB row) equivalent to the startup discovery path (which reads it from metadata).
        var updated = await _repository.GetByJobNameAsync("invoice-classification");
        Assert.NotNull(updated);
        Assert.Equal("America/New_York", updated!.TimeZoneId);
    }
```

- [ ] **Step 2: Build**

Run: `cd backend && dotnet build`

Expected: `Build succeeded.` with 0 errors and no new warnings.

- [ ] **Step 3: Run the seeder tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RecurringJobSeederTests"`

Expected: `Passed!` with 0 failed and 6 tests run (5 pre-existing + the new one).

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs
git commit -m "test(background-jobs): assert RecurringJobSeeder re-syncs an existing row's TimeZoneId from metadata"
```
