All tasks complete. Here is the output artifact:

---

# Implementation: Analytics Margin Report Validation Pipeline

## What was implemented

Centralized validation for `GetMarginReportRequest` and `GetProductMarginAnalysisRequest` into FluentValidation validators wired through a new `ValidationResultBehavior<TRequest, TResponse>` MediatR pipeline behavior. The behavior returns a `BaseResponse` envelope (never throws), so invalid requests are rejected before reaching the handlers with the exact `ErrorCode` + `Params` shape the frontend depends on.

The arch review correctly identified that the validators were **not** pipeline-wired before this change — the handler-level `if`-blocks were the only runtime enforcement. This PR fixes that structural gap first (before deleting the guards), ensuring no silent regression.

## Files created/modified

**New source files:**
- `backend/src/Anela.Heblo.Application/Common/Behaviors/ValidationResultBehavior.cs` — typed `IPipelineBehavior<TRequest, TResponse>` that translates the first FluentValidation failure into a `BaseResponse` envelope; parses `ErrorCode` from `failure.ErrorCode` and `Params` from `failure.CustomState`

**Modified source files:**
- `backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetMarginReportRequestValidator.cs` — added `WithErrorCode(...)` + `WithState(...)` to all rules
- `backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetProductMarginAnalysisRequestValidator.cs` — same, including `ProductId.NotEmpty()` → `ErrorCodes.RequiredFieldMissing`
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — registered `ValidationResultBehavior` for both request types
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs` — removed duplicate validation if-blocks
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs` — removed duplicate validation if-blocks

**New test files:**
- `backend/test/Anela.Heblo.Tests/Common/Behaviors/ValidationResultBehaviorTests.cs` — 6 unit tests for the behavior
- `backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetMarginReportRequestValidatorTests.cs` — 10 validator unit tests
- `backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetProductMarginAnalysisRequestValidatorTests.cs` — 12 validator unit tests
- `backend/test/Anela.Heblo.Tests/Features/Analytics/Pipeline/AnalyticsValidationPipelineTests.cs` — 4 `IMediator`-level integration tests verifying the wiring

**Modified test files:**
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs` — removed `Handle_InvalidDateRange_ReturnsErrorResponse`, `Handle_PeriodTooLong_ReturnsErrorResponse`, `Handle_ZeroDaysPeriod_ReturnsErrorResponse`
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs` — removed `Handle_InvalidDateRange_ReturnsErrorResponse`, `Handle_EmptyProductId_ReturnsErrorResponse`

## Tests

- `ValidationResultBehaviorTests` — 6 tests: no validators, valid request, failure with parseable ErrorCode+State, unparseable ErrorCode fallback, null CustomState, multiple failures use first
- `GetMarginReportRequestValidatorTests` — 10 tests: start>end (error, code, params), period too long/short (code), boundary days, valid request
- `GetProductMarginAnalysisRequestValidatorTests` — 12 tests: empty ProductId (message, code, params), valid ProductId, start>end (message, code, params), period too long/short, valid request
- `AnalyticsValidationPipelineTests` — 4 integration tests: invalid date range and period too long for GetMarginReport; empty ProductId and invalid date range for GetProductMarginAnalysis — all via real `IMediator`

## How to verify

```bash
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ValidationResultBehaviorTests|FullyQualifiedName~GetMarginReportRequestValidatorTests|FullyQualifiedName~GetProductMarginAnalysisRequestValidatorTests|FullyQualifiedName~AnalyticsValidationPipelineTests|FullyQualifiedName~GetMarginReportHandlerTests|FullyQualifiedName~GetProductMarginAnalysisHandlerTests"
# Expected: 44 tests pass
```

## Notes

- The 32 failing tests in the full suite are all Testcontainers/PostgreSQL integration tests (MeetingTranscript, Article, KnowledgeBase, Catalog) — pre-existing infrastructure failures not caused by this change.
- The new `ValidationResultBehavior` is deliberately separate from the existing throwing `ValidationBehavior`. Other modules are unaffected.
- A single test fix was needed: `GetMarginReportRequestValidatorTests` had `.HaveCount(1)` assertions on error collections that failed because `StartDate > EndDate` triggers both the date-range rule and the min-period rule simultaneously. Fixed by filtering to the specific property error instead.

## PR Summary

Centralizes validation for `GetMarginReportRequest` and `GetProductMarginAnalysisRequest` in their FluentValidation validators by wiring a new `ValidationResultBehavior<TRequest, TResponse>` into the MediatR pipeline. The behavior returns the existing `BaseResponse` envelope (never throws), preserving the exact `ErrorCode` + `Params` shape the frontend depends on.

**Key correction from arch review:** the validators were not actually pipeline-wired before — the handler-level `if`-blocks were the only runtime enforcement. This PR adds the wiring as a prerequisite before deleting those guards.

### Changes
- `Common/Behaviors/ValidationResultBehavior.cs` — new typed pipeline behavior returning `BaseResponse` envelope
- `Validators/GetMarginReportRequestValidator.cs` — added `WithErrorCode`/`WithState` to all rules
- `Validators/GetProductMarginAnalysisRequestValidator.cs` — same, including ProductId rule
- `AnalyticsModule.cs` — registered `ValidationResultBehavior` per-request (matching Catalog/Photobank pattern)
- `GetMarginReportHandler.cs` — removed duplicate validation if-blocks (now dead code)
- `GetProductMarginAnalysisHandler.cs` — removed duplicate validation if-blocks
- `Tests/Common/Behaviors/ValidationResultBehaviorTests.cs` — 6 unit tests
- `Tests/Features/Analytics/Validators/GetMarginReportRequestValidatorTests.cs` — 10 tests (new)
- `Tests/Features/Analytics/Validators/GetProductMarginAnalysisRequestValidatorTests.cs` — 12 tests (new)
- `Tests/Features/Analytics/Pipeline/AnalyticsValidationPipelineTests.cs` — 4 IMediator integration tests (new)
- `GetMarginReportHandlerTests.cs` — removed 3 tests: `Handle_InvalidDateRange_ReturnsErrorResponse`, `Handle_PeriodTooLong_ReturnsErrorResponse`, `Handle_ZeroDaysPeriod_ReturnsErrorResponse`
- `GetProductMarginAnalysisHandlerTests.cs` — removed 2 tests: `Handle_InvalidDateRange_ReturnsErrorResponse`, `Handle_EmptyProductId_ReturnsErrorResponse`

## Status
DONE