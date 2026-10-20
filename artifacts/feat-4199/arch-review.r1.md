# Architecture Review: Standardize validation error response in PurchaseStockAnalysisController

## Skip Design: true

## Architectural Fit Assessment
This is a pure backend consistency fix with no UI/UX surface. It aligns `PurchaseStockAnalysisController.GetStockAnalysis` with the project's existing, already-dominant convention: every action that performs `ModelState.IsValid` validation and needs to return a 400 should build its error body via `Anela.Heblo.API.Infrastructure.ErrorResponseHelper.CreateValidationError<TResponse>()` rather than passing the raw ASP.NET Core `ModelState` object to `BadRequest`. `PurchaseOrdersController` (verified directly, `CreatePurchaseOrder`, `UpdatePurchaseOrder`, `UpdatePurchaseOrderStatus`, `RecalculatePurchasePrice`) already does this consistently. `GetPurchaseStockAnalysisResponse` already extends `BaseResponse` (confirmed in `GetPurchaseStockAnalysisResponse.cs`), so it satisfies `ErrorResponseHelper`'s `where T : BaseResponse, new()` constraint with zero type changes. The fit is exact — this is a one-line substitution, not a redesign.

Note for the record (out of scope for this fix, do not touch): a grep across `backend/src/Anela.Heblo.API/Controllers/` shows the same `BadRequest(ModelState)` anti-pattern also present in `ManufacturingStockAnalysisController.cs` (line 28) and `PackingMaterialsController.cs` (lines 51, 78, 99, 139). These are separate arch-review findings and must not be bundled into this issue's PR — flagging here only so a future arch-review pass doesn't need to rediscover them.

## Proposed Architecture

### Component Overview
No new components. Single call-site change inside an existing MVC controller action:

```
HTTP GET /api/purchase-stock-analysis
        |
        v
PurchaseStockAnalysisController.GetStockAnalysis
        |
        +-- ModelState.IsValid? --false--> BadRequest(ErrorResponseHelper.CreateValidationError<GetPurchaseStockAnalysisResponse>())
        |                                   (was: BadRequest(ModelState))
        |
        +-- true --> IMediator.Send(request) --> GetPurchaseStockAnalysisHandler --> HandleResponse(response)
```

### Key Design Decisions

#### Decision 1: Reuse `ErrorResponseHelper.CreateValidationError<T>()` as-is
**Options considered:**
- (a) Call `ErrorResponseHelper.CreateValidationError<GetPurchaseStockAnalysisResponse>()` with no field name, mirroring `PurchaseOrdersController.CreatePurchaseOrder`.
- (b) Call it with a specific field name (e.g. `"PageSize"`) to pinpoint which field failed.
- (c) Introduce a new, endpoint-specific error helper.

**Chosen approach:** (a) — no field name, exactly matching every existing call site in `PurchaseOrdersController`.

**Rationale:** `PurchaseOrdersController`'s own calls to `CreateValidationError<T>()` never pass a field name (see `CreatePurchaseOrder`, `UpdatePurchaseOrder`, `RecalculatePurchasePrice` — all call it with zero arguments), even though several of those requests have multiple validatable fields. Matching that exact call shape keeps this fix a literal drop-in replacement, consistent with "surgical changes" — it does not invent a new, richer error contract that no other Purchase endpoint uses. (b) would improve diagnosability but is a scope expansion beyond the filed finding and would make this endpoint's error payload inconsistent with its siblings. (c) is unwarranted — the existing helper already fits perfectly.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Single edit to:
- `backend/src/Anela.Heblo.API/Controllers/PurchaseStockAnalysisController.cs`

### Interfaces and Contracts
- No new interfaces or types.
- `ErrorResponseHelper.CreateValidationError<T>(string? fieldName = null) where T : BaseResponse, new()` (existing, in `backend/src/Anela.Heblo.API/Infrastructure/ErrorResponseHelper.cs`) is the only API surface touched, and only as a caller.
- `GetPurchaseStockAnalysisResponse : BaseResponse` (existing, unchanged) satisfies the generic constraint.

### Data Flow
Unchanged except for the validation-failure branch. On invalid `ModelState` (currently only reachable via `PageSize` outside `[Range(1, 100)]`), the controller now constructs a `GetPurchaseStockAnalysisResponse` with `Success = false`, `ErrorCode = ErrorCodes.ValidationError`, and no `Params`, and returns it via `BadRequest(...)`. The success path (`_mediator.Send` → `GetPurchaseStockAnalysisHandler` → `HandleResponse`) is completely untouched.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A test or client relies on the old raw `ModelState` (`ValidationProblemDetails`) response shape for this endpoint | Low | Search `backend/test/` for any test asserting on the old shape for this endpoint before merging; update if found. None were found during this review's exploration. |
| Frontend TypeScript client has a stale generated type for the 400 response of this endpoint | Very Low | The OpenAPI client is regenerated on build per project convention; `GetPurchaseStockAnalysisResponse`'s shape itself does not change, only which code path is hit, so no client regeneration is functionally required, though normal build-time regeneration will still pick it up. |
| Scope creep — “fixing” the same pattern in `ManufacturingStockAnalysisController` or `PackingMaterialsController` while in the file | Low | Explicitly out of scope (see Architectural Fit Assessment); planner must scope the task to this one file/line only. |

## Specification Amendments
None. The spec (`spec.r1.md`) is accurate and requires no changes.

## Prerequisites
None. No migrations, config, or infrastructure changes are needed — this is a same-file, same-build code change.
