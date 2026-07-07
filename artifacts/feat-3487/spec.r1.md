# Specification: Remove dead `IncludeDetailedBreakdown` flag from GetMarginReport

## Summary
`GetMarginReportRequest.IncludeDetailedBreakdown` is a public query parameter that `GetMarginReportHandler` never reads — the handler always returns full `ProductSummaries` and `CategorySummaries` regardless of its value. Investigation confirms **no caller anywhere in the codebase ever sets this flag**: no frontend component, hook, or generated-client caller passes it, and no backend code path other than the request DTO itself references it. Per the decision rule for this review, this is exactly the evidence needed to choose **Option 1: remove the parameter** rather than implement it — there is no real consumer to serve, so building conditional response-shaping logic (and tests for it) would add complexity for a feature nobody uses. This is a small, surgical deletion, not a behavior change for any existing caller.

## Background
This is an architecture-review finding (feat-3487), not a new feature. The `Analytics` module's `GetMarginReport` endpoint exposes a boolean flag in its OpenAPI contract that implies an optimisation (skip building detailed breakdowns for summary-only consumers). The flag is fully wired into request binding, model validation setup, and the generated TypeScript client's query-string serialization — but it does nothing. This is misleading to anyone reading the API contract and is pure noise on every request. The fix must not introduce new behavior; it should remove the illusion of a capability that was never built and is not needed today.

## Functional Requirements

### FR-1: Remove `IncludeDetailedBreakdown` from `GetMarginReportRequest`
Delete the `IncludeDetailedBreakdown` property from `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportRequest.cs`. No other property on the request changes.

**Acceptance criteria:**
- `GetMarginReportRequest` no longer declares `IncludeDetailedBreakdown`.
- `GetMarginReportHandler.cs` requires no changes (it never referenced the property).
- `AnalyticsController.GetMarginReport` continues to bind the remaining request properties via `[FromQuery]` with no controller code changes needed.
- The regenerated OpenAPI spec / TypeScript client (`frontend/src/api/generated/api-client.ts`) no longer includes `includeDetailedBreakdown` as a parameter of `analytics_GetMarginReport(...)`, and no longer serializes `IncludeDetailedBreakdown` into the query string. Regenerate the client per `docs/development/api-client-generation.md` as part of this change.

### FR-2: Clean up references in tests
`backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetMarginReportRequestValidatorTests.cs:220` sets `IncludeDetailedBreakdown = false` on a test request object. Remove that line (or the whole property initializer if it's the only one) so the test still compiles.

**Acceptance criteria:**
- The full backend test suite compiles and passes after the property removal, with no remaining reference to `IncludeDetailedBreakdown` anywhere in `backend/`.
- `GetMarginReportHandlerTests.cs` requires no changes (it never set or asserted on this property).

## Non-Functional Requirements

### NFR-1: Performance
None — this is a pure removal of an unused, no-op field. No performance target applies.

### NFR-2: Security
None — the property carries no sensitive data and no auth implications. Removing it slightly shrinks public API surface, which is a minor net-positive for hygiene but not a security-driven change.

## Data Model
No data model changes. `GetMarginReportRequest` after this change:
```csharp
public class GetMarginReportRequest : IRequest<GetMarginReportResponse>
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? ProductFilter { get; set; }
    public string? CategoryFilter { get; set; }
    public int MaxProducts { get; set; } = 50;
}
```
`GetMarginReportResponse` is unchanged — it continues to always return `ProductSummaries` and `CategorySummaries` in full, matching current (and now honestly-documented) behavior.

## API / Interface Design
- `GET /api/analytics/margin-report` (via `AnalyticsController.GetMarginReport`) drops the `IncludeDetailedBreakdown` query parameter from its contract.
- No other endpoint shape, status code, or response body field changes.
- Any external caller that was previously passing `IncludeDetailedBreakdown=true|false` (none found in this codebase) will simply have that query parameter ignored by ASP.NET model binding rather than rejected — this is a non-breaking removal for any hypothetical unknown caller.

## Dependencies
- OpenAPI client regeneration (`docs/development/api-client-generation.md`) must be run so `frontend/src/api/generated/api-client.ts` stays in sync with the trimmed backend contract.
- No other feature or module depends on this property (confirmed via repo-wide search).

## Out of Scope
- No change to `GetMarginReportHandler`'s response-building logic (`ProductSummaries` / `CategorySummaries` continue to always be populated).
- No new "lightweight/summary" endpoint or response shape is introduced.
- No frontend UI changes — no frontend code referenced this flag.
- No changes to `GetMarginReportRequestValidator.cs` validation rules (the flag was never validated).

## Open Questions
None.

## Status: COMPLETE
