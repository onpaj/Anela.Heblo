# Implementation: map-orgchart-json-model-to-response-contracts

## What was implemented

`OrgChartService` no longer deserializes the external org-chart JSON directly into
`OrgChartResponse` (an Application-layer API response contract inheriting
`Success`/`ErrorCode`/`Params` from `BaseResponse`). It now deserializes into a new
adapter-internal `OrgChartJsonModel` (plus `OrgChartJsonOrganization`,
`OrgChartJsonPosition`, `OrgChartJsonEmployee`) that mirrors the external JSON shape,
then explicitly maps that model into `OrgChartResponse`/`OrganizationDto`/`PositionDto`/
`EmployeeDto` via three private static mapping methods (`MapOrganization`, `MapPosition`,
`MapEmployee`).

This decouples the adapter's JSON parsing from the Application contract shape: a rename
of an `OrgChartResponse`/`*Dto` member no longer silently changes external JSON binding,
and `BaseResponse`'s default `Success`/`ErrorCode`/`Params` values are now explicit
construction defaults rather than incidental deserialization artifacts.

## Files created/modified

- `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/Models/OrgChartJsonModel.cs` — new
  file with the four `internal sealed class` JSON-shape POCOs (`OrgChartJsonModel`,
  `OrgChartJsonOrganization`, `OrgChartJsonPosition`, `OrgChartJsonEmployee`), not
  referenced from anywhere outside the Adapters project.
- `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartService.cs` — deserializes
  into `OrgChartJsonModel` instead of `OrgChartResponse`; adds `MapOrganization`/
  `MapPosition`/`MapEmployee` private static mapping methods implementing the
  null-coalescing rules (strings → `string.Empty`, lists → empty list, `Level`/
  `IsPrimary` passed through unchanged). The `try`/`catch` structure, both exception
  wrap messages, the null-check message, and the `LogInformation` call (now reading the
  final mapped `orgChart`) are all unchanged.
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs` — added one
  new `[Fact]`, `GetOrganizationStructureAsync_MapsFullJsonGraphToResponseContracts`,
  asserting the full mapped object graph (2 positions, nested employee, null → empty
  string/list coalescing) plus the `BaseResponse` envelope defaults. The four existing
  tests are unmodified.

## Tests

- `OrgChartServiceTests` (5 tests, all passing): the 4 pre-existing error-path tests plus
  the new happy-path mapping test.
- `GetOrganizationStructureHandlerTests` and `OrgChartModuleValidationTests` (5 tests, all
  passing, unmodified) — confirm no regression in the handler/DI/module layer, none of
  which changed.
- Full backend suite (`dotnet test`): 7110 passed, 4 skipped, 110 failed — every failure
  is a pre-existing Docker/testcontainers-dependent integration test (Postgres
  testcontainers unavailable in this sandbox, e.g.
  `ArticleRepositoryFeedbackProjectionSqlTests`, `LeafletDocumentRepositoryPagedTests`,
  etc.). No OrgChart test appears in the failure list; this is an environment limitation,
  not a regression from this change.

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OrgChartServiceTests"
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetOrganizationStructureHandlerTests|FullyQualifiedName~OrgChartModuleValidationTests"
dotnet build ../Anela.Heblo.sln    # from backend/, or `dotnet build Anela.Heblo.sln` from repo root
dotnet format ../Anela.Heblo.sln --verify-no-changes
```

## Notes

- The task context's Step 7/Step 11 build/test commands assumed a solution file directly
  under `backend/`. This repo's actual layout has `Anela.Heblo.sln` at the repo root, so
  those two steps were run against `Anela.Heblo.sln` from the repo root instead —
  functionally equivalent, same acceptance criteria (0 build errors; full test suite run
  as a safety net). No other deviation from the task context.
- Followed the task context's exact code for `OrgChartJsonModel.cs` and the
  `OrgChartService.cs` replacement, and the exact new test body, verbatim.
- Confirmed (Step 3) that the new test fails against the pre-refactor code with a
  `NullReferenceException` on `p.Employees.Count` inside the `LogInformation` call's
  `Sum` (position 2 has `employees: null`), consistent with the task context's framing
  that this is a TDD pin for the post-refactor mapping rather than a proof the old code
  was broken for typical payloads.

## PR Summary

Decoupled `OrgChartService`'s external-JSON deserialization from the Application-layer
`OrgChartResponse` contract by introducing an adapter-internal `OrgChartJsonModel` and
mapping it explicitly into the response DTOs, closing the architecture-review finding
that a contract rename could silently change external JSON parsing behavior. Added a
happy-path mapping test to close the test-coverage gap (previously only error paths were
tested).

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/Models/OrgChartJsonModel.cs` — new adapter-internal JSON POCOs
- `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartService.cs` — deserialize into the new model, map explicitly into the response contracts
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs` — new happy-path mapping test

## Status
DONE
