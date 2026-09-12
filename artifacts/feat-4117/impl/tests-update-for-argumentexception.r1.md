# Implementation: tests-update-for-argumentexception

## What was implemented
Updated `RecurringJobConfigurationTests` to match the entity's new exception type
(`ArgumentException` instead of `System.ComponentModel.DataAnnotations.ValidationException`,
swapped in the companion task `entity-remove-dataannotations-and-swap-exception`):
- Removed the now-unused `using System.ComponentModel.DataAnnotations;` line.
- Swapped all 6 `Assert.Throws<ValidationException>` call sites to
  `Assert.Throws<ArgumentException>`.
- Renamed the 6 affected test methods from `*ShouldThrowValidationException*` to
  `*ShouldThrowArgumentException*` so names match what they assert.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs` — using removal, assertion type swap, method renames.

## Tests
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobConfigurationTests"` — `Passed! - Failed: 0, Passed: 12, Skipped: 0, Total: 12`.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BackgroundJobs"` — `Passed! - Failed: 0, Passed: 98, Skipped: 0, Total: 98` (full BackgroundJobs slice, no other test referenced the old type).

## How to verify
- `grep -n "ValidationException\|DataAnnotations" backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs` — no matches (confirmed, exit 1).
- `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — Build succeeded, 0 Error(s).
- `dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --verify-no-changes` — exit 0, no formatting diffs.
- `dotnet build Anela.Heblo.sln` — Build succeeded, 0 Error(s) (repo-wide, confirms no other file referenced the old type/attributes in a way that broke compilation).

## Notes
- Step 1's "confirm red before edit" was attempted via a background test run launched
  before the edits, but heavy CPU/memory contention from other parallel workers on this
  shared machine caused that particular run's `csc.dll` to be OOM-killed (exit 137)
  before it could complete — so it produced no usable red/green signal either way. The
  type mismatch between `Assert.Throws<ValidationException>` and the already-`ArgumentException`-throwing
  entity (from the completed, reviewed `entity-remove-dataannotations-and-swap-exception`
  task) is a compile-time-safe runtime assertion mismatch, not a compile error, so this
  is a documentation-only step; it does not change the correctness of the after-edit
  green run above, which is a clean, reproducible pass on a fully rebuilt project.
- No deviations from the task context's prescribed edits (Steps 2–4 applied exactly as
  specified; Steps 5–9 all confirmed green above).

## PR Summary
Updated `RecurringJobConfigurationTests` to assert `ArgumentException` (matching the
`RecurringJobConfiguration` entity's new exception type) instead of the removed
`System.ComponentModel.DataAnnotations.ValidationException`, renamed the 6 affected test
methods to match, and dropped the now-unused `DataAnnotations` using directive.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationTests.cs` — assertion type swap (6 sites), method renames (6 methods), using removal

## Status
DONE
