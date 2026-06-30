# Design: Analytics – Remove Internal Exception Messages from Error Responses

## Component Design

### Affected handlers and tiles

Three files require identical structural changes: inject `ILogger<T>`, add a `using Microsoft.Extensions.Logging;` directive, and replace the leaking catch block with a logging call followed by an opaque error return.

**GetMarginReportHandler**
Path: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs`

- Add constructor parameter `ILogger<GetMarginReportHandler> logger`; assign to `_logger`.
- In `catch (Exception ex)`: call `_logger.LogError(ex, "Unhandled exception in GetMarginReportHandler")`, then return `CreateErrorResponse(ErrorCodes.InternalServerError)` with no additional parameters.

**GetProductMarginAnalysisHandler**
Path: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs`

- Same injection and catch-block change as above; log message: `"Unhandled exception in GetProductMarginAnalysisHandler"`.

**InvoiceImportStatisticsTile**
Path: `backend/src/Anela.Heblo.Application/Features/Analytics/DashboardTiles/InvoiceImportStatisticsTile.cs`

- Same injection pattern.
- In `catch (Exception ex)`: call `_logger.LogError(ex, "Unhandled exception in InvoiceImportStatisticsTile")`, then return `new { status = "error", error = "Nepodařilo se načíst statistiky importu faktur" }` — omitting the `details` property entirely.

### Unchanged components

- `CreateErrorResponse` helper — signature unchanged; the call simply drops the `("details", ex.Message)` argument tuple.
- `BaseResponse` / `GetMarginReportResponse` / `GetProductMarginAnalysisResponse` — no changes.
- `ErrorCodes.InternalServerError` — no changes.

## Data Schemas

No database or domain model changes.

### API response shapes (error case only)

**GetMarginReport and GetProductMarginAnalysis endpoints**

Before:
```json
{ "success": false, "errorCode": 10, "params": { "details": "<raw exception text>" } }
```

After:
```json
{ "success": false, "errorCode": 10, "params": null }
```

**InvoiceImportStatisticsTile data**

Before:
```json
{ "status": "error", "error": "Nepodařilo se načíst statistiky importu faktur", "details": "<raw exception text>" }
```

After:
```json
{ "status": "error", "error": "Nepodařilo se načíst statistiky importu faktur" }
```

### Structured log entry (all three sites)

Level: `LogError`
Template: `"Unhandled exception in {HandlerName}"` (class name substituted per site)
Exception object passed as first argument so stack trace is captured by the logging provider.
