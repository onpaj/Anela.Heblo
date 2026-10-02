# Specification: Validate `TimeWindow` on GetProductMarginSummaryRequest

## Summary
`GET /api/analytics/product-margin-summary` accepts a free-form `TimeWindow` string that is never validated before reaching `TimeWindowParser.ParseTimeWindow`, which throws an unhandled `ArgumentException` for any value outside its five known literals. This surfaces to API clients as a 500 Internal Server Error instead of a 400 Bad Request. This change adds a FluentValidation validator and registers the existing `ValidationResultBehavior` pipeline behavior for this request, following the pattern already used by `GetMarginReportRequest` and `GetProductMarginAnalysisRequest`, so invalid `TimeWindow` values are rejected with a structured 400 response before the handler runs.

## Background
`AnalyticsModule.AddAnalyticsModule` registers `IValidator<T>` + `IPipelineBehavior<T, TResponse>` pairs for `GetMarginReportRequest` and `GetProductMarginAnalysisRequest`, but not for `GetProductMarginSummaryRequest`. As a result, `GetProductMarginSummaryHandler.Handle` calls `_timeWindowParser.ParseTimeWindow(request.TimeWindow)` with no upstream validation and no try/catch. `TimeWindowParser.ParseTimeWindow` throws a plain `ArgumentException` for any string not in `{current-year, current-and-previous-year, last-6-months, last-12-months, last-24-months}`. Since MediatR request handling has no global exception-to-400 mapping for `ArgumentException` in this codebase, the exception propagates to ASP.NET's default exception handling and produces a 500, which is inconsistent with the rest of the Analytics module's error contract (structured `BaseResponse` with `Success=false` and a `[HttpStatusCode]`-tagged `ErrorCode`, mapped to the correct status by `BaseApiController.HandleResponse`).

This is an arch-review finding (issue #4229), filed against existing production code — not a new feature. The fix must close the gap without changing the request's existing default behavior or its currently-supported values.

## Functional Requirements

### FR-1: Reject invalid `TimeWindow` values with a 400 response
`GetProductMarginSummaryRequestValidator` (new, `FluentValidation.AbstractValidator<GetProductMarginSummaryRequest>`) must add a rule on `TimeWindow` that fails when the value is not one of the five supported literals: `current-year`, `current-and-previous-year`, `last-6-months`, `last-12-months`, `last-24-months`. The comparison is case-sensitive and exact, matching `TimeWindowParser`'s `switch` exactly — the validator's allowed set and the parser's `switch` arms must not be allowed to drift apart (see NFR-2).

**Acceptance criteria:**
- `GET /api/analytics/product-margin-summary?timeWindow=bogus` returns HTTP 400, not 500.
- The 400 response body is the standard `BaseResponse`-shaped error contract (`Success=false`, a defined `ErrorCode`, and a `Params` dictionary carrying the offending value), consistent with how `GetMarginReportRequestValidator` reports errors — not a raw ASP.NET problem-details/exception page.
- `TimeWindowParser.ParseTimeWindow` is never reached with an invalid value once the validator/pipeline is registered, so the `ArgumentException` in `TimeWindowParser` becomes unreachable dead code for this request (it may remain as a defensive fallback; see Out of Scope).
- Empty string and `null`-coalesced-to-default cases are covered: `TimeWindow` has a non-null default (`"current-year"`) and is typed as non-nullable `string`, so an explicitly empty query value (`timeWindow=`) is treated as the literal string `""`, which the validator correctly rejects as not in the allowed set.
- All five currently-supported values continue to be accepted and behave exactly as before (regression coverage).

### FR-2: Register the validator and pipeline behavior in `AnalyticsModule`
`AnalyticsModule.AddAnalyticsModule` must register:
- `services.AddScoped<IValidator<GetProductMarginSummaryRequest>, GetProductMarginSummaryRequestValidator>();`
- `services.AddScoped<IPipelineBehavior<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>, ValidationResultBehavior<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>>();`

in the same location/style as the existing registrations for `GetMarginReportRequest` and `GetProductMarginAnalysisRequest` (lines 37–44 of the current file).

**Acceptance criteria:**
- DI resolves `IPipelineBehavior<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>` and it runs before the handler for every request (verified by an integration/handler test asserting the handler's own MediatR-mocked short-circuit, or a validation-pipeline test analogous to the existing `Pipeline/AnalyticsValidationPipelineTests.cs`).
- No other existing Analytics registrations, tile registrations, or DI lifetimes are altered.

### FR-3: Use a distinct, documented error code
Add a new `ErrorCodes` entry for this specific failure (e.g. `InvalidTimeWindow`) in the Analytics range (17XX; next free value after `InvalidReportPeriod = 1705` is `1706`), tagged `[HttpStatusCode(HttpStatusCode.BadRequest)]`, rather than reusing an unrelated code or falling back to the generic `ValidationError`. Add a corresponding message constant to `AnalyticsConstants.ValidationMessages` (e.g. `INVALID_TIME_WINDOW`) that names the offending value and, ideally, the allowed set — mirroring `INVALID_DATE_RANGE`/`PERIOD_TOO_LONG`'s style of a `Params`-driven, human-readable message.

**Acceptance criteria:**
- The 400 response's `ErrorCode` is the new specific code, not `ValidationError` or an existing unrelated code.
- The response `Params` dictionary includes the invalid `timeWindow` value the caller sent (matching the `WithState(...)` pattern used by `GetMarginReportRequestValidator`), so the message is user-actionable per the issue's "Why it matters" framing.

## Non-Functional Requirements

### NFR-1: Backward compatibility
No change to `GetProductMarginSummaryRequest`'s public shape (property names, types, defaults) or to the five currently-accepted `TimeWindow` values' behavior. Existing callers sending any of the five valid literals see no change in status code, response shape, or computed date range.

### NFR-2: Single source of truth risk (flag for architect)
The suggested fix (per the issue body) duplicates the allowed-values list between the new validator and `TimeWindowParser`'s `switch`. This spec flags that duplication as a design risk to be resolved in architecture/design (see Open Questions) — e.g. the validator could delegate to the parser or a shared constant/enum rather than hardcoding a second copy of the five strings.

### NFR-3: No behavior change for other requests
`GetMarginReportRequest`, `GetProductMarginAnalysisRequest`, and all other Analytics MediatR requests are untouched. Only `GetProductMarginSummaryRequest` gains validation.

## Data Model
No persistent data model changes. In-memory/DI additions only:
- New `GetProductMarginSummaryRequestValidator` class (Analytics.Validators namespace, matching existing sibling validators).
- One new `ErrorCodes` enum member (see FR-3).
- One new `AnalyticsConstants.ValidationMessages` constant (see FR-3).

## API / Interface Design
No new endpoints. Existing endpoint `GET /api/analytics/product-margin-summary` (already documented with `[ProducesResponseType(StatusCodes.Status400BadRequest)]` in `AnalyticsController`, which is currently unreachable for this specific cause) gains an actually-reachable 400 path for invalid `timeWindow`. Response body on failure follows the existing `BaseResponse`/`GetProductMarginSummaryResponse` error contract already used by sibling endpoints — no new response DTO needed.

## Dependencies
- `FluentValidation` (already a project dependency, used by sibling validators).
- `Anela.Heblo.Application.Common.Behaviors.ValidationResultBehavior<TRequest, TResponse>` (existing, reused as-is — no changes needed to this generic behavior).
- `Anela.Heblo.Application.Shared.ErrorCodes` and `BaseApiController.HandleResponse` (existing status-code mapping infrastructure, reused as-is).
- Existing test infrastructure: `backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetMarginReportRequestValidatorTests.cs` and `backend/test/Anela.Heblo.Tests/Features/Analytics/Pipeline/AnalyticsValidationPipelineTests.cs` are the closest existing analogues for the new tests this change needs.

## Out of Scope
- Replacing the `string TimeWindow` property with an enum (the issue's "alternative" suggestion). This spec targets the validator+pipeline fix only; an enum-based redesign would be a breaking API/contract change requiring its own spec and OpenAPI client regeneration, and is left as a possible future improvement.
- Removing or changing the `ArgumentException` thrown by `TimeWindowParser.ParseTimeWindow` itself — it may remain as a defensive guard for any other/future caller of the parser that bypasses the MediatR pipeline.
- Any change to `GetMarginReportRequestValidator` or `GetProductMarginAnalysisRequestValidator` (already correct, used only as reference pattern).
- Frontend changes — the existing frontend already must handle 400 responses from sibling Analytics endpoints; no new UI behavior is specified here beyond what already exists for other validation errors on this page/module.

## Open Questions
None — the fix pattern is fully established by two existing sibling implementations (`GetMarginReportRequest`, `GetProductMarginAnalysisRequest`) in the same module, and the allowed-value set is already fully enumerated in both the issue and the current `TimeWindowParser` source. The single design choice worth the architect's explicit sign-off — whether the validator hardcodes its own copy of the five literals (matching the issue's suggested-fix snippet exactly) or derives them from a shared source with `TimeWindowParser` to avoid duplication (NFR-2) — is not a blocking ambiguity: either choice is a valid, shippable implementation of this spec, so it is carried into architecture as a decision point rather than held here as an open question.

## Status: COMPLETE
