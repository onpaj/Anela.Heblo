# Implementation: update-groupby-tests

## What was implemented

Updated `GetDailyConsumptionBreakdownHandlerTests.cs` to use the new `ConsumptionGroupBy`
enum instead of raw strings, matching the retyped `GroupBy` property on
`GetDailyConsumptionBreakdownRequest` introduced by earlier tasks on this branch.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetDailyConsumptionBreakdownHandlerTests.cs`
  — added the `Contracts` `using`, replaced the three string `GroupBy` literals
  (`"material"`, `"order"`, `"product"`) with `ConsumptionGroupBy` enum values across
  the four tests that use them, and replaced `GroupBy_InvalidValue_ReturnsError`
  (which asserted the now-removed runtime `HashSet` validation) with
  `GroupBy_OutOfRangeEnumValue_ThrowsArgumentOutOfRangeException`, which asserts the
  handler's switch throws on an out-of-range enum value cast in from an int.

## Tests

- `GetDailyConsumptionBreakdownHandlerTests.cs` — all 71 tests in the PackingMaterials
  suite pass, including the four retyped tests and the new
  `GroupBy_OutOfRangeEnumValue_ThrowsArgumentOutOfRangeException`.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterials"
```

Result: `Passed! - Failed: 0, Passed: 71, Skipped: 0, Total: 71`.

## Notes

No deviations from the task context — implemented exactly as specified.

## PR Summary
Updated the `GetDailyConsumptionBreakdownHandler` test suite to use the new
`ConsumptionGroupBy` enum instead of magic strings for the `GroupBy` request property,
and replaced the obsolete invalid-string test with one that verifies the handler still
fails loudly on an out-of-range enum value.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetDailyConsumptionBreakdownHandlerTests.cs` — retyped `GroupBy` test literals to `ConsumptionGroupBy`, replaced the invalid-string test with an out-of-range-enum test

## Status
DONE
