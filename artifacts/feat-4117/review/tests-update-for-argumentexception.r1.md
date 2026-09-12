# Code Review: tests-update-for-argumentexception

## Summary
The implementation matches the task context exactly: the `System.ComponentModel.DataAnnotations`
using directive was removed, all 6 `Assert.Throws<ValidationException>` call sites were swapped
to `Assert.Throws<ArgumentException>`, and the 6 corresponding test methods were renamed from
`*ShouldThrowValidationException*` to `*ShouldThrowArgumentException*`. A repo-wide grep confirms
`RecurringJobConfigurationTests.cs` no longer references `ValidationException` or `DataAnnotations`,
and no other file in the `BackgroundJobs` feature area references the old exception type for this
entity.

## Verification performed
- `grep -n "ValidationException\|DataAnnotations" .../RecurringJobConfigurationTests.cs` — no matches.
- `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — Build succeeded, 0 Error(s).
- `dotnet test .../Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobConfigurationTests"` — 12/12 passed.
- `dotnet format .../Anela.Heblo.Tests.csproj --verify-no-changes` — exit 0, no diffs.
- `dotnet build Anela.Heblo.sln` — Build succeeded, 0 Error(s), repo-wide.
- `dotnet test .../Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BackgroundJobs"` — 98/98 passed.

## Review Result: PASS

### task: tests-update-for-argumentexception
**Status:** PASS

## Docs to Update
(none — test-only change, no public behavior or documented API surface changed)

## Overall Notes
This was the last pending developer task for feat-4117 (`entity-remove-dataannotations-and-swap-exception`
already passed review). Both FR-1/FR-2/FR-3 from the spec and the arch-review's method-rename addendum
are now fully covered. Step 1 of the task context (confirm red state before editing) could not be cleanly
captured as a standalone artifact here because a background pre-edit test run was killed by OOM (`csc.dll`
exit 137) under heavy contention from other parallel workers sharing this sandbox — non-blocking, since the
type mismatch is a well-understood runtime assertion failure (not a compile error) and the post-edit green
run above is a clean, reproducible, from-scratch build+test pass that fully accounts for correctness.
