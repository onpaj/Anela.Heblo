# Specification: Standardize validation error response in PurchaseStockAnalysisController

## Summary
`PurchaseStockAnalysisController.GetStockAnalysis` currently returns ASP.NET Core's raw `ModelState` when request validation fails (`BadRequest(ModelState)`), instead of the project's standardized `BaseResponse`-derived error envelope used by every other Purchase controller. This spec covers replacing that one call with `ErrorResponseHelper.CreateValidationError<GetPurchaseStockAnalysisResponse>()`, bringing the controller in line with `PurchaseOrdersController` and the rest of the codebase's unified API error contract.

## Background
The project's API contract requires all validation failures to be returned as a typed `BaseResponse`-derived object with `Success = false` and an `ErrorCode`, so frontend error handling can uniformly key off `BaseResponse.Success` / `BaseResponse.ErrorCode`. `PurchaseStockAnalysisController` predates or otherwise missed this convention: it returns `BadRequest(ModelState)`, which serializes ASP.NET Core's default `ValidationProblemDetails` shape instead. This was flagged by the automated arch-review routine (issue #4199) as an inconsistency versus the established pattern already used in `PurchaseOrdersController.cs` (e.g. `CreatePurchaseOrder`, `UpdatePurchaseOrder`, `RecalculatePurchasePrice`).

`GetPurchaseStockAnalysisResponse` already derives from `BaseResponse` (confirmed in `GetPurchaseStockAnalysisResponse.cs`), so no data-model change is needed — only the controller's error-construction call.

The only current source of model validation failure on this endpoint is `[Range(1, 100)]` on `GetPurchaseStockAnalysisRequest.PageSize`.

## Functional Requirements

### FR-1: Return standardized validation error envelope
When `ModelState.IsValid` is `false` in `PurchaseStockAnalysisController.GetStockAnalysis`, the action must return `BadRequest(ErrorResponseHelper.CreateValidationError<GetPurchaseStockAnalysisResponse>())` instead of `BadRequest(ModelState)`.

**Acceptance criteria:**
- `PurchaseStockAnalysisController.cs` line 26 (or its current location after the change) calls `ErrorResponseHelper.CreateValidationError<GetPurchaseStockAnalysisResponse>()`, matching the pattern in `PurchaseOrdersController.cs`.
- A request to `GET /api/purchase-stock-analysis` with `PageSize` outside `[1, 100]` returns HTTP 400 with a JSON body shaped as `GetPurchaseStockAnalysisResponse` where `Success == false` and `ErrorCode == ErrorCodes.ValidationError`, rather than an ASP.NET Core `ValidationProblemDetails` object.
- No behavior changes for valid requests (the success path via `_mediator.Send` / `HandleResponse` is untouched).

## Non-Functional Requirements

### NFR-1: Performance
None — this is a like-for-like control-flow substitution with no added work in the request path.

### NFR-2: Security
None — no change to authorization (`FeatureAuthorize` remains untouched) or to what data is exposed. If anything, the change reduces information disclosure slightly by no longer serializing raw ASP.NET Core `ModelState` internals.

## Data Model
No changes. `GetPurchaseStockAnalysisResponse` already extends `BaseResponse` (`Success`, `ErrorCode`, `Params`) and requires no new fields.

## API / Interface Design
- Endpoint: `GET /api/purchase-stock-analysis` (unchanged route, verb, and success-path response shape).
- Error-path response body changes shape only when validation fails: from ASP.NET Core's default `ValidationProblemDetails` to `GetPurchaseStockAnalysisResponse { Success = false, ErrorCode = ErrorCodes.ValidationError, Params = null }`, matching the shape already returned by other Purchase endpoints (e.g. `POST /api/purchase-orders` on validation failure).
- HTTP status code for the failure case remains 400 (`BadRequest`).

## Dependencies
- `Anela.Heblo.API.Infrastructure.ErrorResponseHelper` (`CreateValidationError<T>()`) — already present and used elsewhere in the codebase; no new dependency introduced.
- No frontend changes are required for this fix itself, since the frontend's typed OpenAPI client and error handling already expect the `BaseResponse` shape; however, since the wire shape of the 400 response for this specific endpoint changes, any test or manual verification relying on the old raw `ModelState` shape should be updated (see Open Questions).

## Out of Scope
- No other controllers are touched. This spec addresses only `PurchaseStockAnalysisController`.
- No changes to `ErrorResponseHelper` itself.
- No changes to the OpenAPI-generated TypeScript client beyond what regenerates automatically from the existing `GetPurchaseStockAnalysisResponse` type (the type itself does not change).
- No new automated regression test is mandated beyond what the architect/planner phases decide is warranted for a one-line change (see Open Questions).

## Open Questions
None.
