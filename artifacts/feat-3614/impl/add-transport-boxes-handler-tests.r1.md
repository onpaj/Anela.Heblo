# Implementation: add-transport-boxes-handler-tests

## What was implemented
Added a new xUnit test class covering the previously-untested `State` filter
routing logic in `GetTransportBoxesHandler.Handle`, plus request pass-through
and response-mapping behavior, per `task-plan.r1.md`.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxesHandlerTests.cs` — new test file with a `[Theory]` covering all 8 state-filter routing cases and two `[Fact]`s covering parameter pass-through and response mapping.

## Tests
- `Handle_StateFilter_RoutesExpectedArgumentsToRepository` (`[Theory]`, 8 `[InlineData]` cases): `"ACTIVE"`, `"active"`, `"Opened"`, `"closed"`, `null`, `""`, `"   "`, `"NotARealState"` — asserts the exact `state`/`isActiveFilter` arguments reaching `ITransportBoxRepository.GetPagedListAsync` via `Mock.Verify`.
- `Handle_ForwardsAllPassThroughParametersToRepository`: asserts `Skip`, `Take`, `Code`, `ProductCode`, `SortBy`, `SortDescending` are forwarded verbatim.
- `Handle_MapsRepositoryResultIntoResponse`: asserts `Items`/`TotalCount`/`Skip`/`Take` are populated correctly from a real `TransportBoxMappingProfile`-backed mapper, using a `totalCount` distinct from `items.Count`.

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetTransportBoxesHandlerTests"
```
Expected: `Passed! - Failed: 0, Passed: 10, Skipped: 0`.

`dotnet format Anela.Heblo.sln --verify-no-changes` reports no violations for the new file.

The full suite (`dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`) was also run: 76 pre-existing failures, all in Postgres/Testcontainers-backed integration and SQL-shape tests (`*IntegrationTests`, `*SqlShapeTests`) — this sandbox has no Docker daemon available (`docker ps` fails to connect to the socket), so these fail regardless of this change. None reference `Logistics`, `TransportBox`, or `GetTransportBoxes`. No regression was introduced by this change.

## Notes
- The architect caught that the spec's example enum value `TransportBoxState.Open` does not exist — the real member is `Opened`. The planner already corrected this in `task-plan.r1.md`, and the test code uses `TransportBoxState.Opened`/`TransportBoxState.Closed` throughout.
- No production code was modified — this is a test-only change, per FR-3's scope (no bug was found in the handler during implementation).

## PR Summary
Adds unit test coverage for `GetTransportBoxesHandler`'s `State` filter routing, the piece of logic flagged as untested by the coverage-gap issue: the special-case `"ACTIVE"` string (meaning "all boxes except Closed"), parseable `TransportBoxState` enum values, and the null/empty/unparseable fallthrough. The new test class also covers request parameter pass-through and response mapping so the file's coverage isn't limited to just the `if/else if` branch. No production code changed — the existing handler behavior was confirmed correct by these tests, not fixed.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxesHandlerTests.cs` — new test file, 10 test cases (1 theory with 8 inline cases + 2 facts)

## Status
DONE
