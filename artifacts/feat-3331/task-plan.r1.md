# Task Plan: Analytics – Remove Internal Exception Messages from Error Responses

## Overview

Three catch blocks in the Analytics module expose raw `ex.Message` strings in API response payloads. This task injects `ILogger<T>` into each of the three affected classes, logs the exception server-side, and removes the exception message from the response.

## Tasks

### task: fix-exception-leakage

**Goal**
Fix the three catch blocks that expose `ex.Message` in API responses.

**Files to modify**
1. `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs`
2. `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs`
3. `backend/src/Anela.Heblo.Application/Features/Analytics/DashboardTiles/InvoiceImportStatisticsTile.cs`

**Changes per file**

For GetMarginReportHandler and GetProductMarginAnalysisHandler:
1. Add `using Microsoft.Extensions.Logging;` to the using block.
2. Add `private readonly ILogger<T> _logger;` field (where T is the handler class).
3. Add `ILogger<T> logger` as constructor parameter and assign `_logger = logger;` in the constructor body.
4. In the `catch (Exception ex)` block: replace
   `return CreateErrorResponse(ErrorCodes.InternalServerError, ("details", ex.Message));`
   with:
   ```csharp
   _logger.LogError(ex, "Unhandled exception in {HandlerName}");
   return CreateErrorResponse(ErrorCodes.InternalServerError);
   ```
   (substitute the actual class name for {HandlerName})

For InvoiceImportStatisticsTile:
1. Add `using Microsoft.Extensions.Logging;` to the using block.
2. Add `private readonly ILogger<InvoiceImportStatisticsTile> _logger;` field.
3. Add `ILogger<InvoiceImportStatisticsTile> logger` as constructor parameter and assign.
4. In the `catch (Exception ex)` block: replace
   `return new { status = "error", error = "Nepodařilo se načíst statistiky importu faktur", details = ex.Message };`
   with:
   ```csharp
   _logger.LogError(ex, "Unhandled exception in InvoiceImportStatisticsTile");
   return new { status = "error", error = "Nepodařilo se načíst statistiky importu faktur" };
   ```

**Verification**
- Run `dotnet build` from `backend/` — must succeed with no errors.
- Run `dotnet format --verify-no-changes` or `dotnet format` to check formatting.
- No other files should be modified.
