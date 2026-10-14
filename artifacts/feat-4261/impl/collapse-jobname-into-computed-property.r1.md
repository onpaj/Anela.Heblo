# Implementation: collapse-jobname-into-computed-property

## What was implemented
Collapsed the redundant `JobName` column on `RecurringJobConfiguration` into a
computed, read-only property backed by `Id` (`JobName => Id`), removed its
explicit EF Core column mapping and unique index, and updated the repository's
two queries to filter/order by `Id` instead of `JobName`. Added a regression
test asserting the EF Core model no longer maps `JobName` as a column.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs` — replaced the `JobName` auto-property with `public string JobName => Id;`; removed the now-invalid `JobName = string.Empty;` assignment in the private EF constructor and the `JobName = jobName;` assignment in the public constructor (kept `Id = jobName;`)
- `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs` — removed the `builder.Property(e => e.JobName)...` mapping and the `IX_RecurringJobConfigurations_JobName` unique index; left `HasKey(e => e.Id)`, `Property(e => e.Id)`, and the `IsEnabled` index untouched
- `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs` — `GetAllAsync` now orders by `c.Id`; `GetByJobNameAsync` now filters by `c.Id == jobName` (method name/signature unchanged)
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationRepositoryTests.cs` — added `EfCoreModel_ShouldNotMapJobNameAsColumn`, asserting `FindEntityType(...).FindProperty(nameof(RecurringJobConfiguration.JobName))` is `null`

## Tests
- `RecurringJobConfigurationRepositoryTests.EfCoreModel_ShouldNotMapJobNameAsColumn` (new) — confirms `JobName` is no longer EF-mapped
- All pre-existing tests in `RecurringJobConfigurationTests.cs` and `RecurringJobConfigurationRepositoryTests.cs` continue to pass unchanged (they already assert `config.JobName == config.Id`), serving as characterization tests for this refactor

## How to verify
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BackgroundJobs"
dotnet build
```
Both were run: full `BackgroundJobs` filter — 115/115 passed, 0 failed; solution build — 0 errors (91 pre-existing nullable-reference warnings unrelated to this change).

## Notes
No migration was generated in this task — that is the scope of the separate
`generate-and-verify-migration` task later in this plan. Checked all other
call sites in `backend/src` for `.JobName` usage (`RecurringJobSeeder.cs` and
`BackgroundJobsMappingProfile.cs` per the task's Step 8 hint); both only read
`JobName`, which compiles unchanged against the computed property. No writes
to `JobName` exist anywhere else in the codebase (it was already a
private-setter property before this change, so it was never settable outside
the entity).

## PR Summary
Collapsed the redundant `JobName` column on `RecurringJobConfiguration` into
a computed property derived from the entity's `Id` (its primary key), since
the two were always kept equal. Removed the now-unnecessary explicit column
mapping and duplicate unique index from the EF Core configuration, and
updated the repository's queries to filter/order on `Id` directly.

### Changes
- `RecurringJobConfiguration.cs` — `JobName` is now `Id`-derived, not a separate mapped field
- `RecurringJobConfigurationConfiguration.cs` — dropped `JobName` column mapping and its unique index
- `RecurringJobConfigurationRepository.cs` — queries now reference `Id` instead of `JobName`
- `RecurringJobConfigurationRepositoryTests.cs` — new test guarding against `JobName` re-appearing as a mapped column

## Status
DONE
