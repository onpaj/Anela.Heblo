# Specification: Harden Product Export Download against transient HTTP failures

## Summary
The daily `ProductExportDownloadJob` is failing with `Faulted` HttpClient dependency results (3 failures observed in the last 24 h). This spec defines the changes required to make the download path resilient: explicit, bounded HTTP timeouts; a Polly-based retry policy for transient network/TLS errors; structured telemetry that distinguishes transient retries from terminal failures; and an investigation outcome explaining why a once-daily job is recording multiple failures per day.

## Background

`ProductExportDownloadJob` (cron `0 2 * * *`) is a recurring job that:

1. Resolves the configured external CSV URL from `ProductExportOptions.Url`.
2. Sends a `DownloadFromUrlRequest` through MediatR.
3. `DownloadFromUrlHandler` validates the URL/container, performs a `HEAD` request to capture file size, then delegates to `IBlobStorageService.DownloadFromUrlAsync(...)`, which downloads with `HttpClient.GetAsync(...)` and uploads the resulting stream to Azure Blob Storage.

Application Insights shows the `GET /export/products.csv` dependency call entering `Faulted` state — i.e. an exception was thrown by `HttpClient` (DNS/TCP/TLS/timeout), not a non-2xx HTTP response. Today the code path:

- Uses a transient `HttpClient` registered with no explicit timeout (defaults to 100 s).
- Has no retry policy — a single transient failure permanently fails the day's run.
- Logs and tracks the exception via `ITelemetryService.TrackException`, then rethrows so Hangfire marks the job failed.

The 3-failures-in-24-h signal is itself anomalous for a once-daily job and must be explained as part of this work (see FR-5).

Relevant files:

- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs`
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ProductExportOptions.cs`
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (options binding)

The codebase already uses Polly v8 (`ResiliencePipeline`) — see `CatalogResilienceService.cs`. Reuse that pattern; do not introduce a different resilience library.

## Functional Requirements

### FR-1: Configurable, bounded HTTP timeouts for product export downloads
Both the `HEAD` (size probe) and `GET` (download) calls used by `DownloadFromUrlHandler` / `AzureBlobStorageService.DownloadFromUrlAsync` MUST run under explicit, bounded timeouts sourced from configuration. The defaults MUST be tighter than the current 100 s `HttpClient` default to fail fast on hung connections, but generous enough to download a multi-MB CSV under normal conditions.

Defaults (overridable via `ProductExportOptions`):

- Connection/HEAD timeout: **10 seconds**
- Total download (GET) timeout: **120 seconds**

The download must run with a `CancellationToken` that combines the caller's token with the configured timeout. The 100 s ambient `HttpClient.Timeout` MUST NOT be relied on.

**Acceptance criteria:**
- `ProductExportOptions` exposes `HeadTimeout` and `DownloadTimeout` as `TimeSpan` properties with the defaults above (or named in seconds; consistent with surrounding options style).
- A unit test asserts the handler aborts a download that exceeds `DownloadTimeout` and surfaces the cancellation as a typed failure (see FR-3), not as a raw `TaskCanceledException`.
- A unit test asserts the HEAD probe returns size `0` (current fallback behaviour) — and does NOT cancel the parent download — when the HEAD call exceeds `HeadTimeout`.
- The named `HttpClient` registration (or local `CancellationTokenSource.CreateLinkedTokenSource`) enforces these timeouts; the default 100 s is no longer the effective ceiling.

### FR-2: Polly-based retry for transient download failures
Calls that download the product export CSV MUST be wrapped in a Polly v8 `ResiliencePipeline` configured for transient HTTP/network errors. The pipeline must follow the conventions already used by `CatalogResilienceService`.

Retry policy (defaults, configurable via `ProductExportOptions`):

- Max retry attempts: **3** (so up to 4 total attempts)
- Backoff: exponential with jitter, base delay **2 seconds**
- Handle: `HttpRequestException`; `TaskCanceledException` / `OperationCanceledException` ONLY when the caller's `CancellationToken` was not the trigger; non-success status codes from the GET (e.g. 5xx, 408, 429) when surfaced as `HttpRequestException` via `EnsureSuccessStatusCode`.
- Do NOT retry on: 4xx other than 408/429, `InvalidOperationException` from validation, or any `OperationCanceledException` whose token equals the inbound `cancellationToken`.

The retry MUST log each attempt with `WARN` level including attempt number, delay, and exception type/message (matching `CatalogResilienceService.OnRetry`).

**Acceptance criteria:**
- Unit test: handler retries exactly 3 times on a simulated `HttpRequestException` and succeeds on attempt 4.
- Unit test: handler does NOT retry when the caller cancels the operation (token equality check).
- Unit test: handler does NOT retry on a 404 (hard failure) and surfaces it as a typed failure response.
- Unit test: total elapsed time across retries respects the configured backoff schedule (use a fake `TimeProvider` or Polly test hooks; do not sleep in tests).
- Retry attempts are visible in logs with the attempt number and the underlying exception.

### FR-3: Structured failure response and telemetry
`DownloadFromUrlHandler` already returns a `DownloadFromUrlResponse` envelope with `Success`, `ErrorCode`, and `Params`. The hardened path MUST preserve and extend this contract:

- A timeout (any layer) results in `Success = false`, `ErrorCode = ErrorCodes.FileDownloadFailed` (or a new `DownloadTimeout` code if the team prefers — see Open Questions), and `Params` containing `fileUrl`, `attemptCount`, `elapsedMs`, and `cause = "timeout"`.
- A transient retry exhaustion produces `Success = false`, `ErrorCode = ErrorCodes.FileDownloadFailed`, and `Params` containing `fileUrl`, `attemptCount`, `elapsedMs`, and the final exception type.
- `ProductExportDownloadJob` MUST emit:
  - One `TrackBusinessEvent("ProductExportDownload", ...)` event per terminal outcome with `Status` of `Success`, `Failed`, or `Skipped` (job disabled).
  - A `TrackDependency`/`TrackException` for each failed attempt distinguishable from the terminal outcome (so Application Insights shows N retries, not N independent job runs). Use the existing `ITelemetryService` API; do not introduce a new telemetry abstraction.
- The job MUST still rethrow on terminal failure so Hangfire records the run as failed (preserves current operational visibility).

**Acceptance criteria:**
- The shape of `DownloadFromUrlResponse.Params` on failure is asserted in unit tests for each terminal outcome (timeout, retry exhaustion, validation failure, hard 4xx).
- Telemetry assertions verify exactly one `Success`/`Failed`/`Skipped` business event per job run, regardless of retry count.
- `ITelemetryService.TrackException` is called per retry, with `Job` and `AttemptNumber` properties.

### FR-4: Diagnose external host accessibility (operational, in-scope)
Before shipping the resilience changes, the team MUST verify whether the configured `ProductExportOptions.Url` is currently reachable, whether its TLS certificate is valid, and whether the external host returns a stable `Content-Length` for the CSV. The findings MUST be appended to `docs/integrations/shoptet-api.md` (the export URL appears to be a Shoptet `/export/products.csv-…` endpoint based on neighbouring config keys) so the knowledge is durable per repository rule §9.

**Acceptance criteria:**
- A short note added to `docs/integrations/shoptet-api.md` covering: host, observed certificate validity, observed response size range, observed latency range, and any quirks (e.g. server closes connection mid-stream).
- If the URL is found to be wrong or stale, `ProductExportOptions.Url` is updated in environment configuration (not committed secrets).

### FR-5: Explain the "3 failures / 24 h" anomaly
A once-daily job recording 3 failures per day implies one of: (a) the job is being triggered more than once per day, (b) Hangfire is auto-retrying on failure, (c) another code path is also hitting the same dependency URL, or (d) AI dependency telemetry is double-counting. The investigation MUST identify which.

**Acceptance criteria:**
- A finding is recorded in the PR description (or `memory/context/state.md`) naming the cause.
- If the cause is Hangfire auto-retry, the retry settings are explicitly aligned with FR-2 (no double-retry: either Hangfire retries OR Polly retries, not both layered).
- If the cause is an unrelated code path consuming the same URL, that code path is documented.

## Non-Functional Requirements

### NFR-1: Performance
- Happy-path download latency MUST NOT regress noticeably; the only added overhead per successful run is the timeout linked-cancellation cost (negligible).
- Total worst-case wall-clock time for a fully-failed run is bounded by `DownloadTimeout × (MaxRetryAttempts + 1)` plus jittered backoff (~< 10 minutes with the proposed defaults). This MUST remain comfortably below any Hangfire job timeout.

### NFR-2: Security
- No credentials or full URLs containing tokens may be logged at `Information` level. The export URL today contains a token-style suffix in adjacent options (`StockClient.Url`, `ProductPriceOptions.ProductExportUrl`); confirm `ProductExportOptions.Url` is treated the same and redact the query string / suffix in logs.
- TLS validation MUST remain on; do not introduce certificate-bypass switches under any flag. If the external host has an expired cert, the fix is to update the host or the URL, not to disable validation.
- All new configuration values bind via the Options pattern (no raw `IConfiguration["..."]` reads in handlers).

### NFR-3: Observability
- Every job run produces exactly one terminal AI custom event (`ProductExportDownload`) with `Status ∈ {Success, Failed, Skipped}`.
- Retries are visible as separate dependency / exception telemetry entries linked to the same operation ID, so on-call can tell "1 job run with 3 retries" apart from "3 separate job runs".
- Logs include `JobName`, `AttemptNumber`, `MaxRetryAttempts`, `ElapsedMs`, and (on failure) `ErrorCode`.

### NFR-4: Backwards compatibility
- The public API surface of `DownloadFromUrlRequest` / `DownloadFromUrlResponse` MUST NOT break existing consumers (also called from elsewhere in the FileStorage feature). New `Params` keys are additive only.
- `ProductExportOptions` gains new properties with defaults; existing deployments without the new keys continue to work.

## Data Model

No persistent schema changes. The only additions are configuration properties:

```csharp
public class ProductExportOptions
{
    public string Url { get; set; } = null!;
    public string ContainerName { get; set; } = null!;
    public TimeSpan HeadTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromSeconds(120);
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
}
```

(`null!` initializers preserve the current pattern; defaults are picked so omitting the new keys in `appsettings.json` reproduces the proposed defaults.)

## API / Interface Design

No HTTP API change. Internal touchpoints:

1. **`ProductExportOptions`** — gains the four new properties listed above. Bind in `ServiceCollectionExtensions.cs` (already present at line 324).
2. **`FileStorageModule`** — switch the `HttpClient` registration from `services.AddTransient<HttpClient>()` to a named `services.AddHttpClient("ProductExportDownload", ...)` that:
   - sets `BaseAddress`/handler defaults if any
   - enforces a per-request timeout via `HttpClient.Timeout` (set to `DownloadTimeout`)
   - has socket handler `PooledConnectionLifetime` configured (DNS refresh)
   - leaves general `IBlobStorageService` HTTP usage on its existing client OR switches all blob-storage HTTP calls to typed clients — decide in a brief design note in the PR.
3. **`DownloadFromUrlHandler`** — accept `IOptions<ProductExportOptions>` (or a dedicated `IDownloadResilienceService` analogous to `CatalogResilienceService`) and wrap the HTTP-touching code path in the Polly pipeline. Prefer a dedicated `IDownloadResilienceService` for parity with the existing pattern; the `CatalogResilienceService` shape is the template.
4. **`AzureBlobStorageService.DownloadFromUrlAsync`** — accept (or be invoked under) a `CancellationToken` that already carries the `DownloadTimeout`. The current method keeps its signature; the timeout is applied by the caller via `CancellationTokenSource.CreateLinkedTokenSource`.
5. **`ProductExportDownloadJob.ExecuteAsync`** — unchanged externally; emits a single terminal business event regardless of retries.

No new MediatR requests, no new controllers, no new public DTOs.

## Dependencies

- `Polly` v8 — already a transitive dependency via `Anela.Heblo.Application.csproj` (used by `CatalogResilienceService`). No new package.
- `Microsoft.Extensions.Http` for `IHttpClientFactory` — already in use elsewhere; verify in `Anela.Heblo.Application.csproj`.
- Application Insights (`ITelemetryService`) — already wired.
- Hangfire — must verify default retry attempts on this job type as part of FR-5; do not change Hangfire defaults globally without an explicit decision.

## Out of Scope

- Replacing the external CSV-over-HTTP integration with a different protocol (SFTP, push-based, S3, etc.).
- Streaming the CSV directly to blob storage instead of buffering through a stream from `HttpClient` (already implemented; not regressing).
- Adding circuit-breaker semantics. The job runs once per day; circuit breaking adds little value on a daily cadence and is explicitly deferred. (`CatalogResilienceService` uses one because catalog endpoints are hit constantly; this job is not.)
- Generalising the retry/timeout knobs into a shared `DownloadResilienceOptions` reused by other features (e.g. `ProductPriceOptions.ProductExportUrl`, `StockClient.Url`, `OrgChart.DataSourceUrl`). Worthy follow-up but not required here.
- Changing how the job is scheduled (cron, time zone, manual trigger UI).
- Migrating the export URL to staging vs. production environment differentiation.

## Open Questions

1. **Dedicated `ErrorCode` for timeout?** Today only `ErrorCodes.FileDownloadFailed` exists for download issues. Should we introduce `ErrorCodes.DownloadTimeout` for clearer alerting, or keep one code with `cause` in `Params`? Assumption taken in FR-3: keep one code, use `Params.cause` to distinguish — but flag for product/PM confirmation.
2. **Resilience service vs. inline pipeline.** `CatalogResilienceService` is a singleton resilience holder. For one job, an inline pipeline inside `DownloadFromUrlHandler` may be simpler. Recommendation: introduce `IDownloadResilienceService` for parity and testability; confirm with architect in plan phase.
3. **Should HEAD-failure block the download?** Current code treats a failed HEAD as "size unknown" and proceeds. Spec preserves that. If product wants HEAD failure to fail-fast (e.g. cert error already visible there), the policy needs to flip.
4. **Hangfire retry count.** If FR-5 finds Hangfire is auto-retrying, the team must decide whether to (a) leave Hangfire retries on top of Polly retries (yields up to N×M attempts) or (b) set Hangfire `AutomaticRetry` attempts to 0 for this job. Recommendation: (b), to keep retry logic in one place.
5. **Where do downloaded blobs accumulate?** Files are named `products_{timestamp}.csv` and never deleted. Out of scope here, but worth a separate cleanup-job ticket if storage cost or container listing performance becomes a concern.
6. **Are the 3/24 h failures correlated in time?** If they cluster within seconds (Hangfire retry burst) the cause is almost certainly (b) above; if spread across hours, another caller or scheduler is involved. The investigation in FR-5 should explicitly check timestamps.
