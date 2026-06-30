# Implementation: merge-named-client-into-typed-client

## What was implemented

Moved the resilience pipeline (retry + per-attempt timeout) from the named `"ShoptetStockCsv"` HTTP client registration onto the typed `IEshopStockClient` / `ShoptetStockClient` registration. Removed `IHttpClientFactory` from `ShoptetStockClient`'s constructor and field set. `ListAsync` now uses the already-injected `_http` typed client directly.

Changes per part:

**ShoptetStockClient.cs**
- Removed `private readonly IHttpClientFactory _httpClientFactory;` field
- Removed `IHttpClientFactory httpClientFactory` constructor parameter
- Changed `var client = _httpClientFactory.CreateClient("ShoptetStockCsv");` → `var client = _http;`

**ShoptetApiAdapterServiceCollectionExtensions.cs**
- Added `.AddResilienceHandler("shoptet-stock-csv", (builder, context) => ...)` onto the typed `AddHttpClient<IEshopStockClient, ShoptetStockClient>` registration, matching the existing 2-arg lambda style used by the original named client handler
- Added `client.Timeout = Timeout.InfiniteTimeSpan` so the typed client does not have a short socket-level timeout that conflicts with Polly's per-attempt timeout
- Deleted the entire `services.AddHttpClient("ShoptetStockCsv", ...)` block including its `.AddResilienceHandler`

**ShoptetStockClientTests.cs**
- Removed `Mock<IHttpClientFactory>` from `BuildClient` helper
- Rewrote `BuildClientForCsv` helper to use the HTTP handler directly as the primary `HttpClient` (no factory mock needed)
- Removed unused `using Moq;` directive

**ShoptetStockClientResilienceTests.cs**
- Changed `services.AddHttpClient("ShoptetStockCsv").ConfigurePrimaryHttpMessageHandler(...)` → `services.AddHttpClient<IEshopStockClient, ShoptetStockClient>().ConfigurePrimaryHttpMessageHandler(...)` so resilience test handler overrides the typed client's handler

## Files created/modified

- Modified: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs`
- Modified: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs`
- Modified: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetStockClientTests.cs`
- Modified: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetStockClientResilienceTests.cs`

## How to verify

```bash
cd /home/user/worktrees/feature-3193-socket-exception-polly
dotnet build -v q 2>&1 | grep "^Build"
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetStockClient" -v n --no-restore
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetStockClientResilience" -v n --no-restore
```

Expected: build succeeds, all 15 unit tests in `Anela.Heblo.Tests` pass, all 5 unit resilience tests + 6 RedactToken theory cases in `Anela.Heblo.Adapters.Shoptet.Tests.Unit` pass. (3 integration tests in `ShoptetStockClientIntegrationTests` fail by design — they require live Shoptet credentials not present in the dev environment.)

## Notes

- The CSV URL from `_stockClientOptions.Value.Url` is absolute, so passing it to `_http.GetAsync(url)` overrides the typed client's `BaseAddress`. This is correct behaviour.
- `Timeout.InfiniteTimeSpan` is set on the typed client so the socket-level timeout does not race with Polly's per-attempt timeout strategy.
- The named client `"ShoptetStockCsv"` no longer exists in the DI container; any code that previously resolved it via `IHttpClientFactory.CreateClient("ShoptetStockCsv")` would now need to use the typed client, but there were no other callers.

## Status
DONE
