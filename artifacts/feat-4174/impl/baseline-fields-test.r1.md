# Implementation: baseline-fields-test

## What was implemented
Added `Handle_AlwaysReturnsOneDtoPerRegisteredFlag_WithFieldsCopiedFromDefinition`,
a new `[Fact]` on `ListFlagsHandlerTests` covering spec FR-4: the response
contains exactly one `FlagStatusDto` per entry in `FeatureFlagRegistry.All`,
and each DTO's `Key`/`Description`/`DefaultValue` are copied verbatim from the
registry's `FeatureFlagDefinition`. The test stubs the repository with no
overrides, asserts `response.Flags.Count == FeatureFlagRegistry.All.Count`,
then picks the `LabelPrintingEnabled` DTO and asserts its `Description` and
`DefaultValue` match the definition looked up via `FeatureFlagRegistry.ByKey`.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs` — added the one test method, placed directly after `Handle_OverrideKeyCaseDiffersFromRegistryKey_IsTreatedAsNoMatch` per the task context.

## Tests
- `ListFlagsHandlerTests.Handle_AlwaysReturnsOneDtoPerRegisteredFlag_WithFieldsCopiedFromDefinition` — new, covers the baseline one-DTO-per-flag / field-mapping contract (FR-4).

## How to verify
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ListFlagsHandlerTests"
```
Result: `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4` (verified locally).

## Notes
No deviations from the task context — the test method was added verbatim as
specified, using the existing `_repoMock`/`CreateHandler()` fixtures already
present in the class.

## PR Summary
Added a unit test covering the `ListFlagsHandler` baseline DTO-mapping
contract (FR-4): the response returns exactly one DTO per registered flag,
with `Key`/`Description`/`DefaultValue` copied from the registry definition.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs` — added `Handle_AlwaysReturnsOneDtoPerRegisteredFlag_WithFieldsCopiedFromDefinition`

## Status
DONE
