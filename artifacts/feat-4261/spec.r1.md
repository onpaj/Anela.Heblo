# Specification: Remove redundant `JobName` column from `RecurringJobConfiguration`

## Summary
`RecurringJobConfiguration` currently persists the same string value twice: once as the EF `Id` primary key and once as a separately-mapped `JobName` column, which also carries a redundant unique index. This spec removes the duplicate column and index, keeping `JobName` only as a non-persisted convenience accessor for `Id`, and updates the one query that filters on it.

## Background
`RecurringJobConfiguration` inherits `Entity<string>` (which supplies `Id`) and additionally declares a `JobName` property. Both are set to the same value in the constructor (`Id = jobName; JobName = jobName;`). The EF Core configuration in `RecurringJobConfigurationConfiguration.cs` maps `JobName` as its own column and adds a unique index on it, even though the primary key on `Id` already enforces uniqueness over an identical value. This is a pure arch-review finding (issue #4261): no user-facing behavior is wrong, but the schema carries redundant storage, a redundant index maintained on every write, and a latent DRY risk (a future change to one property silently diverging from the other). `GetByJobNameAsync` currently filters on `c.JobName`; after this change it will filter on `c.Id`.

## Functional Requirements

### FR-1: `JobName` is no longer an EF-mapped column
`RecurringJobConfiguration.JobName` must stop being persisted as its own table column. It remains available as a property on the entity (either `[NotMapped]` or a computed property reading `Id`), so existing in-memory/domain code that reads `entity.JobName` continues to compile and behave identically.

**Acceptance criteria:**
- `RecurringJobConfigurationConfiguration.cs` no longer calls `builder.Property(e => e.JobName)...`.
- `RecurringJobConfiguration.JobName` returns the same value as `Id` for every instance (compile-time or runtime guarantee, e.g. `public string JobName => Id;` or `[NotMapped] public string JobName { get; }`).
- No other entity/DTO in the codebase that maps `RecurringJobConfiguration` to a database row references a `JobName` column.

### FR-2: Redundant unique index is dropped
The `IX_RecurringJobConfigurations_JobName` unique index must be removed from the EF configuration and from the database via a migration.

**Acceptance criteria:**
- `RecurringJobConfigurationConfiguration.cs` no longer calls `builder.HasIndex(e => e.JobName).IsUnique()`.
- A new EF Core migration is added that drops the `JobName` column and its unique index from the `RecurringJobConfigurations` table.
- The migration's `Down()` method restores the column and index (standard EF migration reversibility), consistent with existing migrations in the same folder.

### FR-3: Repository queries updated to use `Id`
`RecurringJobConfigurationRepository` (`backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs`) has two LINQ-to-Entities query call sites that reference `JobName` and translate to SQL — both must be changed to reference `Id` instead, since `Id` is the persisted primary key and always equals the job name by invariant:
- `GetByJobNameAsync`: `.FirstOrDefaultAsync(c => c.JobName == jobName, ...)` → `.FirstOrDefaultAsync(c => c.Id == jobName, ...)`.
- `GetAllAsync`: `.OrderBy(c => c.JobName)` → `.OrderBy(c => c.Id)`.

**Acceptance criteria:**
- Both call sites above filter/order on `Id`, not `JobName`.
- `GetByJobNameAsync` and `GetAllAsync` return the same results (matching rows, same order) before and after the change for existing data (verified via existing/updated repository tests in `RecurringJobConfigurationRepositoryTests.cs`).
- No remaining LINQ-to-Entities query in the codebase references the removed `JobName` column (a query referencing a `[NotMapped]`/unmapped property would throw an EF Core translation exception at runtime, so this is a correctness requirement, not just style). In-memory (non-query) reads of `.JobName`, e.g. in `RecurringJobSeeder.cs` (`config.JobName`) and the AutoMapper projection in `BackgroundJobsMappingProfile.cs` (`RecurringJobConfiguration` → `RecurringJobDto`), are unaffected and require no code change since they operate on already-materialized entities.

## Non-Functional Requirements

### NFR-1: Data integrity across the migration
The migration must not lose data or break existing `RecurringJobConfiguration` rows. Since `JobName` always equals `Id` by invariant (enforced by the constructor), dropping the column loses no information — `Id` already holds every value `JobName` held.

### NFR-2: Backward compatibility of in-memory API
Any code elsewhere in the solution that constructs or reads `RecurringJobConfiguration.JobName` outside of EF query expressions (e.g., application/domain logic, Hangfire job registration code) must continue to compile and return the same value without modification, unless that code specifically queried the database column (covered by FR-3).

## Data Model
- **Entity:** `RecurringJobConfiguration : Entity<string>` (table `RecurringJobConfigurations`).
  - `Id` (string, PK) — unchanged, remains the persisted identifier equal to the job name.
  - `JobName` (string) — changes from a separately-mapped, indexed column to a non-mapped derived property (`JobName == Id`).
- **Index removed:** `IX_RecurringJobConfigurations_JobName` (unique index on the now-unmapped `JobName` column).
- **Migration:** one new EF Core migration in `backend/src/Anela.Heblo.Persistence` (or the project's standard migrations location) that:
  - Drops the `JobName` column from `RecurringJobConfigurations`.
  - Drops the `IX_RecurringJobConfigurations_JobName` index (dropping the column may drop the index automatically depending on provider, but the migration should be explicit/verified either way).

## API / Interface Design
No public API surface (controllers, DTOs, OpenAPI contracts) changes. This is an internal persistence-layer and repository-layer change only. `RecurringJobConfiguration.JobName` keeps its existing C# signature (`string JobName { get; }` or `{ get; set; }`, per FR-1's chosen implementation) so any internal caller reading it is unaffected.

## Dependencies
- EF Core migrations tooling (`dotnet ef migrations add`), consistent with how other migrations in `backend/src/Anela.Heblo.Persistence` are generated and applied.
- Per `CLAUDE.md`: database migrations in this project are applied manually (not automated in deployment), so this migration will need manual application to each environment (dev/staging/prod) after merge — this is an operational follow-up, not part of the code change itself, but should be called out in the PR description.

## Out of Scope
- The more thorough alternative mentioned in the brief (not inheriting `Entity<string>` and instead using a conventionally-named `[Key] public string JobName` primary key directly) is **out of scope** for this fix. It would be a larger, more invasive rename touching every reference to `.Id` on this entity across the codebase, for a purely cosmetic naming improvement. This spec implements the brief's primary suggested fix (keep `Id` as PK, drop the redundant `JobName` column/index).
- No changes to `RecurringJobConfiguration`'s other properties (e.g., cron expression, enabled flag, etc.) or to Hangfire job scheduling behavior.
- No changes to any other entity or module.

## Open Questions
None.

## Status: COMPLETE
