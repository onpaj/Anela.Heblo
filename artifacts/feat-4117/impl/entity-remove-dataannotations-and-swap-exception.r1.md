# Implementation: entity-remove-dataannotations-and-swap-exception

## What was implemented
Removed the `System.ComponentModel.DataAnnotations` coupling from `RecurringJobConfiguration`:
- Dropped the now-unused `using System.ComponentModel.DataAnnotations;` line.
- Removed the `[Required]`/`[MaxLength(...)]` attributes from the six decorated
  properties (`JobName`, `DisplayName`, `Description`, `CronExpression`,
  `TimeZoneId`, `LastModifiedBy`) — the equivalent constraints (same max lengths)
  are already enforced by the Fluent API configuration in
  `RecurringJobConfigurationConfiguration`, which was not touched.
- Replaced all 15 `throw new ValidationException(...)` guard-clause throws with
  `throw new ArgumentException(...)`, preserving every message string verbatim.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs` — attribute removal, using removal, exception type swap.

## Tests
None written in this unit — a separate task (`tests-update-for-argumentexception`) covers updating the existing test suite to expect `ArgumentException` instead of `ValidationException`.

## How to verify
- `grep -n "ValidationException\|DataAnnotations" backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs` — no matches (confirmed, exit 1).
- `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj` — Build succeeded, 0 Error(s) (86 pre-existing warnings unrelated to this file).
- `dotnet format backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj --verify-no-changes` — exit 0, no formatting diffs.

## Notes
- Step 7 (optional `dotnet ef migrations has-pending-model-changes` sanity check) was skipped: the `dotnet-ef` global tool is not installed in this environment. This is a non-gating confirmatory check per the task context; `RecurringJobConfigurationConfiguration.cs` was not edited and the Fluent API max-lengths already match the removed attributes exactly (100/200/500/50/100/100).
- No deviations from the task context's prescribed edits.

## PR Summary
Removed the `System.ComponentModel.DataAnnotations` dependency from the `RecurringJobConfiguration` domain entity: dropped redundant `[Required]`/`[MaxLength]` attributes (already enforced identically by the Fluent API configuration) and replaced all 15 `ValidationException` guard-clause throws with `ArgumentException`, so the Domain entity no longer references `System.ComponentModel.DataAnnotations`.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs` — removed DataAnnotations attributes/using, swapped `ValidationException` for `ArgumentException` in all 15 guard clauses

## Status
DONE
