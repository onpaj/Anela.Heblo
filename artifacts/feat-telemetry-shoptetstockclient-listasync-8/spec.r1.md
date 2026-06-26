I have enough context. Writing the spec now.

```markdown
# Specification: Harden ShoptetStockClient.ListAsync against transient HTTP failures

## Summary
The `ShoptetStockClient.ListAsync` method throws ~1.1 unhandled `HttpRequestException`s per day when downloading the Shoptet stock CSV export. The current implementation creates a default `HttpClient` with no configured timeout, no retry policy, and no structured exception logging, and not every caller wraps the call in the existing `CatalogResilienceService`. This spec defines the work to add resilience, observability, and consistent caller-side wrapping so transient Shoptet failures stop surfacing as application-level exceptions.

## Background
Telemetry for the window 2026-06-05 → 2026-06-12 recorded 8 occurrences of `System.Net.Http.HttpRequestException` at `Anela.Heblo.Adapters.ShoptetApi.Stock.ShoptetStockClient.ListAsync`. Over the same window the dependency tracker reported 1367 calls to `api.myshoptet.com` with **0 logged failures** and a p95 of 241 ms — meaning these failures either hit a different host (the stock CSV export URL is configured separately in `ShoptetStockClientOptions.Url` and is **not** `api.myshoptet.com`) or are connection-level faults that the dependency tracker never recorded as HTTP responses.

Inspection of `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs:41-58` reveals four problems that compound to produce the observed signal:

1. **Wrong HttpClient is used.** Although the class is registered via `AddHttpClient<IEshopStockClient, ShoptetStockClient>` (with `BaseAddress` and `Shoptet-Private-API-Token` configured), `ListAsync` ignores the injected `_http` and calls `_httpClientFactory.CreateClient()` to get a default unnamed client. The default client has no configured timeout (so it inherits the 100 s default), no retry policy, no telemetry hooks, and no auth headers. The CSV URL is an absolute external URL stored in `ShoptetStockClientOptions.Url`, which is why this still functions in practice.
2. **No retry / Polly policy is registered on the HTTP client itself.** `CatalogResilienceService` (Polly v8 `ResiliencePipeline` with 3 retries, circuit breaker, 30 s timeout) exists but is applied at the **caller** layer, not at the HTTP layer.
3. **Inconsistent caller wrapping.** `CatalogDataRefreshService.RefreshEshopStockData` (line 158) wraps `ListAsync` with `_resilienceService.ExecuteWithResilienceAsync(...)` — but `ProductPairingDqtComparer.CompareAsync` (line 23) calls `_eshopStockClient.ListAsync(ct)` directly with no resilience. Any failure on that path is fatal.
4. **Exception context is lost.** `EnsureSuccessStatusCode()` is called with no logging, and the underlying `HttpRequestException.Message` / `InnerException` / status code never reach the logs — so triage cannot distinguish timeout, DNS, TLS, connection-reset, or 5xx without reading raw exception dumps.

The fix is to make resilience a property of the client (so all callers benefit) and to record enough context that the next 8 failures can be triaged in seconds.

## Functional Requirements

### FR-1: Use the typed HttpClient injected into `ShoptetStockClient`
`ListAsync` MUST issue its CSV `GET` through the injected `HttpClient _http` (or a named client registered specifically for the stock CSV export) instead of `_httpClientFactory.CreateClient()`. This puts the request on a code path where Polly handlers, timeouts, and telemetry handlers configured in DI actually apply.

**Acceptance criteria:**
- `ListAsync` does not call `_httpClientFactory.CreateClient()` with no name.
- If a separate client is required because the CSV URL host differs from `BaseAddress`, a **named** `HttpClient` (e.g. `"ShoptetStockCsv"`) is registered in `ShoptetApiAdapterServiceCollectionExtensions` and resolved via `_httpClientFactory.CreateClient("ShoptetStockCsv")`.
- `ShoptetStockClientIntegrationTests.ListAsync_ReturnsStock_ForKnownStore` continues to pass against the staging Shoptet CSV endpoint.

### FR-2: Configure an explicit timeout for the stock CSV request
The CSV download MUST have an explicit, configurable timeout. The current default 100 s allows a slow/hung TCP connection to block a refresh job indefinitely from a user-perception standpoint.

**Acceptance criteria:**
- `ShoptetStockClientOptions` exposes a `TimeoutSeconds` (default: 60) property.
- The named HttpClient registered for the stock CSV download has `Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)`.
- A unit/integration test asserts that a request that exceeds the configured timeout fails with `TaskCanceledException`/`HttpRequestException` (whichever .NET 8 produces for the configured runtime) within `TimeoutSeconds + 5` seconds.

### FR-3: Add a Polly retry policy to the HTTP layer
Transient HTTP failures (`HttpRequestException`, 5xx, 408, 429, timeout) MUST be retried at the HTTP-handler layer using `Microsoft.Extensions.Http.Resilience` (or `Microsoft.Extensions.Http.Polly` already on the dependency graph — same mechanism that `CatalogResilienceService` uses).

**Acceptance criteria:**
- The stock-CSV HttpClient registration adds a retry handler: **3 attempts**, exponential backoff with jitter starting at 1 s, retrying on `HttpRequestException`, transient HTTP errors (5xx, 408, 429), and `TimeoutRejectedException`/`TaskCanceledException` that are not caused by the **outer** `CancellationToken`.
- Each retry attempt emits a `LogWarning` with `OperationName="ShoptetStockClient.ListAsync"`, `AttemptNumber`, `MaxAttempts`, `ExceptionType`, and `ExceptionMessage`.
- The total time across retries is bounded by the per-attempt timeout × max attempts; the outer caller's `CancellationToken` is still honored.
- A unit test using a stub `HttpMessageHandler` proves: (a) 2 transient 503 responses followed by a 200 ultimately succeeds, (b) 4 consecutive 503 responses surface as `HttpRequestException` to the caller, (c) cancellation via the caller token short-circuits retries.

### FR-4: Wrap exceptions with structured logging that captures the failure mode
`ListAsync` MUST log every terminal failure (after retries) with enough structured context to distinguish failure modes without reading stack traces.

**Acceptance criteria:**
- On terminal failure, the client logs at `LogError` with structured fields: `Operation="ShoptetStockClient.ListAsync"`, `Url` (the CSV URL with any token query string redacted), `ExceptionType`, `Message`, `InnerExceptionType`, `InnerMessage`, `StatusCode` (nullable), `ElapsedMs`.
- The original exception is rethrown (the client does **not** swallow it).
- A unit test verifies the log record contains `StatusCode=503` for a 503 response and `InnerExceptionType=System.Net.Sockets.SocketException` (or equivalent) for a simulated connection failure.

### FR-5: Make ProductPairingDqtComparer use the resilience pipeline
`ProductPairingDqtComparer.CompareAsync` MUST not call `_eshopStockClient.ListAsync(ct)` (or `_erpStockClient.ListAsync(ct)`) without resilience wrapping. With FR-1–FR-3, the HttpClient handler already retries, but the comparer's two-source call still needs the **circuit breaker + timeout** semantics that `CatalogResilienceService` provides — and parity with `CatalogDataRefreshService` is required so failure handling is consistent across consumers.

**Acceptance criteria:**
- `ProductPairingDqtComparer.CompareAsync` either (a) takes `ICatalogResilienceService` as a dependency and wraps both list calls, or (b) calls into a shared helper that already does so.
- Existing tests under `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs` are updated and pass.
- No public behavior changes for the comparer beyond resilience — the returned `DriftComparisonResult` is identical for successful runs.

### FR-6: Document the stock CSV endpoint behavior
The stock CSV export endpoint, its host, and the observed failure modes MUST be recorded in `docs/integrations/shoptet-api.md` per the project rule "Shoptet API findings must be documented before use."

**Acceptance criteria:**
- A new subsection under section 4 (Stock) in `docs/integrations/shoptet-api.md` documents: the CSV URL pattern, the encoding (windows-1250), the delimiter (`;`), the known transient failure rate from telemetry (~1/day baseline), the retry policy applied, and a note that the CSV host is **not** `api.myshoptet.com` and therefore is invisible to the dependency tracker.

## Non-Functional Requirements

### NFR-1: Performance
- The stock CSV refresh is an out-of-band job (`CatalogDataRefreshService`) and is not on a user-request hot path. The retry policy MAY add up to ~7 s (1 s + 2 s + 4 s with jitter) of latency to a failing call before surfacing the error.
- Successful calls MUST NOT exhibit additional latency beyond what the dependency tracker currently reports (p95 ≈ 241 ms on the REST API; CSV export latency to be measured but is expected to be similar).

### NFR-2: Reliability target
- After the fix, the rate of unhandled `HttpRequestException` from `ShoptetStockClient.ListAsync` SHOULD drop to ≤ 0.2 / day (an 80%+ reduction). This will be measured by the same telemetry signal over a 7-day post-deploy window.

### NFR-3: Security
- The CSV URL MAY contain an access token in its query string. The redacted form (`?token=***`) MUST be used in any log line; no raw token may appear in logs.
- No new secrets are introduced. Existing token handling via `ShoptetApiSettings` is preserved.

### NFR-4: Observability
- All retry attempts and terminal failures MUST be emitted as structured logs (Serilog/`ILogger` semantic logging).
- The operation name `ShoptetStockClient.ListAsync` MUST be the constant used in `OperationName` log fields so it matches existing dashboards.

### NFR-5: Testability
- The HTTP layer changes MUST be unit-testable via a `DelegatingHandler` test double — no live Shoptet calls in unit tests.
- The existing integration test (`ShoptetStockClientIntegrationTests.cs`) continues to run against staging and validates the happy path.

## Data Model
No persistent data-model changes. Configuration additions only:

```csharp
public class ShoptetStockClientOptions
{
    public const string SettingsKey = "StockClient";
    public string Url { get; set; } = "http://";
    public int TimeoutSeconds { get; set; } = 60;        // new
    public int MaxRetryAttempts { get; set; } = 3;       // new
    public int RetryBaseDelaySeconds { get; set; } = 1;  // new
}
```

The new fields are optional with sensible defaults; existing deployments need no configuration changes.

## API / Interface Design
- `IEshopStockClient` is unchanged. Method signature `Task<List<EshopStock>> ListAsync(CancellationToken cancellationToken)` is preserved.
- `ShoptetApiAdapterServiceCollectionExtensions.AddShoptetApiAdapter` registers a named `HttpClient` for the CSV export and attaches a Polly resilience handler via `AddStandardResilienceHandler()` or `AddPolicyHandler(...)` (whichever pattern is already in use elsewhere in the project — prefer consistency over novelty).
- `ProductPairingDqtComparer` constructor gains an `ICatalogResilienceService` parameter (already a DI-registered service).
- No public REST or HTTP API changes; this is internal-adapter hardening.

## Dependencies
- `Microsoft.Extensions.Http.Resilience` (or the existing `Polly` package — verify which the project standardizes on; `CatalogResilienceService` uses Polly v8 directly).
- Existing `ICatalogResilienceService` and its DI registration.
- No new third-party packages should be required.

## Out of Scope
- Re-architecting `CatalogResilienceService` to operate as an HTTP-handler instead of a wrapping service.
- Changing the CSV format, encoding, or schema mapping.
- Migrating the stock CSV export to a different Shoptet endpoint (e.g. the `/api/products/snapshot` webhook-driven endpoint — explicitly noted as unusable in `docs/integrations/shoptet-api.md:309`).
- Caching the CSV response on disk to survive Shoptet outages — out of scope for this incident fix.
- Backfilling resilience for the **other** Shoptet adapter methods (`UpdateStockAsync`, `GetSupplyAsync`, `SetRealStockAsync`); those use the injected `_http` and benefit automatically from any handler attached to `IEshopStockClient`'s registered client, but verifying each method's behavior is a separate exercise.
- Changes to `IErpStockClient` (FlexiBee adapter) even though `ProductPairingDqtComparer` also calls it without resilience — left for a follow-up.

## Open Questions
None.

## Status: COMPLETE
```