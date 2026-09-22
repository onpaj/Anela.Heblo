### task: collapse-jobname-into-computed-property

**Collapse `JobName` into a computed `Id`-backed property across entity, EF config, and repository**

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs`
- Test (new): `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationRepositoryTests.cs`

- [ ] **Step 1: Confirm the existing test suite is green before touching anything**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BackgroundJobs.RecurringJobConfiguration"`
Expected: All existing tests in `RecurringJobConfigurationTests.cs` and `RecurringJobConfigurationRepositoryTests.cs` PASS. This is the baseline you must not regress — they already assert `config.JobName == config.Id` (see `RecurringJobConfigurationTests.cs:24-25`), so they double as characterization tests for this refactor.

- [ ] **Step 2: Write the new regression test asserting `JobName` is no longer an EF-mapped column**

Add this test to `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationRepositoryTests.cs`, inside the `RecurringJobConfigurationRepositoryTests` class (e.g. after `GetAllAsync_WhenNoConfigurations_ReturnsEmptyList`):

```csharp
[Fact]
public void EfCoreModel_ShouldNotMapJobNameAsColumn()
{
    // Assert
    var entityType = _context.Model.FindEntityType(typeof(RecurringJobConfiguration));
    Assert.NotNull(entityType);
    Assert.Null(entityType.FindProperty(nameof(RecurringJobConfiguration.JobName)));
}
```

- [ ] **Step 3: Run the new test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~EfCoreModel_ShouldNotMapJobNameAsColumn"`
Expected: FAIL — `entityType.FindProperty(nameof(RecurringJobConfiguration.JobName))` currently returns a non-null `IProperty`, because `JobName` is still explicitly mapped in `RecurringJobConfigurationConfiguration.cs`.

- [ ] **Step 4: Collapse `JobName` into a computed property on the entity**

In `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`, replace the `JobName` auto-property and its two assignments with a computed, read-only property:

Replace:
```csharp
public string JobName { get; private set; }
```
with:
```csharp
public string JobName => Id;
```

Remove the line `JobName = string.Empty;` from the private parameterless constructor (around line 26) — it no longer compiles against a get-only computed property, and it is no longer needed since `JobName` derives from `Id`.

Remove the line `JobName = jobName;` from the public constructor (around line 58), keeping only:
```csharp
Id = jobName; // JobName is the primary key
```

- [ ] **Step 5: Remove the `JobName` column mapping and its unique index from the EF configuration**

In `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs`, delete this block entirely:
```csharp
        builder.Property(e => e.JobName)
            .HasMaxLength(100)
            .IsRequired();
```
and this block entirely:
```csharp
        // Create index on JobName for efficient lookups
        builder.HasIndex(e => e.JobName)
            .IsUnique()
            .HasDatabaseName("IX_RecurringJobConfigurations_JobName");
```
Leave `builder.HasKey(e => e.Id)`, `builder.Property(e => e.Id)...`, and the `IX_RecurringJobConfigurations_IsEnabled` index untouched.

- [ ] **Step 6: Update the repository's two queries to reference `Id` instead of `JobName`**

In `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs`, change:
```csharp
    public async Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.RecurringJobConfigurations
            .OrderBy(c => c.JobName)
            .ToListAsync(cancellationToken);
    }

    public async Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default)
    {
        return await _context.RecurringJobConfigurations
            .FirstOrDefaultAsync(c => c.JobName == jobName, cancellationToken);
    }
```
to:
```csharp
    public async Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.RecurringJobConfigurations
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default)
    {
        return await _context.RecurringJobConfigurations
            .FirstOrDefaultAsync(c => c.Id == jobName, cancellationToken);
    }
```
The method name and parameter stay `GetByJobNameAsync(string jobName, ...)` — only the query predicate's column reference changes.

- [ ] **Step 7: Run the full BackgroundJobs test suite and verify everything passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BackgroundJobs"`
Expected: PASS — all pre-existing tests from Step 1 still pass unchanged (characterization preserved), and the new `EfCoreModel_ShouldNotMapJobNameAsColumn` test from Step 2 now passes.

- [ ] **Step 8: Build the whole solution to catch any other reference to the old mapping**

Run: `dotnet build`
Expected: Build succeeds with 0 errors. (`RecurringJobSeeder.cs`'s `config.JobName` and `BackgroundJobsMappingProfile.cs`'s AutoMapper convention mapping both read the entity in-memory, so they compile unchanged against the new computed property — this step confirms that.)

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationRepositoryTests.cs
git commit -m "refactor(background-jobs): collapse redundant JobName column into computed Id-backed property"
```

---

