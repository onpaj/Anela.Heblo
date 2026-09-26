# Implementation: register-validation-pipeline

## What was implemented
Registered `IValidator<GetProductMarginSummaryRequest>` and the MediatR `IPipelineBehavior<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>` in `AnalyticsModule.AddAnalyticsModule`, mirroring the existing registrations for `GetMarginReportRequest` and `GetProductMarginAnalysisRequest`. Added an integration test proving the end-to-end pipeline (mediator -> validator -> `ValidationResultBehavior`) rejects an invalid `TimeWindow` value with the `InvalidTimeWindow` error code, without ever reaching the handler.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — added `using ...UseCases.GetProductMarginSummary;` and registered `IValidator<GetProductMarginSummaryRequest>` + `IPipelineBehavior<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>`, immediately after the sibling `GetProductMarginAnalysisRequest` registrations.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/Pipeline/AnalyticsValidationPipelineTests.cs` — extended `BuildMediator` with the same two registrations plus `ITimeWindowParser`, `IMonthlyBreakdownGenerator`, `ITopProductSorter` (needed to resolve `GetProductMarginSummaryHandler`'s constructor dependencies), and added `GetProductMarginSummary_InvalidTimeWindow_ReturnsInvalidTimeWindowErrorCode`.

## Tests
`AnalyticsValidationPipelineTests.GetProductMarginSummary_InvalidTimeWindow_ReturnsInvalidTimeWindowErrorCode` — sends a `GetProductMarginSummaryRequest` with `TimeWindow = "not-a-real-window"` through the real mediator pipeline and asserts `Success == false`, `ErrorCode == ErrorCodes.InvalidTimeWindow`, and `Params["timeWindow"] == "not-a-real-window"`.

Verification run (resumed unit): `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Analytics"` — `Passed! - Failed: 0, Passed: 162, Skipped: 0, Total: 162`. This includes the full `AnalyticsValidationPipelineTests` suite (all sibling pipeline tests plus the new case) and `GetProductMarginSummaryHandlerTests.ParseTimeWindow_UnknownValue_ThrowsArgumentException`, confirmed still passing unchanged (it calls the handler directly, bypassing the pipeline).

## How to verify
Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Analytics"` — expect 162/162 passing.
Run (from the worktree root, where `Anela.Heblo.sln` lives): `dotnet build Anela.Heblo.sln` — expect 0 errors.
Run: `dotnet format Anela.Heblo.sln --verify-no-changes` — expect no output / exit 0.

## Notes
This unit resumed a prior agent run that was force-stopped mid-task: the source diff (both files) already existed uncommitted in the worktree and matched the task context's Step 1 and Step 3 verbatim. This pass reviewed that diff against the task's stated intent, confirmed it was complete and correct, and ran the verification steps (full Analytics suite, solution build, format check) that had not yet been completed before the stop. No code changes were needed beyond what was already staged in the working tree.

`dotnet build` / `dotnet format` must target `Anela.Heblo.sln` at the worktree root — the `backend/` directory itself contains no `.sln` or top-level `.csproj`, so a bare `dotnet build`/`dotnet format` run from `backend/` fails with `MSB1003`/`MSBuildWorkspaceFinder` errors rather than resolving the solution.

## PR Summary
Registers `GetProductMarginSummaryRequest`'s validator and pipeline behavior in `AnalyticsModule`, the final wiring step that makes an invalid `TimeWindow` string return a proper `InvalidTimeWindow` (400-shaped) response instead of throwing an unhandled `ArgumentException` from the handler. An integration test exercises the real MediatR pipeline end-to-end to prove this.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — registered validator + pipeline behavior for `GetProductMarginSummaryRequest`
- `backend/test/Anela.Heblo.Tests/Features/Analytics/Pipeline/AnalyticsValidationPipelineTests.cs` — new integration test plus supporting `BuildMediator` registrations

## Status
DONE
