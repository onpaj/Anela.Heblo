```markdown
# Design: Harden Product Export Download against transient HTTP failures

## Component Design

### `IDownloadResilienceService` / `DownloadResilienceService`

**Responsibility:** Singleton Polly v8 pipeline holder scoped to the FileStorage feature. Mirrors `ICatalogResilienceService` shape exactly; no circuit breaker.

**Interface:**
```csharp
public interface IDownloadResilienceService
{
    Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default);
}
```

**Pipeline composition (inner → outer):**
1. **Per-attempt timeout strategy** — creates a linked CTS from `callerCt + DownloadTimeout`; passes the linked token to the operation; if the inner timeout fires, the `OperationCanceledException.CancellationToken` matches the inner CTS (not the caller's), making it distinguishable.
2. **Retry strategy** — up to `MaxRetryAttempts` (default 3) retries, exponential + jitter backoff with `RetryBaseDelay` (default 2 s).
   - **Retry predicate:** retries on `HttpRequestException`; retries on `OperationCanceledException` / `TaskCanceledException` only when `ex.CancellationToken != callerCt` (inner timeout, not caller cancel).
   - **`OnRetry` callback:** logs `WARN` with `attemptNumber`, `delay`, `ex.GetType().Name`, sanitised operation name; calls `ITelemetryService.TrackException(ex, { Job, AttemptNumber, IsTerminal="false" })`.
3. No circuit breaker (once-daily job; circuit break has no value).

**Construction:** Reads `IOptionsMonitor<ProductExportOptions>` once at ctor (singleton; pipeline rebuilt only on app restart, matching `CatalogResilienceService`). Adds a ctor invariant: `MaxRetryAttempts * DownloadTimeout < 20 min`.

---

### Named `HttpClient` registration — `"ProductExportDownload"`

**Responsibility:** Provides a socket-pooling, DNS-refreshing `HttpClient` for all external HTTP calls in the export download path. Fixes the latent Singleton-captures-Transient-HttpClient bug.

**Configuration (registered in `FileStorageModule`):**
```csharp
services.AddHttpClient("ProductExportDownload")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression  = DecompressionMethods.All,
    })
    .ConfigureHttpClient(c => c.Timeout = Timeout.InfiniteTimeSpan);

// REMOVE: services.AddTransient<HttpClient>();
// CHANGE: services.AddSingleton<AzureBlobStorageService>() remains, but constructor
//         now takes IHttpClientFactory, not HttpClient
services.AddSingleton<IDownloadResilienceService, DownloadResilienceService>();
```

`HttpClient.Timeout = InfiniteTimeSpan` is intentional and must be documented inline: per-call timeouts are enforced via linked `CancellationTokenSource` inside `DownloadResilienceService` and around the HEAD probe, not via `HttpClient.Timeout`.

---

### `DownloadFromUrlHandler` (modified)

**Responsibility:** Validates URL/container; runs HEAD probe under `HeadTimeout`; delegates download to `IBlobStorageService` under the Polly pipeline; converts all outcomes to the `DownloadFromUrlResponse` envelope.

**Constructor dependencies added:**
- `IDownloadResilienceService`
- `IHttpClientFactory` (for HEAD probe; resolves `"ProductExportDownload"`)

**HEAD probe contract:**
- Creates `CancellationTokenSource.CreateLinkedTokenSource(callerCt, headTimeoutCts)` where `headTimeoutCts` is bounded by `ProductExportOptions.HeadTimeout`.
- On timeout or exception: logs `Debug`, sets size to `0`, does NOT cancel the parent download.
- HEAD CTS must NOT be shared with the download CTS.

**Download contract:**
- Calls `IDownloadResilienceService.ExecuteWithResilienceAsync(ct => _blobStorage.DownloadFromUrlAsync(..., ct), "ProductExportDownload", callerCt)`.
- On success: returns `DownloadFromUrlResponse { Success = true, ... }`.
- On `OperationCanceledException` where `ex.CancellationToken == callerCt`: propagates (caller cancelled; no failure response emitted).
- On all other terminal exceptions (retry exhausted, hard HTTP error): catches, constructs failure `Params` (see Data Schemas), returns `DownloadFromUrlResponse { Success = false, ... }`.

---

### `AzureBlobStorageService` (modified — `DownloadFromUrlAsync` only)

**Responsibility:** Streams external URL to Azure Blob Storage. All other methods (blob CRUD via Azure SDK) are unchanged.

**Constructor change:** `HttpClient httpClient` → `IHttpClientFactory httpClientFactory`. The `_httpClientFactory` field replaces the `_httpClient` field.

**`DownloadFromUrlAsync` change:** Resolves `_httpClientFactory.CreateClient("ProductExportDownload")` per call (never cached as a field). Accepts and forwards the `CancellationToken` it receives (timeout already baked into the token by the caller). Calls `EnsureSuccessStatusCode()` to surface non-2xx as `HttpRequestException` for the retry predicate.

---

### `ProductExportDownloadJob` (modified)

**Responsibility:** Hangfire recurring job. Sends `DownloadFromUrlRequest`, emits exactly one terminal business event, rethrows on failure.

**Changes:**
- Decorated with `[Hangfire.AutomaticRetry(Attempts = 0)]` at class level. This disables Hangfire's default 10-attempt retry, ensuring retry logic lives exclusively in Polly.
- Emits one `TrackBusinessEvent("ProductExportDownload", { Status, ... })` per run: `"Success"`, `"Failed"`, `"Skipped"` (job disabled), or `"Cancelled"` (caller token fired — preserves NFR-3 exactly-one-event invariant).
- On `Success = false` response: emits `Failed` event then rethrows the structured exception so Hangfire records the run as failed.

---

## Data Schemas

### `ProductExportOptions` — new properties

```csharp
public class ProductExportOptions
{
    // existing
    public string Url           { get; set; } = null!;
    public string ContainerName { get; set; } = null!;

    // new — defaults match spec; omitting keys in appsettings preserves these defaults
    public TimeSpan HeadTimeout      { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan DownloadTimeout  { get; set; } = TimeSpan.FromSeconds(120);
    public int      MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryBaseDelay   { get; set; } = TimeSpan.FromSeconds(2);
}
```

Bound in `ServiceCollectionExtensions.cs` at the existing binding site. No new config section key.

---

### `DownloadFromUrlResponse.Params` — failure shape

All keys are `string`-valued (existing convention). New keys are additive; existing consumers reading `fileUrl` or `error` are unaffected.

| Key           | Type (string-encoded) | Present on                          | Example value                  |
|---------------|-----------------------|-------------------------------------|-------------------------------|
| `fileUrl`     | string (sanitised)    | all failures                        | `https://example.com/export/…` |
| `error`       | string                | all failures (existing key, kept)   | `"Connection refused"`        |
| `cause`       | string enum           | all failures                        | `"timeout"` / `"retry-exhausted"` / `"http-status"` / `"validation"` |
| `attemptCount`| int → string          | timeout, retry-exhausted            | `"4"`                         |
| `elapsedMs`   | long → string         | timeout, retry-exhausted            | `"14320"`                     |

**`cause` values:**
- `"timeout"` — the per-attempt or download-level `CancellationToken` fired (inner CTS, not caller).
- `"retry-exhausted"` — all `MaxRetryAttempts + 1` attempts failed with a retryable error.
- `"http-status"` — a non-retryable HTTP status (4xx except 408/429) was returned and `EnsureSuccessStatusCode` threw.
- `"validation"` — URL or container failed validation before any HTTP call.

URL logged in `Params["fileUrl"]` MUST have query string / token suffix redacted (replace with `[redacted]`) to comply with NFR-2.

---

### `TrackBusinessEvent` payload — `"ProductExportDownload"`

Emitted once per job run by `ProductExportDownloadJob`:

| Property        | Type   | Values                                    |
|-----------------|--------|-------------------------------------------|
| `Status`        | string | `"Success"` / `"Failed"` / `"Skipped"` / `"Cancelled"` |
| `AttemptCount`  | string | total attempts made (including retries)   |
| `ElapsedMs`     | string | total wall-clock ms for the job run       |
| `ErrorCode`     | string | present on `Failed` only; `"FileDownloadFailed"` |
| `Cause`         | string | present on `Failed` only; mirrors `Params["cause"]` |

---

### `TrackException` payload — per retry attempt

Emitted by `DownloadResilienceService.OnRetry` for each non-terminal failure:

| Property        | Value                                    |
|-----------------|------------------------------------------|
| `Job`           | `"ProductExportDownload"`               |
| `AttemptNumber` | 1-indexed attempt that just failed      |
| `IsTerminal`    | `"false"` (terminal failure tracked via business event) |

Exception itself: `args.Outcome.Exception` (full exception object, not `.Message`, to preserve stack trace in AI).

---

### Polly pipeline configuration (logical)

```
ResiliencePipelineBuilder<T>
  .AddRetry(new RetryStrategyOptions<T>
  {
      MaxRetryAttempts = options.MaxRetryAttempts,   // default 3
      BackoffType      = DelayBackoffType.Exponential,
      UseJitter        = true,
      Delay            = options.RetryBaseDelay,      // default 2 s
      ShouldHandle     = args => args.Outcome switch
      {
          { Exception: HttpRequestException }                                          => PredicateResult.True(),
          { Exception: OperationCanceledException e }
              when e.CancellationToken != callerCt                                    => PredicateResult.True(),
          _                                                                            => PredicateResult.False(),
      },
      OnRetry = args => { /* WARN log + TrackException */ return ValueTask.CompletedTask; },
  })
  .AddTimeout(new TimeoutStrategyOptions
  {
      Timeout = options.DownloadTimeout,              // default 120 s
  })
  .Build();
```

The inner `AddTimeout` wraps each attempt; `AddRetry` is the outer strategy so timeouts on one attempt trigger a retry (standard Polly v8 ordering).
```