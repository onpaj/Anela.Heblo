# Implementation: case-mismatch-test

## What was implemented
Added `Handle_OverrideKeyCaseDiffersFromRegistryKey_IsTreatedAsNoMatch`, a new
`[Fact]` on `ListFlagsHandlerTests` covering spec FR-3: the `ListFlagsHandler`
override lookup uses `StringComparer.Ordinal` (case-sensitive), so an override
row whose `Key` differs only in case from the registry key must not be treated
as a match. The test stubs the repository with an override keyed by
`FeatureFlagKeys.LabelPrintingEnabled.ToUpperInvariant()` and asserts the
resulting DTO for `FeatureFlagKeys.LabelPrintingEnabled` has
`IsOverridden == false` with `UpdatedBy`/`UpdatedAt` both null — the same
"no override" shape already asserted by the existing
`Handle_FlagHasNoOverride_...` test, but reached via a case-mismatched key
instead of an absent one.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs` — added the one test method, placed directly after `Handle_FlagHasNoOverride_SetsIsOverriddenFalseAndNullsAuthorAndDate` per the task context.

## Tests
- `ListFlagsHandlerTests.Handle_OverrideKeyCaseDiffersFromRegistryKey_IsTreatedAsNoMatch` — new, covers the case-sensitive lookup contract (FR-3).

## How to verify
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ListFlagsHandlerTests"
```
Result: `Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3` (verified locally).

## Notes
No deviations from the task context — the test method was added verbatim as
specified, using the existing `_repoMock`/`CreateHandler()` fixtures already
present in the class.

## PR Summary
Added a unit test covering the `ListFlagsHandler` override-lookup case-sensitivity
contract (FR-3): an override key that differs from the registry key only in
case must not match, so the flag reports as not overridden.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandlerTests.cs` — added `Handle_OverrideKeyCaseDiffersFromRegistryKey_IsTreatedAsNoMatch`

## Status
DONE
