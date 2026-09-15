# Implementation: no-override-test

## What was implemented
Added the second unit test for `ListFlagsHandler`, covering the "no override exists"
path (spec FR-2): with an empty override repository result, the handler's DTO for a
tracked flag key should report `IsOverridden = false` and leave `UpdatedBy`/`UpdatedAt`
null.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs` — added `Handle_FlagHasNoOverride_SetsIsOverriddenFalseAndNullsAuthorAndDate`, appended after the existing has-override test in the class scaffolded by the previous task.

## Tests
- `ListFlagsHandlerTests.Handle_FlagHasNoOverride_SetsIsOverriddenFalseAndNullsAuthorAndDate` — stubs `IFeatureFlagOverrideRepository.GetAllAsync` to return an empty list, then asserts the `FeatureFlagKeys.LabelPrintingEnabled` DTO has `IsOverridden == false`, `UpdatedBy == null`, `UpdatedAt == null`.

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ListFlagsHandlerTests"
```
Result: `Passed! - Failed: 0, Passed: 2, Skipped: 0`.

## Notes
No deviations from the task context — implemented exactly the test method specified,
verbatim.

## PR Summary
Added the no-override-path unit test for `ListFlagsHandler`, per the task plan's second
test-coverage task. No production code changed.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs` — added `Handle_FlagHasNoOverride_SetsIsOverriddenFalseAndNullsAuthorAndDate`

## Status
DONE
