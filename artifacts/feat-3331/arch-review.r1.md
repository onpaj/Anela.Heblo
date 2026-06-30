# Architecture Review: Analytics – Remove Internal Exception Messages from Error Responses

## Skip Design: true

## Architectural Fit Assessment

This change aligns directly with the established handler pattern used throughout the codebase. The rest of the application — `GetDqtRunDetailHandler`, `GetDqtRunsHandler`, `RunDqtHandler`, and many others — already follows the correct pattern: inject `ILogger<T>` via constructor, call `_logger.LogError(ex, "…")` in the catch block, and return an error response containing only an `ErrorCode` with no exception text in `Params`.

The three affected files are outliers that were written without the logger and incorrectly forwarded `ex.Message` into the response payload. No architectural change is needed — this is a conformance fix that brings three files into alignment with a pattern that already exists everywhere else.

The only structural difference worth noting: `InvoiceImportStatisticsTile` returns an anonymous `object` (the `ITile.LoadDataAsync` contract returns `Task<object>`) rather than a `BaseResponse`-derived type, so it cannot use the shared `CreateErrorResponse` helper. The fix for that file is narrower — remove the `details` property from the anonymous error object. This difference is already captured in the spec and requires no further accommodation.

Integration points:
- `Microsoft.Extensions.Logging.ILogger<T>` — already a transitive dependency of the host; no new package references.
- `ErrorCodes.InternalServerError` (value `0010`, HTTP 500) — already defined in `Shared/ErrorCodes.cs` and used by both handlers. The spec requires this value to be preserved.
- `CreateErrorResponse(ErrorCodes errorCode, params (string key, string value)[] parameters)` — private static helper in both handler classes. Its `params` signature makes extra arguments optional; no signature change is needed.
- `BaseResponse.Params` — `Dictionary<string, string>?`; already nullable; passing no extra parameters causes it to serialize as `null`, which is the desired after-state.

## Proposed Architecture

### Component Overview

```
GetMarginReportHandler
  + ILogger<GetMarginReportHandler> _logger   [ADD]
  catch (Exception ex)
    - was: CreateErrorResponse(ErrorCodes.InternalServerError, ("details", ex.Message))
    + now: _logger.LogError(ex, "Unhandled exception in GetMarginReportHandler")
            CreateErrorResponse(ErrorCodes.InternalServerError)

GetProductMarginAnalysisHandler
  + ILogger<GetProductMarginAnalysisHandler> _logger   [ADD]
  catch (Exception ex)
    - was: CreateErrorResponse(ErrorCodes.InternalServerError, ("details", ex.Message))
    + now: _logger.LogError(ex, "Unhandled exception in GetProductMarginAnalysisHandler")
            CreateErrorResponse(ErrorCodes.InternalServerError)

InvoiceImportStatisticsTile
  + ILogger<InvoiceImportStatisticsTile> _logger   [ADD]
  catch (Exception ex)
    - was: new { status = "error", error = "...", details = ex.Message }
    + now: _logger.LogError(ex, "Unhandled exception in InvoiceImportStatisticsTile")
            new { status = "error", error = "Nepodařilo se načíst statistiky importu faktur" }
```

All three changes are leaf-level within their respective files. No callers, no shared infrastructure, no DI registrations are touched.

### Key Design Decisions

#### Decision 1: Preserve ErrorCodes.InternalServerError (not ErrorCodes.Exception)

**Options considered:**
- Keep `ErrorCodes.InternalServerError` (value `0010`) as currently used in the two handlers.
- Switch to `ErrorCodes.Exception` (value `0099`) to match the DataQuality handler pattern (`GetDqtRunDetailHandler`, `GetDqtRunsHandler`).

**Chosen approach:** Preserve `ErrorCodes.InternalServerError`. Do not change the error code value.

**Rationale:** The spec explicitly states the `ErrorCode` value must not change (NFR-1, Out of Scope section). The frontend may already interpret `0010` vs `0099` differently. This fix is about removing information leakage, not normalising error codes. A separate clean-up could unify error codes later, but it is out of scope here.

#### Decision 2: No change to CreateErrorResponse helper signature

**Options considered:**
- Remove the `params (string key, string value)[] parameters` parameter from the private `CreateErrorResponse` helper since it will no longer be called with extra params.
- Leave the signature unchanged.

**Chosen approach:** Leave the helper signature unchanged.

**Rationale:** `CreateErrorResponse` is called with legitimate `Params` in the non-exception error paths (e.g., `AnalysisDataNotAvailable` with product/period context, `ProductNotFoundForAnalysis` with productId). Removing the parameter would require changing those call sites, which is outside the scope of this fix.

#### Decision 3: No change to InvoiceImportStatisticsTile response shape beyond removing details

**Options considered:**
- Convert `InvoiceImportStatisticsTile` to use a typed `BaseResponse`-derived class for error cases.
- Only remove the `details` property from the existing anonymous object.

**Chosen approach:** Remove only the `details` property. Keep the anonymous object pattern.

**Rationale:** Switching to `BaseResponse` would change the serialised response shape seen by the frontend and is explicitly listed as out of scope. The `ITile` contract returns `Task<object>`, so anonymous objects are an accepted pattern in this location.

#### Decision 4: Log message template

**Options considered:**
- Generic message: `"Unhandled exception in {HandlerName}"` with a string literal for the handler name.
- Contextual message: include request parameters (e.g., product ID, date range) in the log template.

**Chosen approach:** Use a simple literal string with the class name embedded, following the brevity of `GetDqtRunsHandler` (`"Error getting DQT runs"`). No request parameters in the message template.

**Rationale:** Structured context (product ID, date range) is already available in the request object. Adding it would require non-trivial changes to capture request state in the catch block. The exception object itself — passed as the first argument to `LogError` — already carries the full stack trace and exception type, which is sufficient for diagnosis. Keep the change minimal.

## Implementation Guidance

### Directory / Module Structure

No new files. Changes are confined to three existing files:

```
backend/src/Anela.Heblo.Application/Features/Analytics/
  UseCases/GetMarginReport/GetMarginReportHandler.cs          [MODIFY]
  UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs  [MODIFY]
  DashboardTiles/InvoiceImportStatisticsTile.cs               [MODIFY]
```

### Interfaces and Contracts

**ILogger<T> injection pattern** — copy exactly from `GetDqtRunDetailHandler`:

```csharp
// Field
private readonly ILogger<T> _logger;

// Constructor parameter (appended after existing parameters)
ILogger<T> logger

// Assignment in constructor body
_logger = logger;
```

The `using Microsoft.Extensions.Logging;` directive must be added to the using block of all three files (currently absent from all three).

**Logging call in catch block:**

```csharp
_logger.LogError(ex, "Unhandled exception in GetMarginReportHandler");
```

The exception object is the first argument. The message string is a literal — no interpolation, no structured properties beyond what the exception itself carries.

**GetMarginReportHandler and GetProductMarginAnalysisHandler — catch block after:**

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Unhandled exception in GetMarginReportHandler");
    return CreateErrorResponse(ErrorCodes.InternalServerError);
}
```

**InvoiceImportStatisticsTile — catch block after:**

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Unhandled exception in InvoiceImportStatisticsTile");
    return new
    {
        status = "error",
        error = "Nepodařilo se načíst statistiky importu faktur"
    };
}
```

### Data Flow

**Before (error path):**
```
Exception thrown in repository/service
  → caught in handler catch block
  → ex.Message copied into Params dictionary / anonymous object
  → serialised JSON includes "details": "<raw exception text>"
  → returned to browser client
  → no server-side log entry
```

**After (error path):**
```
Exception thrown in repository/service
  → caught in handler catch block
  → _logger.LogError(ex, "...") writes full exception to structured log (Azure Monitor)
  → response created with ErrorCode only, no exception text
  → serialised JSON: { "success": false, "errorCode": 10, "params": null }
  → returned to browser client
```

The success path is completely unchanged in all three files.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Frontend currently reads `params.details` to display diagnostic text to the user | Low | The spec states `params` is already nullable in `BaseResponse` and the frontend must handle the null case. Verify by grep for `params?.details` or `params.details` in frontend code before merging. |
| `BaseResponse(Exception ex)` constructor (line 44–53 of `BaseResponse.cs`) also leaks `ex.Message` via `Params["ErrorMessage"]` | Medium | This constructor is not called by the three affected files and is explicitly out of scope. It should be addressed as a follow-up. Document as a known issue. |
| ILogger<T> constructor parameter order breaks an existing test that manually constructs the handler | Low | Check for unit tests that `new` the handler directly. If any exist, add the logger parameter using `NullLogger<T>.Instance`. |
| Silent regression: `ErrorCodes.InternalServerError` vs `ErrorCodes.Exception` | Low | No change to error code values in this fix; no regression possible on that axis. |

## Specification Amendments

None. The spec is complete and accurate. One observation to carry forward as a separate issue (not blocking this fix):

- `BaseResponse(Exception ex)` constructor at `Shared/BaseResponse.cs` lines 44–53 places `ex.Message` and `ex.ToString()` into `Params["ErrorMessage"]` and `Params["ExceptionType"]`. No handler in the three affected files calls this constructor, but other handlers in the codebase might. This should be audited in a follow-up task.

## Prerequisites

None. All required types (`ILogger<T>`, `ErrorCodes.InternalServerError`, `CreateErrorResponse`) already exist. No migrations, no config changes, no new NuGet packages, no DI registration changes are required. Implementation can start immediately.
