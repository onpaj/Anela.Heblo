# Implementation: remove-includedetailedbreakdown-property

## What was implemented
Removed the dead `IncludeDetailedBreakdown` boolean property from `GetMarginReportRequest` (it was never read by `GetMarginReportHandler` or anywhere else in the backend/frontend) and removed its one initializer reference in the validator test suite.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportRequest.cs` — deleted the `IncludeDetailedBreakdown` property.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetMarginReportRequestValidatorTests.cs` — removed the `IncludeDetailedBreakdown = false` initializer line from `ValidRequest_ShouldNotHaveAnyValidationErrors`.

## Tests
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetMarginReport"` — 19/19 passed.
- Full suite: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — 5414 passed, 64 failed, 4 skipped. All 64 failures are pre-existing `Article.Persistence` Testcontainers/PostgreSQL tests that require a Docker daemon, which is unavailable in this sandbox (`docker info` confirms no daemon socket). Unrelated to this change — none touch Analytics/GetMarginReport.

## How to verify
```bash
grep -rn "IncludeDetailedBreakdown" backend/ --include="*.cs"   # no matches
dotnet build Anela.Heblo.sln                                    # build succeeds
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetMarginReport"
```

## Notes
- The plan referenced `backend/Anela.Heblo.sln`; the actual solution file is at the repo root (`Anela.Heblo.sln`).
- Frontend/backend generated OpenAPI client regeneration is deferred to task 2 (`regenerate-openapi-clients-and-verify`) per the task plan.

## Status
DONE
