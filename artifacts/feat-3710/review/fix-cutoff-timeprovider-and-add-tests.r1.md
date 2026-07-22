## Review — fix-cutoff-timeprovider-and-add-tests (r1)

### Scope
`InventoryCountTileBase.cs` line 38 now derives the cutoff from `_timeProvider.GetUtcNow().UtcDateTime` instead of `DateTime.UtcNow`, matching the existing `_timeProvider` usage on lines 51/55. New unit test class covers the four cases requested in the issue: exact-cutoff inclusion, one-second-before exclusion, null `LastStockTaking` exclusion, and a custom `DaysOffset` shifting the cutoff.

### Checks
- Fix is a minimal, single-line change; no behavior change beyond making the cutoff deterministic/testable — matches the issue's suggested approach exactly.
- The `HasValue` guard on line 43 is untouched, so the null-reference risk called out in the issue is not introduced.
- Tests use `FakeTimeProvider` frozen at a fixed instant plus a mocked `ICatalogRepository`, exercising the concrete `ProductInventoryCountTile` and a local subclass for the custom-offset case — matches the existing test conventions in the assembly (xUnit, FluentAssertions, Moq).
- `dotnet build` succeeds, `dotnet test --filter FullyQualifiedName~InventoryCountTileBaseTests` passes 4/4, `dotnet format` reports 0 files changed.

### Verdict
**Status:** PASS
