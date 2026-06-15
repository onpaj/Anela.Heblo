All tasks are complete. Here is the implementation summary:

---

# Implementation: Fix OrgChartController Error Response Contract

## What was implemented

Replaced the anonymous-object error return in `OrgChartController.GetOrganizationStructure` with a typed `OrgChartResponse(ErrorCodes.InternalServerError)`, eliminating raw exception-message leakage (including internal SharePoint URLs) and restoring the declared `ActionResult<OrgChartResponse>` contract.

## Files created/modified

- `backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs` — added `using Anela.Heblo.Application.Shared;`, replaced `new { error = "...", message = ex.Message }` with `new OrgChartResponse(ErrorCodes.InternalServerError)` in catch arm
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartControllerTests.cs` — new file, 4 unit tests covering success path, typed error envelope, no-leak assertion, and log-preservation assertion

## Tests

`OrgChartControllerTests.cs` covers:
1. `GetOrganizationStructure_ReturnsOk_WhenHandlerSucceeds` — happy path baseline
2. `GetOrganizationStructure_Returns500_WithTypedErrorResponse_WhenHandlerThrows` — FR-1: typed `OrgChartResponse`, `Success=false`, `ErrorCode=InternalServerError`, status 500
3. `GetOrganizationStructure_DoesNotLeakExceptionMessage_WhenHandlerThrows` — FR-2: serializes body to JSON and asserts SECRET-MARKER is absent
4. `GetOrganizationStructure_LogsExceptionWithFullDetail_WhenHandlerThrows` — FR-3: verifies `LogError(ex, "Error fetching organizational structure")` called once

All 4 pass. Full backend suite: 4,959 passed, 49 skipped (Docker-dependent integration tests, unrelated).

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~OrgChartControllerTests" -v minimal
# expect: 4/4 PASS

dotnet test backend/Anela.Heblo.sln --no-build -v minimal
# expect: all pass (integration tests skipped — Docker not running)
```

## Notes

- The `try`/`catch` is retained intentionally — the only registered `IExceptionHandler` is `UnauthorizedAccessExceptionHandler`; removing it would cause the fallback to emit `ProblemDetails` instead of `OrgChartResponse`.
- Frontend `npm run lint` exits 1 due to **pre-existing** test suite lint violations unrelated to this change. No new TypeScript errors were introduced. The generated client (`frontend/src/api/generated/`) did not drift.
- The `BaseResponse(Exception)` constructor was deliberately avoided — it stuffs `ex.Message` into `Params`, which would re-introduce the leak this fix exists to prevent.
- Commits: `8c945c5f` (tests, RED state), `bf8a1a02` (production fix, GREEN).

## PR Summary

Restores the typed error contract on `GET /api/orgchart/structure`, which was the only endpoint in the codebase returning an anonymous object instead of a `BaseResponse`-derived DTO. Before this fix, any exception thrown by `OrgChartService` (including wrapped `HttpRequestException`s containing the configured SharePoint `DataSourceUrl`) was serialized verbatim into `ex.Message` and sent to the browser. The fix is a single-line swap in the catch arm — `new OrgChartResponse(ErrorCodes.InternalServerError)` — preserving the HTTP 500 status, keeping full exception detail in server logs, and making the error shape visible to the generated TypeScript client.

### Changes
- `backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs` — replaced anonymous error object with typed `OrgChartResponse(ErrorCodes.InternalServerError)`; added `using Anela.Heblo.Application.Shared`
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartControllerTests.cs` — new test file with 4 unit tests: success path, typed error envelope, no-leak (SECRET-MARKER sentinel), log preservation

## Status

DONE_WITH_CONCERNS

> **Concern:** `npm run lint` in `frontend/` exits 1 due to pre-existing lint violations unrelated to this change. No new TypeScript errors were introduced by the backend fix. The pre-existing lint failures should be tracked and resolved separately.