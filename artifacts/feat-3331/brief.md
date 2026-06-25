## Module
Analytics

## Finding
Three locations catch `Exception` and include `ex.Message` verbatim in the response payload returned to callers:

- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs:74`
  ```csharp
  catch (Exception ex)
  {
      return CreateErrorResponse(ErrorCodes.InternalServerError, ("details", ex.Message));
  }
  ```
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs:61–63` — identical pattern
- `backend/src/Anela.Heblo.Application/Features/Analytics/DashboardTiles/InvoiceImportStatisticsTile.cs:87`
  ```csharp
  catch (Exception ex)
  {
      return new { status = "error", error = "...", details = ex.Message };
  }
  ```

## Why it matters
`ex.Message` from unhandled infrastructure exceptions regularly contains sensitive internal detail: PostgreSQL connection strings, EF Core query text, stack-frame snippets from Npgsql, internal type names, file paths. Forwarding this to authenticated browser clients violates OWASP A05 (Security Misconfiguration / information leakage). It also couples the frontend contract to internal error text that changes between library versions.

## Suggested fix
- Log the full exception server-side (structured logging — `ILogger.LogError(ex, "...")`)
- Return a fixed, opaque string (e.g. `"An unexpected error occurred."`) or no `details` field at all
- Remove the `("details", ex.Message)` parameter from `CreateErrorResponse` at all three call sites
- The `ErrorCodes.InternalServerError` value and `Params` dictionary remain; only the `ex.Message` string is removed

---
_Filed by daily arch-review routine on 2026-06-24._
