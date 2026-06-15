I have enough to write the review now.

```markdown
# Architecture Review: Harden ShoptetStockClient.ListAsync against transient HTTP failures

## Skip Design: true

This is a backend resilience/observability change with no UI/UX work — no visual components, screens, or layouts are added or modified.

## Architectural Fit Assessment

The proposal aligns well with two patterns already established in the codebase:

1. **Polly v8 resilience pipelines.** `CatalogResilienceService` (`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogResilienceService.cs:56`) and `DownloadResilienceService` (`backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs:50`) both build `ResiliencePipeline` instances directly on Polly v8 (8.4.1). The codebase has **not** adopted `Microsoft.Extensions.Http.Resilience`; HTTP-layer resilience handlers are not used anywhere today.
2. **Typed `HttpClient` registration in `ShoptetApiAdapterServiceCollectionExtensions`** (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs:45`). The pattern is `AddHttpClient<TInterface, TImpl>` with `BaseAddress` + `Shoptet-Private-API-Token` headers.

Two important integration points the spec only touches lightly:

- **The CSV URL is a different host** than `BaseAddress` (`api.myshoptet.com`). The typed client cannot be reused as-is — its base address and bearer token header are wrong for the CSV host.
- **There is an outer 30 s timeout** baked into `CatalogResilienceService.CreateResiliencePipeline()` (`...:106`). The spec's proposed default `TimeoutSeconds=60` and 3 retries with exponential backoff produce a worst-case wall clock that exceeds 30 s, so the outer `CatalogResilienceService` will abort retries mid-flight when invoked from `CatalogDataRefreshService.RefreshEshopStockData`. This needs to be reconciled — see Specification Amendments below.

The spec is also right that `ProductPairingDqtComparer.CompareAsync` calls the clients without resilience (`backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs:23`).

## Proposed Architecture

### Component Overview

```
                                ┌──────────────────────────────────────────────┐
                                │ CatalogDataRefreshService.RefreshEshopStock  │
                                │  └─► ICatalogResilienceService               │ (outer: circuit breaker)
                                │      └─► IEshopStockClient.ListAsync ───┐    │
                                └────────────────────────────────────────-┼────┘
                                                                          │
┌──────────────────────────────────────────────────────────────────────┐  │
│ ProductPairingDqtComparer.CompareAsync                               │  │
│  └─► ICatalogResilienceService (NEW)                                 │  │
│      └─► IEshopStockClient.ListAsync ─────────────────────────────┐  │  │
│      └─► IErpStockClient.ListAsync (out of scope per spec)        │  │  │
└──────────────────────────────────────────────────────────────────-┼─-┘  │
                                                                    │     │
                                                                    ▼     ▼
                                       ┌──────────────────────────────────────┐
                                       │ ShoptetStockClient.ListAsync         │
                                       │  uses _httpClientFactory             │
                                       │   .CreateClient("ShoptetStockCsv")   │
                                       │  + structured try/catch logging      │
                                       └──────────────────────────────────────┘
                                                          │
                                                          ▼
                  ┌──────────────────────────────────────────────────────┐
                  │ Named HttpClient "ShoptetStockCsv"                   │
                  │  ├─ Timeout = options.TimeoutSeconds                 │
                  │  └─ Resilience handler (Polly v8):                   │
                  │     - retry (3, expo+jitter, base 1s)                │
                  │     - handles HttpRequestException, 5xx, 408, 429,   │
                  │       TimeoutRejectedException, OperationCanceled    │
                  │       (only when callerCt not requested)             │
                  └──────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Add a named `HttpClient` for the CSV download, not reuse the typed client
**Options considered:**
- A. Reuse the injected `_http` (typed `HttpClient`) and attach the resilience handler to that registration.
- B. Register a **named** `HttpClient` `"ShoptetStockCsv"` and resolve it from the factory inside `ListAsync`.

**Chosen approach:** B.

**Rationale:** The typed client carries `BaseAddress = ShoptetApiSettings.BaseUrl` and the `Shoptet-Private-API-Token` header — both are wrong for the CSV host (per the spec the CSV host is a separate URL on a non-`api.myshoptet.com` host, which is also why it is invisible to the dependency tracker). Reusing the typed client would either send the token to a third party or require ad-hoc header manipulation per call. A named client cleanly isolates CSV-specific config (host, timeout, retry policy) from the REST API client used by `UpdateStockAsync`, `GetSupplyAsync`, and `SetRealStockAsync`.

#### Decision 2: Use `Microsoft.Extensions.Http.Resilience`, not a hand-rolled Polly pipeline at the HTTP layer
**Options considered:**
- A. `Microsoft.Extensions.Http.Resilience` (`AddResilienceHandler(...)`) on the named client.
- B. Hand-built `ResiliencePipeline` invoked manually inside `ListAsync` (mirroring `DownloadResilienceService`).
- C. `Microsoft.Extensions.Http.Polly` (`AddPolicyHandler(...)`) — the legacy Polly v7 path.

**Chosen approach:** A.

**Rationale:** The spec explicitly asks for resilience as a property of the HTTP client (FR-1, FR-3). `Microsoft.Extensions.Http.Resilience` v8.x sits on top of Polly v8, which is what the rest of the project already uses (8.4.1 everywhere). It plugs cleanly into the existing `AddHttpClient` registration, runs as a `DelegatingHandler`, is unit-testable via stub `HttpMessageHandler` (FR-3 acceptance, NFR-5), and avoids the maintenance overhead of two parallel pipelines. C is rejected because the project is on Polly v8; mixing v7 here would be a regression. B is rejected because it defeats the purpose of FR-1 (other future methods on the same client should inherit retries automatically).

#### Decision 3: Inject `ICatalogResilienceService` directly into `ProductPairingDqtComparer`
**Options considered:**
- A. Inject `ICatalogResilienceService` and wrap both `ListAsync` calls.
- B. Introduce a shared helper (e.g. `IResilientStockSource`) that both `CatalogDataRefreshService` and `ProductPairingDqtComparer` use.

**Chosen approach:** A.

**Rationale:** YAGNI. Two callers don't justify a new abstraction. `ICatalogResilienceService` is already registered as singleton in `CatalogModule` (`...CatalogModule.cs:87`), is in the same project as the comparer (`Anela.Heblo.Application`), and is already the pattern used by `CatalogDataRefreshService`. Direct injection keeps parity with the established pattern. If a third caller appears, revisit B.

#### Decision 4: Structured terminal logging via try/catch in `ListAsync`, not a custom `DelegatingHandler`
**Options considered:**
- A. Wrap the `GetAsync` call in `try/catch` within `ListAsync` and log on the catch path with all the structured fields (`Operation`, `Url` (redacted), `ExceptionType`, `Message`, `InnerExceptionType`, `InnerMessage`, `StatusCode`, `ElapsedMs`).
- B. Implement a `ShoptetCsvLoggingHandler : DelegatingHandler` to log terminal failures.

**Chosen approach:** A.

**Rationale:** The retry handler already logs each attempt (FR-3). Terminal logging needs `Operation="ShoptetStockClient.ListAsync"` semantics that a generic handler does not naturally have. A handler also cannot easily capture parse-stage failures separately from network failures, which is part of the value of FR-4. A small try/catch in `ListAsync` is clearer, more testable in unit tests, and adds no new types.

#### Decision 5: Keep `IEshopStockClient` interface untouched
**Options considered:**
- A. Preserve the existing `Task<List<EshopStock>> ListAsync(CancellationToken ct)` signature.
- B. Return a `Result<List<EshopStock>>` style envelope so callers can decide on failure.

**Chosen approach:** A.

**Rationale:** This is a contained incident fix, not an API redesign. The interface is consumed by `CatalogDataRefreshService` and `ProductPairingDqtComparer`; both already handle exceptions via the resilience layer. Changing the interface ripples to every test and consumer for no observable benefit.

## Implementation Guidance

### Directory / Module Structure

No new directories. Touched files:

```
backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/
├── Anela.Heblo.Adapters.ShoptetApi.csproj                # +PackageReference Microsoft.Extensions.Http.Resilience 8.x
├── ShoptetApiAdapterServiceCollectionExtensions.cs       # register named "ShoptetStockCsv" client + AddResilienceHandler
└── Stock/
    ├── ShoptetStockClient.cs                             # ListAsync rewrite (named client + structured try/catch)
    └── ShoptetStockClientOptions.cs                      # +TimeoutSeconds, +MaxRetryAttempts, +RetryBaseDelaySeconds

backend/src/Anela.Heblo.Application/
└── Features/DataQuality/Services/
    └── ProductPairingDqtComparer.cs                      # +ICatalogResilienceService constructor param, wrap calls

backend/test/Anela.Heblo.Tests/Features/
├── DataQuality/ProductPairingDqtComparerTests.cs         # update mock setup for ICatalogResilienceService
└── Adapters/ShoptetApi/Stock/ShoptetStockClientTests.cs  # NEW — DelegatingHandler-based unit tests for retry, timeout, logging

docs/integrations/shoptet-api.md                          # new subsection under §4 documenting CSV endpoint + retry policy
```

The `ShoptetStockClientTests` class is new — there are no existing unit tests for `ShoptetStockClient`, only the integration test in `Anela.Heblo.Adapters.Shoptet.Tests/Integration/ShoptetStockClientIntegrationTests.cs`.

### Interfaces and Contracts

**No changes** to:
- `IEshopStockClient` (signature preserved)
- `ICatalogResilienceService` (already exists and matches the contract `ProductPairingDqtComparer` needs)

**Updated:**
```csharp
public class ShoptetStockClientOptions
{
    public const string SettingsKey = "StockClient";
    public string Url { get; set; } = "http://";
    public int TimeoutSeconds { get; set; } = 30;            // see Amendment 1
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryBaseDelaySeconds { get; set; } = 1;
}
```

**Registration (named client) — illustrative:**
```csharp
services.AddHttpClient("ShoptetStockCsv", (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<ShoptetStockClientOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
})
.AddResilienceHandler("shoptet-stock-csv", (builder, ctx) =>
{
    var opts = ctx.ServiceProvider.GetRequiredService<IOptions<ShoptetStockClientOptions>>().Value;
    builder
        .AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts   = opts.MaxRetryAttempts,
            BackoffType        = DelayBackoffType.Exponential,
            UseJitter          = true,
            Delay              = TimeSpan.FromSeconds(opts.RetryBaseDelaySeconds),
            ShouldHandle       = /* HttpRequestException, 5xx, 408, 429,
                                    TimeoutRejectedException, and
                                    OperationCanceledException where !callerCt.IsCancellationRequested */,
            OnRetry            = /* structured LogWarning per FR-3 */,
        })
        .AddTimeout(TimeSpan.FromSeconds(opts.TimeoutSeconds)); // per-attempt timeout
});
```

The per-attempt timeout in the resilience pipeline must be slightly less than `HttpClient.Timeout` to avoid a race where the outer client timeout fires first.

### Data Flow

**Refresh path (cron / background job):**
```
CatalogDataRefreshService.RefreshEshopStockData
  → ICatalogResilienceService (outer pipeline: circuit breaker, 30 s pipeline timeout)
     → IEshopStockClient.ListAsync
        → IHttpClientFactory.CreateClient("ShoptetStockCsv")
        → client.GetAsync(opts.Url, ct)
           ↑ resilience handler retries on transient failures
        → CSV parse → List<EshopStock>
        → on terminal failure: structured LogError + rethrow
```

**Data quality path (DQT job):**
```
ProductPairingDqtComparer.CompareAsync
  → ICatalogResilienceService (NEW wrapping)
     → IEshopStockClient.ListAsync (same downstream path as above)
  → ICatalogResilienceService (NEW wrapping)
     → IErpStockClient.ListAsync (out of scope — left direct per spec)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Outer `CatalogResilienceService` has a hard 30 s timeout; the spec's default `TimeoutSeconds=60` × `MaxRetryAttempts=3` produces a worst case far over 30 s. The outer timeout would abort the inner retries every time, defeating FR-3 on the `CatalogDataRefreshService` path. | **HIGH** | Spec Amendment 1: tune defaults so worst-case wall clock fits inside 30 s. Recommend `TimeoutSeconds=8`, `MaxRetryAttempts=3`, `RetryBaseDelaySeconds=1` → worst case ≈ 8 + (1+jitter) + 8 + (2+jitter) + 8 ≈ 27 s. |
| Token leakage in logs. The CSV URL contains the access token as a query parameter. | **HIGH** | Implement a `RedactToken(string url)` helper used in **every** log line that contains the URL. Unit test the redaction explicitly. Per NFR-3. |
| The retry handler retries on `OperationCanceledException` even when the **caller's** token requested cancellation, leading to spurious retries during graceful shutdown or DQT cancellation. | MEDIUM | Use the same closure-on-`callerCt` predicate that `DownloadResilienceService` uses (`...:75`). The retry `ShouldHandle` must check `!callerCt.IsCancellationRequested` before retrying `OperationCanceledException`. |
| `Microsoft.Extensions.Http.Resilience` is a new package in the ShoptetApi adapter. Version skew with Polly 8.4.1 used elsewhere could surface as runtime conflicts. | MEDIUM | Pin to the latest stable `Microsoft.Extensions.Http.Resilience` that transitively depends on Polly ≥ 8.4.1 (8.10+ as of writing). Verify `dotnet list package --include-transitive` after install. |
| Integration test `ShoptetStockClientIntegrationTests` resolves `IEshopStockClient` from DI; if test fixture does not register the new named client, the test will throw at the new `CreateClient("ShoptetStockCsv")` call. | MEDIUM | The integration fixture already calls `AddShoptetApiAdapter(configuration)` — registration changes are picked up automatically. Verify in the fixture (`backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/Infrastructure/`). |
| `ProductPairingDqtComparer` is registered in `DataQualityModule` (`...DataQualityModule.cs:18`); adding the new constructor parameter is a binary-breaking change to that constructor. Any direct `new ProductPairingDqtComparer(...)` calls in tests will fail to compile. | LOW | The existing test (`ProductPairingDqtComparerTests.cs:15`) builds the SUT directly — update its constructor call and add a `Mock<ICatalogResilienceService>` whose `ExecuteWithResilienceAsync` passes through to the operation. |
| Hard-to-distinguish failure modes — `HttpRequestException` is thrown for DNS, TLS, connection reset, 5xx, timeouts, etc. FR-4 requires structured logging to disambiguate. | LOW | Capture `InnerException?.GetType().Name` and `InnerException?.Message` separately. Add `StatusCode` only when an `HttpResponseMessage` was received (i.e. not on connection-level failures). Stopwatch around the call → `ElapsedMs`. |

## Specification Amendments

**Amendment 1 — Default `TimeoutSeconds` reduced to fit inside the outer pipeline.**
The spec sets `TimeoutSeconds=60` (default) and `MaxRetryAttempts=3`. With exponential jittered backoff starting at 1 s, worst case ≈ 60 + 1 + 60 + 2 + 60 + 4 + 60 ≈ 247 s — but the outer `CatalogResilienceService` aborts after 30 s. **Default `TimeoutSeconds` to `8` (per-attempt)**, keeping `MaxRetryAttempts=3` and `RetryBaseDelaySeconds=1`. Operators can raise it via config for non-`CatalogDataRefreshService` paths. Update FR-2 acceptance test to assert on `TimeoutSeconds + 2` rather than `+5` to keep CI fast.

**Amendment 2 — Cancellation-aware retry predicate (mirror `DownloadResilienceService`).**
FR-3 says "retrying on … `TimeoutRejectedException`/`TaskCanceledException` that are not caused by the **outer** `CancellationToken`." Make this explicit in the spec: the retry pipeline must be **built per call** (or use a delegate that closes over `callerCt`) so the `ShouldHandle` predicate can check `!callerCt.IsCancellationRequested` — the same constraint already documented at `DownloadResilienceService.cs:48-49`.

**Amendment 3 — Add a unit test project hosting `ShoptetStockClientTests`.**
The adapter currently has no unit tests, only `Anela.Heblo.Adapters.Shoptet.Tests/Integration/...`. The spec assumes such a project exists; it does not. New tests should be added to a parallel `Anela.Heblo.Adapters.Shoptet.Tests/Unit/` directory (or whatever the project's convention is) using a stub `HttpMessageHandler`.

**Amendment 4 — URL redaction is a one-liner contract.**
NFR-3 says "no raw token may appear in logs." Add a one-line note to FR-4 acceptance: every log line that includes `Url` must use the redacted form. Add a unit test that asserts no log record contains the raw token substring.

**Amendment 5 — Clarify `ProductPairingDqtComparer` operation names.**
FR-5 doesn't specify the `operationName` string passed to `ExecuteWithResilienceAsync`. Recommend:
- `"ProductPairingDqtComparer.EshopList"` for the Shoptet call
- `"ProductPairingDqtComparer.ErpList"` for the ERP call (even though ERP retry is out of scope, wrapping it in the pipeline is cheap and gives circuit-breaker parity)

This keeps dashboard signals clean and lets us compare circuit-breaker behavior across DQT and refresh paths.

## Prerequisites

- **NuGet package added** to `Anela.Heblo.Adapters.ShoptetApi.csproj`: `Microsoft.Extensions.Http.Resilience` (latest 8.x compatible with Polly 8.4.1). No other package changes.
- **No DB migrations.** No persistent schema changes.
- **No configuration changes required for existing deployments** — `TimeoutSeconds`, `MaxRetryAttempts`, `RetryBaseDelaySeconds` default sensibly. Existing `StockClient:Url` is unchanged.
- **No infrastructure changes** — no Key Vault secrets, no Web App settings, no Azure resources.
- **Existing integration test fixture** (`Anela.Heblo.Adapters.Shoptet.Tests/Integration/Infrastructure/ShoptetIntegrationTestFixture`) already constructs DI via `AddShoptetApiAdapter`; the new named-client registration is picked up automatically.
- **Telemetry signal monitoring** — confirm an Azure Monitor / App Insights query for `exceptions | where type == "System.Net.Http.HttpRequestException" and outerMethod contains "ShoptetStockClient.ListAsync"` is available before deploy so NFR-2 (≤ 0.2 / day post-deploy) can be measured against the agreed 7-day window.
```