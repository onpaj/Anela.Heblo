## Telemetry

App Insights detected 1 dependency failure in the last 24h:

- **Dependency**: `POST /c/kopie_souboru_anela_cosmetics_s_r_o__2020_09_2/skladovy-pohyb-polozka/query`
- **Type**: HTTP → `petra-tesarikova.flexibee.eu`
- **Result code**: `503` (Service Unavailable)
- **Count**: 1

## Root Cause

`FlexiManufactureHistoryClient.GetHistoryAsync()` calls the FlexiBee `skladovy-pohyb-polozka` (stock movement items) endpoint via `IStockItemsMovementClient`. When FlexiBee returns `503 Service Unavailable` (transient overload), the exception propagates unhandled up through `GetManufactureOutputHandler` to the user.

The client handles `OperationCanceledException` (timeout/cancel) but has no specific handling for transient HTTP errors like 503.

Compare with `FlexiStockClient` which has structured handling for `HttpRequestException` (501 NotImplemented) with descriptive log messages.

## Affected Code

- `FlexiManufactureHistoryClient.cs` — missing `HttpRequestException` handling for transient 5xx responses
- `GetManufactureOutputHandler.cs` — calls `GetHistoryAsync` without any fallback

## Suggested Fix

Add a `catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)` block in `FlexiManufactureHistoryClient.GetHistoryAsync()` with a descriptive log warning (similar to how `FlexiStockClient` handles 501). Optionally consider a simple retry policy (1-2 retries with short delay) via Polly for transient FlexiBee 5xx errors, as FlexiBee occasionally becomes temporarily unavailable.