# Specification: Analytics – Remove Internal Exception Messages from Error Responses

## Summary

Three catch blocks in the Analytics module forward raw `ex.Message` strings into API response payloads sent to browser clients. This exposes internal infrastructure details (PostgreSQL connection strings, EF Core query text, Npgsql stack frames, file paths) to authenticated users, violating OWASP A05. The fix logs the full exception server-side using structured logging and replaces the client-facing payload with an opaque error code, matching the pattern already established in the rest of the codebase.

## Background

The Analytics module contains three catch blocks that pass `ex.Message` verbatim into the response body:

1. `GetMarginReportHandler` (line 74) — uses the shared `CreateErrorResponse` helper with a `("details", ex.Message)` parameter tuple, producing `Params["details"] = <raw exception text>` in a `GetMarginReportResponse : BaseResponse`.
2. `GetProductMarginAnalysisHandler` (lines 61–63) — identical pattern, same helper method, same `BaseResponse`-derived type.
3. `InvoiceImportStatisticsTile` (line 74) — returns an anonymous `object` with a `details = ex.Message` property directly (this tile does not use `BaseResponse`).

None of these three files inject `ILogger`, so no server-side record of the failure currently exists.

The remainder of the application already follows the correct pattern, exemplified by `GetDqtRunDetailHandler`: inject `ILogger<T>`, call `_logger.LogError(ex, "…")` in the catch block, and return `ErrorCode = ErrorCodes.Exception` with no exception text in `Params`.

## Functional Requirements

### FR-1: Remove ex.Message from GetMarginReportHandler error response

Remove the `("details", ex.Message)` argument from the `CreateErrorResponse` call in the `catch (Exception ex)` block (line 74). The call must remain `CreateErrorResponse(ErrorCodes.InternalServerError)` with no extra parameters, so `Params` is null or empty in the serialised response.

**Acceptance criteria:**
- The serialised JSON response for an unhandled exception no longer contains a `"details"` key or any `"params"` entry sourced from `ex.Message`.
- `ErrorCode` in the response is `ErrorCodes.InternalServerError` (value 10).
- A structured log entry at `LogError` level is written, including the full exception object as the first argument.
- The log message template includes enough context to identify the failing handler (e.g., `"Unhandled exception in GetMarginReportHandler"`).

### FR-2: Remove ex.Message from GetProductMarginAnalysisHandler error response

Apply the same change to `GetProductMarginAnalysisHandler` (lines 61–63).

**Acceptance criteria:**
- Identical criteria to FR-1, with `GetProductMarginAnalysisHandler` as the handler name in the log message.
- The `("details", ex.Message)` parameter tuple is removed from `CreateErrorResponse`.

### FR-3: Remove ex.Message from InvoiceImportStatisticsTile error response

Replace the anonymous object `new { status = "error", error = "...", details = ex.Message }` with an anonymous object that omits the `details` field entirely. The `error` field (localised Czech string `"Nepodařilo se načíst statistiky importu faktur"`) must be retained, as it is user-facing and does not originate from the exception.

**Acceptance criteria:**
- The serialised JSON response for an unhandled exception contains `status = "error"` and `error = "Nepodařilo se načíst statistiky importu faktur"` but does not contain a `details` key.
- A structured log entry at `LogError` level is written, including the full exception object as the first argument.
- The log message template identifies the tile (e.g., `"Unhandled exception in InvoiceImportStatisticsTile"`).

### FR-4: Add ILogger injection to the three affected classes

All three classes currently lack `ILogger`. Each must receive `ILogger<T>` (where `T` is the class) via constructor injection, following the existing pattern seen throughout the codebase.

**Acceptance criteria:**
- Each class declares a `private readonly ILogger<T> _logger` field.
- The constructor accepts `ILogger<T> logger` and assigns it.
- No changes to the DI registration are required (ASP.NET Core's built-in container resolves `ILogger<T>` automatically).

## Non-Functional Requirements

### NFR-1: No functional regression

The fix is purely defensive; it must not alter the HTTP status code, the `ErrorCode` value, or any other field in the success path.

**Acceptance criteria:**
- `dotnet build` passes without warnings in the Analytics feature folder.
- All existing unit/integration tests for the three affected handlers pass unchanged.

### NFR-2: Consistency with existing error-handling pattern

The resulting code must be indistinguishable in structure from the established pattern in handlers such as `GetDqtRunDetailHandler`, `GetDqtRunsHandler`, and `RunDqtHandler`.

### NFR-3: Security

After the change, no infrastructure exception text must appear in any field of the HTTP response body for the three affected endpoints/tiles, under any failure mode reachable via the `catch (Exception ex)` block.

## Data Model

No data model changes. The existing `BaseResponse.Params` dictionary remains; it simply must not be populated with exception text. `InvoiceImportStatisticsTile` returns an anonymous object; its shape changes only by removing the `details` property.

## API / Interface Design

### GetMarginReport endpoint

**Before (error case):**
```json
{
  "success": false,
  "errorCode": 10,
  "params": { "details": "<raw PostgreSQL / EF exception text>" }
}
```

**After (error case):**
```json
{
  "success": false,
  "errorCode": 10,
  "params": null
}
```

### GetProductMarginAnalysis endpoint

Same before/after shape as GetMarginReport.

### InvoiceImportStatisticsTile data

**Before (error case):**
```json
{
  "status": "error",
  "error": "Nepodařilo se načíst statistiky importu faktur",
  "details": "<raw exception text>"
}
```

**After (error case):**
```json
{
  "status": "error",
  "error": "Nepodařilo se načíst statistiky importu faktur"
}
```

## Dependencies

- `Microsoft.Extensions.Logging.ILogger<T>` — already a transitive dependency via the host; no new NuGet packages required.
- The existing `ErrorCodes.InternalServerError` enum member — already defined (`= 0010`).
- `CreateErrorResponse` private helper in both handlers — signature already accepts zero extra parameters (the `params` keyword makes them optional); no signature change is needed.

## Out of Scope

- Changes to any other catch block outside the three files named in the brief.
- Frontend changes — the frontend must already handle the `errorCode`-only response gracefully (the `params` field is nullable in `BaseResponse`). If the frontend currently reads `params.details` to display a message, that is a separate issue outside this fix.
- Adding a correlation-ID or request-ID to the response envelope.
- Switching `InvoiceImportStatisticsTile` from anonymous objects to a typed `BaseResponse` subclass — that would be a broader refactor outside the stated scope.
- Changing the `ErrorCodes.InternalServerError` value used by these handlers; the brief explicitly states it should remain.
- `BaseResponse(Exception ex)` constructor (lines 44–53 of `BaseResponse.cs`) — this constructor also places `ex.Message` in `Params["ErrorMessage"]`, but it is not called by any of the three affected files and is outside the scope of this fix.

## Open Questions

None.

## Status: COMPLETE
