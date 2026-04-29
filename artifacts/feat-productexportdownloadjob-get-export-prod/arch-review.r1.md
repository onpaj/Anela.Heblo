# Architecture Review: Harden Product Export Download against transient HTTP failures

## Architectural Fit Assessment

The spec aligns with existing patterns in the codebase: Polly v8 `ResiliencePipeline` is already in use (`CatalogResilienceService`), the Options pattern is already used for `ProductExportOptions`, MediatR + Vertical Slice is the established style, and `ITelemetryService` is the canonical telemetry boundary. There are **two integration points the spec underweights**:

1. **The current `HttpClient` registration is broken**, not just sub-optimal. `FileStorageModule.cs:28` registers `AddTransient<HttpClient>()` and `AzureBlobStorageService` is registered as `Singleton` (line 32). A singleton captures its transient dependency for the lifetime of the app — sockets never recycle, DNS is never re-resolved. This is a latent root cause for `Faulted` dependencies and must be fixed regardless of retry policy.
2. **Hangfire's default `AutomaticRetryAttribute.Attempts = 10`.** This is almost certainly the FR-5 explanation. Without an explicit `[AutomaticRetry(Attempts = 0)]` decoration, layering Polly retries on top of Hangfire retries produces up to 4 × 11 = 44 attempts per "day," and Hangfire backoff puts retries 0/30/60/120/… seconds apart — exactly the shape of "3 faults in a 24h window for a once-daily job."

Both issues must be addressed for the resilience changes to behave as specified. The spec addresses them implicitly; this review makes them explicit.

## Proposed Architecture

### Component Overview

```
ProductExportDownloadJob  (Hangfire recurring job, [AutomaticRetry(Attempts=0)])
        │
        │  IMediator.Send(DownloadFromUrlRequest)
        ▼
DownloadFromUrlHandler  (MediatR handler)
        │
        │  IDownloadResilienceService.ExecuteWithResilienceAsync(...)
        ▼
DownloadResilienceService  (singleton, holds Polly v8 pipeline)
        │  - retry: 3 attempts, exponential + jitter, 2s base
        │  - per-attempt timeout (DownloadTimeout) via linked CTS
        │  - emits per-attempt TrackException via ITelemetryService
        ▼
   inner delegate
        │  HEAD probe (HeadTimeout linked CTS, never cancels parent)
        │  GET via IBlobStorageService.DownloadFromUrlAsync(...)
        ▼
AzureBlobStorageService
        │  IHttpClientFactory.CreateClient("ProductExportDownload")
        │     ├─ PooledConnectionLifetime = 5 min   (DNS refresh)
        │     ├─ HttpClient.Timeout = InfiniteTimeSpan  (delegate to CTS)
        │     └─ no per-message handler retries (Polly owns retries)
        ▼
  external CSV host  → Azure Blob Storage upload
```

### Key Design Decisions

#### Decision 1: Resilience as a feature-scoped service, not inline, not shared

**Options considered:**
- (a) Inline Polly pipeline inside `DownloadFromUrlHandler`.
- (b) Reuse `ICatalogResilienceService` directly.
- (c) Generalise into a cross-cutting `IDownloadResilienceService` consumed by all download-style features.
- (d) Introduce a feature-local `IDownloadResilienceService` registered as a Singleton in `FileStorageModule`.

**Chosen approach:** (d) — a feature-local `IDownloadResilienceService` mirroring the shape of `CatalogResilienceService`, registered Singleton in `FileStorageModule`.

**Rationale:** Parity with `CatalogResilienceService` is what the rest of the codebase already does and what reviewers will expect. Inline Polly (a) duplicates the wiring at every call site. Reusing the catalog one (b) leaks a feature concern across slices and would force the catalog circuit-breaker onto the download path (the job runs once daily — circuit-break has no value). Generalising now (c) is YAGNI — it's a worthy follow-up once a second download caller actually appears.

#### Decision 2: Bound timeouts via linked CancellationTokenSource, NOT via `HttpClient.Timeout`

**Options considered:**
- (a) Set `HttpClient.Timeout = DownloadTimeout` on the named client.
- (b) Keep `HttpClient.Timeout = InfiniteTimeSpan` and rely on per-call `CancellationTokenSource.CreateLinkedTokenSource(callerCt, timeoutCt)`.

**Chosen approach:** (b).

**Rationale:** The retry predicate in `CatalogResilienceService` already distinguishes "caller cancelled" from "internal timeout" via `ex.CancellationToken.IsCancellationRequested == false`. Using `HttpClient.Timeout` raises a `TaskCanceledException` whose `CancellationToken` does **not** match the caller's token, but it also doesn't match a token the retry predicate controls — making the predicate harder to write correctly. A linked CTS gives the retry layer a token it owns, so "timeout vs caller cancel" is unambiguous: caller token cancelled ⇒ no retry; inner timeout token cancelled ⇒ retry. This is what the spec's FR-2 acceptance criteria require.

This means the spec's API/Interface Design item #2 ("enforces a per-request timeout via `HttpClient.Timeout`") should be amended to leave `HttpClient.Timeout` unset and apply timeouts via linked CTS.

#### Decision 3: One named `IHttpClientFactory` client, scoped to the export download path only

**Options considered:**
- (a) Migrate the entire `AzureBlobStorageService` to a typed/named `HttpClient` via factory.
- (b) Add a named client only for `DownloadFromUrlAsync` and inject `IHttpClientFactory` into `AzureBlobStorageService`; switch `DownloadFromUrlHandler` to the same named client.

**Chosen approach:** (b).

**Rationale:** Minimal blast radius. `AzureBlobStorageService.DownloadFromUrlAsync` is the only method that hits an external HTTP origin; every other method talks to Azure SDK. Migrating those would expand scope. Both the handler's HEAD probe and the service's GET must use the **same** named client so timeout/handler config is consistent — pull it via `IHttpClientFactory.CreateClient("ProductExportDownload")` in both places.

This also fixes the latent Singleton-holds-Transient-HttpClient bug noted above; that fix is mandatory regardless of the retry work.

#### Decision 4: Eliminate Hangfire auto-retry on this job; keep retries in Polly only

**Options considered:**
- (a) Globally set `GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute { Attempts = 0 })`.
- (b) Decorate only `ProductExportDownloadJob.ExecuteAsync` with `[AutomaticRetry(Attempts = 0)]`.
- (c) Leave Hangfire defaults; accept N×M attempts.

**Chosen approach:** (b).

**Rationale:** (c) reproduces the original bug. (a) changes behaviour for every other recurring job in the codebase — out of scope and risky for a solo-dev project. (b) is surgical and self-documenting at the job class.

#### Decision 5: Keep `ErrorCodes.FileDownloadFailed`; encode timeout/exhaustion in `Params`

**Chosen approach:** Add `Params["cause"]` ∈ `{"timeout", "retry-exhausted", "http-status"}` and `Params["attemptCount"]`, `Params["elapsedMs"]`. Do **not** introduce `ErrorCodes.DownloadTimeout`.

**Rationale:** Adding a new enum member breaks no consumers but introduces an alerting decision (which code triggers a page?) that is out of scope. The spec's NFR-4 (backwards compatibility) is met by additive params. If the team later wants timeout-specific alerting, splitting the code is a follow-up.

## Implementation Guidance

### Directory / Module Structure

New files:
```
backend/src/Anela.Heblo.Application/Features/FileStorage/
  Infrastructure/
    DownloadResilienceService.cs        ← new (singleton, Polly pipeline)
    IDownloadResilienceService.cs       ← new (interface)
```

Modified files (no new directories):
```
backend/src/Anela.Heblo.Domain/Features/Configuration/
  ProductExportOptions.cs               ← add 4 properties (HeadTimeout, DownloadTimeout,
                                                            MaxRetryAttempts, RetryBaseDelay)

backend/src/Anela.Heblo.Application/Features/FileStorage/
  FileStorageModule.cs                  ← register named HttpClient + resilience service
  UseCases/DownloadFromUrl/
    DownloadFromUrlHandler.cs           ← inject IDownloadResilienceService + IHttpClientFactory;
                                          replace try/catch shape; preserve response envelope
  Services/AzureBlobStorageService.cs   ← inject IHttpClientFactory; resolve named client per call
  Infrastructure/Jobs/
    ProductExportDownloadJob.cs         ← [AutomaticRetry(Attempts = 0)] attribute;
                                          single terminal TrackBusinessEvent

docs/integrations/shoptet-api.md        ← FR-4 finding (host, cert, latency, quirks)
```

Tests live next to existing tests under `backend/test/Anela.Heblo.Tests/Features/FileStorage/` and `…/MCP/` is unaffected.

### Interfaces and Contracts

**`IDownloadResilienceService`** (mirrors `ICatalogResilienceService` shape; no circuit breaker):

```csharp
public interface IDownloadResilienceService
{
    Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default);
}
```

The implementation reads `IOptionsMonitor<ProductExportOptions>` once at construction (singleton); pipeline is rebuilt only on app restart — option hot-reload is out of scope and matches the existing `CatalogResilienceService` pattern.

**Named `HttpClient` registration** (in `FileStorageModule.AddFileStorageModule`):

```csharp
services.AddHttpClient("ProductExportDownload")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression  = DecompressionMethods.All,
    })
    .ConfigureHttpClient(c => c.Timeout = Timeout.InfiniteTimeSpan); // delegates to linked CTS
services.AddSingleton<IDownloadResilienceService, DownloadResilienceService>();
// REMOVE: services.AddTransient<HttpClient>();
```

**`ProductExportOptions`** — keep as a class (consistent with current style). New properties:

```csharp
public TimeSpan HeadTimeout      { get; set; } = TimeSpan.FromSeconds(10);
public TimeSpan DownloadTimeout  { get; set; } = TimeSpan.FromSeconds(120);
public int      MaxRetryAttempts { get; set; } = 3;
public TimeSpan RetryBaseDelay   { get; set; } = TimeSpan.FromSeconds(2);
```

**`DownloadFromUrlResponse.Params`** on failure — additive keys:
- `fileUrl` (existing)
- `attemptCount` (new, string-formatted int)
- `elapsedMs` (new, string-formatted long)
- `cause` (new, ∈ `{"timeout","retry-exhausted","http-status","validation"}`)
- `error` (existing) — keep for backward compatibility

**`ProductExportDownloadJob`** — class-level attribute:

```csharp
[Hangfire.AutomaticRetry(Attempts = 0)]
public class ProductExportDownloadJob : IRecurringJob { … }
```

### Data Flow

**Happy path:**
1. Hangfire fires `ExecuteAsync` (linked to job CTS).
2. Job sends `DownloadFromUrlRequest` via MediatR.
3. Handler validates URL/container.
4. Handler resolves `HttpClient` via `IHttpClientFactory.CreateClient("ProductExportDownload")` and runs HEAD under a linked CTS bounded by `HeadTimeout`. Failure here logs at `Debug` and proceeds with size = 0 (preserves current contract; spec FR-1 acceptance).
5. Handler calls `IDownloadResilienceService.ExecuteWithResilienceAsync(ct => _blobStorageService.DownloadFromUrlAsync(...), "ProductExportDownload", callerCt)`.
6. Pipeline executes the delegate under a freshly-linked CTS bounded by `DownloadTimeout`. `AzureBlobStorageService.DownloadFromUrlAsync` resolves the same named client and issues GET → streams to blob.
7. Handler returns `DownloadFromUrlResponse { Success = true, … }`.
8. Job emits exactly one `TrackBusinessEvent("ProductExportDownload", { Status="Success", … })` and returns.

**Transient-failure path (e.g., 1 socket-level failure, then success):**
1–4 same as above.
5. Pipeline invokes delegate, delegate throws `HttpRequestException` from `EnsureSuccessStatusCode` or socket. Polly `OnRetry` callback logs `WARN` with `AttemptNumber`, calls `_telemetry.TrackException(ex, { Job, AttemptNumber, IsTerminal="false" })`.
6. Backoff (exponential + jitter, base 2 s).
7. Pipeline re-invokes delegate; succeeds.
8. Handler returns success; job emits one terminal `Success` event.
9. Application Insights shows: 1 dependency-fault telemetry + 1 dependency-success telemetry + 1 business event with `Status=Success`. On-call sees "1 run, 1 retry" not "2 runs".

**Hard-failure path (retry exhausted):**
1–4 same.
5. Delegate throws on every attempt. Pipeline bubbles the final exception after `MaxRetryAttempts + 1` attempts.
6. Handler catches, returns `DownloadFromUrlResponse { Success=false, ErrorCode=FileDownloadFailed, Params={..., cause="retry-exhausted"} }`.
7. Job emits one `TrackBusinessEvent("ProductExportDownload", { Status="Failed", … })` AND rethrows so Hangfire records the run as failed (the rethrow + `[AutomaticRetry(Attempts=0)]` keep Hangfire's run history accurate without retrying).

**Caller cancellation path:**
Caller token cancelled → `OperationCanceledException` whose `Token == callerCt` → retry predicate skips it → propagates up unchanged. No retry, no terminal "Failed" event (job emits a `Cancelled` event if you wish, otherwise no event; spec's NFR-3 says exactly one Success/Failed/Skipped — recommend treating cancel as a separate `Status="Cancelled"` to keep the invariant intact).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `[AutomaticRetry(Attempts=0)]` is forgotten → Polly + Hangfire double-retry | **HIGH** | Make this a checklist item in the PR description; add an integration test that asserts the attribute is present via reflection on the job type. |
| Singleton `AzureBlobStorageService` continues to hold a long-lived `HttpClient`, defeating `PooledConnectionLifetime` | **HIGH** | The fix mandates injecting `IHttpClientFactory` into `AzureBlobStorageService` and calling `CreateClient(...)` per `DownloadFromUrlAsync` invocation. Code review must verify the field is `IHttpClientFactory`, not `HttpClient`. |
| `HttpClient.Timeout` left at default 100s would short-circuit our linked CTS in obscure ways | MEDIUM | Explicitly set `Timeout = Timeout.InfiniteTimeSpan` on the named client and document why. |
| HEAD timeout cancellation leaks into the download CTS (acceptance: must NOT cancel parent) | MEDIUM | HEAD must use a CTS linked **only** to `callerCt + HeadTimeout`, not to the download CTS. Unit test required (already in FR-1 acceptance). |
| Total worst-case wall-clock time exceeds Hangfire `InvisibilityTimeout = 30 min` (line 301) | LOW | Defaults (4 × 120 s + jittered backoff ≈ < 10 min) are well below 30 min. Encode this assumption as a `Debug.Assert`-style invariant in `DownloadResilienceService` ctor: `MaxRetryAttempts * DownloadTimeout < TimeSpan.FromMinutes(20)`. |
| `IOptionsMonitor` reload semantics for `ProductExportOptions` are inconsistent (singleton service caches values) | LOW | Match existing pattern (`CatalogResilienceService` reads at ctor). Document that knob changes require app restart; this is acceptable for a daily job. |
| Token in URL (NFR-2) leaks into logs via `OnRetry` exception messages | MEDIUM | `OnRetry` must log `args.Outcome.Exception?.GetType().Name` and a sanitised message; do **not** log `args.Context.OperationKey` if it contains the URL. The operation key passed should be `"ProductExportDownload"` (a constant), not the URL — already what `CatalogResilienceService` does. |
| FR-4 investigation discovers the URL is wrong but fix requires production config change outside this PR | LOW | Document the finding in `docs/integrations/shoptet-api.md` and keep the resilience PR independently mergeable. |

## Specification Amendments

1. **API/Interface Design item #2 (named `HttpClient`)** — strike "enforces a per-request timeout via `HttpClient.Timeout` (set to `DownloadTimeout`)". Replace with: "leaves `HttpClient.Timeout = Timeout.InfiniteTimeSpan`; per-call timeout is enforced by `CancellationTokenSource.CreateLinkedTokenSource` inside `DownloadResilienceService` and around the HEAD probe in `DownloadFromUrlHandler`." Rationale: Decision 2 above.

2. **FR-5 outcome must include a code change, not only a finding.** Add to FR-5 acceptance criteria: "`ProductExportDownloadJob` is decorated with `[Hangfire.AutomaticRetry(Attempts = 0)]`; this is verified by a reflection-based unit test." This is the architecturally-correct resolution of the 3-failures anomaly.

3. **`AzureBlobStorageService` must be migrated from `HttpClient` to `IHttpClientFactory`.** Spec mentions this only obliquely ("decide in a brief design note"); architect's decision is: do it, scoped to `DownloadFromUrlAsync` only. Add to API/Interface Design item #4: "`AzureBlobStorageService` constructor changes from `HttpClient httpClient` to `IHttpClientFactory httpClientFactory`; `DownloadFromUrlAsync` resolves `httpClientFactory.CreateClient("ProductExportDownload")` per call." Other methods on the service do not use HTTP and are unchanged.

4. **Cancellation contract for caller cancel.** Spec NFR-3 says "exactly one terminal AI custom event with `Status ∈ {Success, Failed, Skipped}`." Add `"Cancelled"` to this enumeration so an external shutdown does not produce zero events. Otherwise the cancel path is forced to emit `Failed`, which is misleading on-call signal.

5. **Open Question #2 resolved:** introduce `IDownloadResilienceService` (Decision 1 above). Open Question #1 resolved: keep one `ErrorCode` (Decision 5). Open Question #4 resolved: Hangfire retries off (Decision 4).

## Prerequisites

None at infrastructure level — no migrations, no new packages, no new infra. The work is fully self-contained in the FileStorage feature slice plus a one-line attribute on the job and a doc update.

Soft prerequisites (operational, can run in parallel with implementation):

1. **FR-4 reachability check** — `curl -I` against `ProductExportOptions.Url` from a deployed environment (staging is sufficient) to capture certificate validity, observed `Content-Length`, and observed latency. Findings appended to `docs/integrations/shoptet-api.md` per repo rule §9.
2. **FR-5 timeline check** — pull the 3 timestamps from Application Insights for the last 24h; confirm they cluster within Hangfire's default retry backoff window (~0/30/60 s), which validates Decision 4 before the code is merged.