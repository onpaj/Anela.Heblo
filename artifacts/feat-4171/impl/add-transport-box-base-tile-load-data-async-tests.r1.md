# Implementation: add-transport-box-base-tile-load-data-async-tests

## What was implemented
Added two unit tests covering `TransportBoxBaseTile.LoadDataAsync`'s success and
exception paths, which were previously untested (0% coverage per the coverage-gap
issue).

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/TransportBoxBaseTileTests.cs`
  — added `System.Linq.Expressions` using directive, plus
  `LoadDataAsync_RepositorySucceeds_ReturnsSuccessStatusWithCount` and
  `LoadDataAsync_RepositoryThrows_ReturnsErrorShapeWithExceptionMessage`.

## Tests
- `LoadDataAsync_RepositorySucceeds_ReturnsSuccessStatusWithCount` — mocks
  `ITransportBoxRepository.FindAsync` to return 3 boxes, asserts the JSON result
  has `status: success` and `data.count: 3`.
- `LoadDataAsync_RepositoryThrows_ReturnsErrorShapeWithExceptionMessage` — mocks
  `FindAsync` to throw `InvalidOperationException`, asserts the JSON result has
  `status: error`, `error: "Nepodařilo se načíst počet boxů"`, and
  `details` equal to the exception message.

## How to verify
```
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TransportBoxBaseTileTests"
```
Result: 6 tests discovered (4 pre-existing `GenerateDrillDownFilters` tests + 2
new `LoadDataAsync` tests), all passing (`Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`).

## Notes
No production code changes were needed — the task was purely additive test
coverage per the task-context spec. Followed the exact test code given in the
task-context file verbatim.

## PR Summary
Adds unit test coverage for `TransportBoxBaseTile.LoadDataAsync`'s success and
error-handling paths, closing part of the coverage gap reported against
`TransportBoxBaseTile.cs`.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Logistics/DashboardTiles/TransportBoxBaseTileTests.cs` — added two tests for `LoadDataAsync`

## Status
DONE
