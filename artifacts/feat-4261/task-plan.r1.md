# Remove Redundant JobName Column Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop persisting `RecurringJobConfiguration.JobName` as its own EF-mapped column and unique index — collapse it into a computed property backed by `Id` — and update the repository queries and migration history to match.

**Architecture:** `JobName` becomes `public string JobName => Id;` on the domain entity (no backing field, no independent assignment), the EF Core entity configuration drops the `JobName` property mapping and its unique index, the repository's two LINQ queries that referenced `JobName` switch to `Id`, and a new EF Core migration drops the now-unused `JobName` column and `IX_RecurringJobConfigurations_JobName` index from the database.

**Tech Stack:** .NET 8, EF Core (PostgreSQL, Npgsql provider), xUnit with the EF Core InMemory provider for repository tests.

---

## File Structure

This is a small, tightly-coupled cleanup — no new files except one generated migration pair.

- **Modify** `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs` — collapse `JobName` into a computed property (`=> Id`), remove its backing field and constructor assignment.
- **Modify** `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs` — remove the `JobName` property mapping and its unique index.
- **Modify** `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs` — `GetByJobNameAsync` and `GetAllAsync` reference `Id` instead of `JobName` in their LINQ expressions.
- **Modify** `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationRepositoryTests.cs` — add one new test asserting `JobName` is no longer part of the EF Core model (i.e., not mapped to a database column).
- **Create** `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_DropRedundantJobNameColumn.cs` and `.Designer.cs` — EF-tool-generated migration dropping the `JobName` column and its unique index.
- **Modify (auto-generated)** `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` — updated by `dotnet ef migrations add`; never hand-edit.

The entity, EF configuration, and repository changes must land in a single task/commit: EF Core validates the model (including explicit `builder.Property(e => e.JobName)` calls) the first time any `DbContext` is used, so making `JobName` computed without also removing its EF mapping and query references would break every test and the app itself at runtime, not just at compile time. Splitting these three files across separate commits would leave the tree in a broken, untestable state between commits, which the writing-plans skill's "each task produces working, testable software" rule disallows — so Task 1 below bundles all three.

---

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

### task: generate-and-verify-migration

**Generate the EF Core migration that drops the `JobName` column and its unique index, and verify `Down()` is correct**

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_DropRedundantJobNameColumn.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_DropRedundantJobNameColumn.Designer.cs`
- Modify (auto-generated): `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`

- [ ] **Step 1: Generate the migration from the updated model**

Run:
```bash
dotnet ef migrations add DropRedundantJobNameColumn \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```
Expected: Command succeeds and prints something like `Done. To undo this action, use 'ef migrations remove'`, and creates the two new migration files plus updates `ApplicationDbContextModelSnapshot.cs`.

- [ ] **Step 2: Read the generated `Up()` method and confirm it drops both the column and the index**

Open the newly created `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_DropRedundantJobNameColumn.cs`. `Up()` must contain both:
```csharp
migrationBuilder.DropIndex(
    name: "IX_RecurringJobConfigurations_JobName",
    schema: "public",
    table: "RecurringJobConfigurations");

migrationBuilder.DropColumn(
    name: "JobName",
    schema: "public",
    table: "RecurringJobConfigurations");
```
(order may vary — EF typically drops the index before the column). If either statement is missing, add it manually in the matching style shown above, matching the schema (`"public"`) and table name (`"RecurringJobConfigurations"`) used by every other migration touching this table (see `backend/src/Anela.Heblo.Persistence/Migrations/20260718074122_AddTimeZoneIdToRecurringJobConfigurations.cs` for the established style).

- [ ] **Step 3: Read the generated `Down()` method and confirm it restores both the column and the unique index**

`Down()` must contain both a `migrationBuilder.AddColumn<string>(name: "JobName", schema: "public", table: "RecurringJobConfigurations", type: "character varying(100)", maxLength: 100, nullable: false, ...)` call and a `migrationBuilder.CreateIndex(name: "IX_RecurringJobConfigurations_JobName", schema: "public", table: "RecurringJobConfigurations", column: "JobName", unique: true)` call. If `unique: true` or the index name is missing from the generated `Down()`, fix it by hand — a `Down()` that restores the column but not its uniqueness silently changes the schema on rollback, which is worse than no rollback at all.

- [ ] **Step 4: Build the solution to confirm the migration compiles and the model snapshot is consistent**

Run: `dotnet build`
Expected: Build succeeds with 0 errors, including the new migration files and the updated `ApplicationDbContextModelSnapshot.cs`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(background-jobs): add migration dropping redundant JobName column and unique index"
```

---

### task: full-verification

**Run the project's full backend validation suite before declaring the change done**

**Files:** none (verification only, per CLAUDE.md's "Validation before completion" checklist)

- [ ] **Step 1: Full backend build**

Run: `dotnet build`
Expected: Build succeeds with 0 errors, 0 new warnings introduced by this change.

- [ ] **Step 2: Format check**

Run: `dotnet format --verify-no-changes`
Expected: No formatting violations. If it reports violations, run `dotnet format` (without `--verify-no-changes`) to fix them, then re-run the verify command and re-commit the formatting fix.

- [ ] **Step 3: Run the full backend test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: All tests PASS, including every test in `RecurringJobConfigurationTests.cs` and `RecurringJobConfigurationRepositoryTests.cs` touched by this change, and no unrelated test regresses.

- [ ] **Step 4: Confirm no other reference to the removed EF mapping remains**

Run: `grep -rn "JobName" backend/src --include=*.cs | grep -v Migrations`
Expected output: only in-memory (non-query) references remain — `RecurringJobConfiguration.cs`'s computed property definition, `IRecurringJobConfigurationRepository.cs`'s `GetByJobNameAsync` parameter name, `RecurringJobSeeder.cs`'s `config.JobName` read, and unrelated types (`RecurringJobMetadata.JobName`, `BackgroundJobInfo.JobName`, job-specific `Metadata.JobName` usages in `Anela.Heblo.Adapters.*`) that are out of scope for this issue. No LINQ query against `_context.RecurringJobConfigurations` should reference `JobName`.

- [ ] **Step 5: Note the manual migration-apply step in the PR description**

Per `CLAUDE.md` ("Database migrations are manual, not automated in deployment" for Development/Test/Staging) and `docs/architecture/infrastructure.md` §5 (migrations auto-apply on Production startup), add a line to the PR description noting: "This PR includes an EF Core migration (`DropRedundantJobNameColumn`) that drops a column and a unique index. It applies automatically on Production startup; for Staging/Test/local, run `dotnet ef database update --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API` manually after deploy." No code change for this step — it is a documentation/communication step for whoever reviews or deploys the PR.

No commit for this task — it is a verification pass over the commits made in the previous two tasks. If any step fails, fix the issue in the relevant file from Task 1 or Task 2, re-run the failing step, and amend that earlier task's commit only if instructed to do so by the execution process in use; otherwise add a small follow-up commit.
