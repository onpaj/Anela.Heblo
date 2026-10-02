# Design: Standardize validation error response in PurchaseStockAnalysisController

## Component Design

### `PurchaseStockAnalysisController.GetStockAnalysis` (existing action, modified)
- **Responsibility (unchanged):** Handle `GET /api/purchase-stock-analysis`, validate the bound `GetPurchaseStockAnalysisRequest`, delegate to `IMediator` for the actual query, and translate the `GetPurchaseStockAnalysisResponse` into an `ActionResult`.
- **Change:** In the `!ModelState.IsValid` branch, construct the 400 body via `ErrorResponseHelper.CreateValidationError<GetPurchaseStockAnalysisResponse>()` instead of passing `ModelState` directly to `BadRequest`.
- **Collaborators (unchanged):** `IMediator` (query dispatch), `HandleResponse` (inherited from `BaseApiController`, used only on the success/handler-response path), `ErrorResponseHelper` (existing static helper, now used here for the first time in this controller).

No new components, classes, or files are introduced; no component boundaries change.

## Data Schemas

### Request (unchanged)
`GetPurchaseStockAnalysisRequest` — unchanged. The only field with a validation attribute is `PageSize` (`[Range(1, 100)]`), which remains the sole trigger for the `ModelState.IsValid == false` branch.

### Response — validation-failure shape (changed)

Before:
```json
// ASP.NET Core default ValidationProblemDetails, from BadRequest(ModelState)
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "PageSize": ["The field PageSize must be between 1 and 100."]
  },
  "traceId": "..."
}
```

After (matches every other Purchase endpoint's validation-failure shape):
```json
// GetPurchaseStockAnalysisResponse, from BadRequest(ErrorResponseHelper.CreateValidationError<GetPurchaseStockAnalysisResponse>())
{
  "success": false,
  "errorCode": "ValidationError",
  "params": null,
  "items": [],
  "totalCount": 0,
  "pageNumber": 0,
  "pageSize": 0,
  "summary": { "totalProducts": 0, "criticalCount": 0, "lowStockCount": 0, "optimalCount": 0, "overstockedCount": 0, "notConfiguredCount": 0, "totalInventoryValue": 0, "analysisPeriodStart": "0001-01-01T00:00:00", "analysisPeriodEnd": "0001-01-01T00:00:00" }
}
```
(Exact JSON casing/serialization follows the project's existing global serializer settings, matching the shape already returned by `PurchaseOrdersController`'s validation-failure responses.)

HTTP status code remains `400 Bad Request` in both cases. No changes to the success-path response shape (`Success == true`) or to any database schema, since this endpoint's underlying query and data access are untouched.
