Final review: **APPROVED** with two minor notes (redundant catch block — deliberately kept per the plan's surgical-change principle; test double sync throw — cosmetic, tests pass). No fixes required.

---

# Implementation: OrgChart Service — Consolidate Error Logging to Single Site

## What was implemented

Removed every `_logger.LogError(...)` call from `OrgChartService.GetOrganizationStructureAsync` (4 calls: null-deserialization guard + 3 catch blocks). The controller's existing `LogError` is now the single error-logging site for the OrgChart slice. Regression tests were written first (TDD red phase) then made green by the production change. The handler test comment was refreshed to document the single-owner rule positively.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs` — removed 4 `_logger.LogError(...)` calls; changed `catch (Exception ex)` to `catch (Exception)` to eliminate unused-variable warning; all `throw`/`InvalidOperationException` wrapping unchanged
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs` *(new)* — 4 regression tests covering HttpRequestException, JsonException, null-deserialization, and generic-Exception failure paths; each asserts the expected throw type and `LogLevel.Error` `Times.Never`
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs` — comment-only update at line 66 naming `OrgChartController` as the single error-logging site

## Tests

- `OrgChartServiceTests.cs` — 4 tests, all passing. Covers every failure path in `GetOrganizationStructureAsync`.
- `GetOrganizationStructureHandlerTests.cs` — 2 existing tests, all still passing.
- Full OrgChart slice: 6/6 pass. Full suite: 4,377 passed, 38 failed (pre-existing Docker-dependent tests, unrelated to OrgChart).

## How to verify

```bash
# OrgChart slice only
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.OrgChart" \
  --nologo --verbosity minimal

# Confirm zero LogError in service
grep -n "LogError" backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs \
  || echo "no LogError found"

# Confirm single LogError in controller (line 49)
grep -n "LogError" backend/src/Anela.Heblo.API/Controllers/OrgChartController.cs
```

## Notes

- The `catch (Exception) { throw; }` block was kept as a deliberate minimal-change decision (spec: surgical edits only). The final reviewer flagged it as minor/dead code — valid, but removing it would restructure the catch chain beyond what the spec asked for.
- 38 pre-existing test failures exist in the full suite; all are Docker-dependent integration tests that also fail on `main`. None are OrgChart-related.

## PR Summary

Removes the duplicate `LogError` emitted by `OrgChartService` on every failure path, making `OrgChartController` the single error-logging site for the OrgChart slice. Each failure now produces exactly one `Error` log entry instead of two, reducing noise in Application Insights without losing any exception context (the controller passes the full exception to `LogError`, so the inner `HttpRequestException`/`JsonException` and the data-source URL are visible via `ex.ToString()`).

Four regression tests lock the new invariant: if any `LogError` is ever reintroduced in the service, the tests will fail immediately.

> **Observability note:** OrgChart failures now emit exactly one `Error` log line, from the `OrgChartController` logger category, instead of two. Existing controller-level alerts continue to work; any alert keyed specifically on the `OrgChartService` logger category should be re-pointed to the controller category (or made category-agnostic). No alert configuration changes are strictly required.

### Changes
- `backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs` — removed 4 `LogError` calls from failure paths
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs` — new regression test file (4 tests)
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/GetOrganizationStructureHandlerTests.cs` — comment update only

## Status

DONE