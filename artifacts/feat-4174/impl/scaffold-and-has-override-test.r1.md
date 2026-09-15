# Implementation: scaffold-and-has-override-test

## What was implemented
Created the `ListFlagsHandlerTests` test class scaffold (Moq mocks for
`IFeatureFlagOverrideRepository` and `IFeatureFlagChecker`, a `CreateHandler`
helper, and the constructor stub that makes `IsEnabledAsync` echo back the
default value for any key) plus the first test case, covering spec FR-1: the
"has override" path where a `FeatureFlagOverride` row matches a registered
flag's key and the resulting `FlagStatusDto.IsOverridden` is `true` with
`UpdatedBy`/`UpdatedAt` populated from that override entity.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs` — new test class with scaffold + `Handle_FlagHasMatchingOverride_SetsIsOverriddenTrueAndPopulatesAuthorAndDate` test.

## Tests
- `ListFlagsHandlerTests.Handle_FlagHasMatchingOverride_SetsIsOverriddenTrueAndPopulatesAuthorAndDate` — seeds one override for `FeatureFlagKeys.LabelPrintingEnabled` and asserts `IsOverridden == true`, `UpdatedBy` and `UpdatedAt` match the seeded override.

## How to verify
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ListFlagsHandlerTests"
```
Result: `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`.

## Notes
Matches the task-context file verbatim. No production code changes (NFR-3).
Follow-on tasks (`no-override-test`, `case-mismatch-test`, `baseline-fields-test`,
`full-validation`) add the remaining spec coverage (FR-2, FR-3, FR-4) to this
same test class.

## PR Summary
Adds the scaffold and first test case (FR-1, has-override path) for
`ListFlagsHandler` unit test coverage, closing part of the coverage gap
described in issue #4174. No production code changed.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs` — new test class scaffold + has-override test

## Status
DONE
