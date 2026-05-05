### task: extend-product-export-options

**Goal:** Add four new tunable properties (`HeadTimeout`, `DownloadTimeout`, `MaxRetryAttempts`, `RetryBaseDelay`) to `ProductExportOptions` so resilience policies can be configured via `appsettings`.

**Context:**

`ProductExportOptions` is the existing options class bound from configuration for the product export download feature. The feature currently has no resilience knobs — the download path uses ambient `HttpClient` defaults. We must add four properties to drive the new resilience pipeline and HEAD-probe timeout. Defaults are mandated by spec/design and must be applied so omitting keys in `appsettings` preserves backwards-compatible behaviour.

Defaults (from design):
- `HeadTimeout` = 10 s
- `DownloadTimeout` = 120 s
- `MaxRetryAttempts` = 3
- `RetryBaseDelay` = 2 s

The class must remain a class with public settable properties (project rule §3 — never use C# records for DTOs/options exposed via DI/config). The class is bound at `ServiceCollectionExtensions.cs` at the existing binding site (do not introduce a new config section key).

**Files to create/modify:**
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ProductExportOptions.cs` — add four new properties with defaults.

**Implementation steps:**

1. Open `backend/src/Anela.Heblo.Domain/Features/Configuration/ProductExportOptions.cs`. Add `using System;` if not already present.
2. Append the four new properties to the existing class:

```csharp
public TimeSpan HeadTimeout      { get; set; } = TimeSpan.FromSeconds(10);
public TimeSpan DownloadTimeout  { get; set; } = TimeSpan.FromSeconds(120);
public int      MaxRetryAttempts { get; set; } = 3;
public TimeSpan RetryBaseDelay   { get; set; } = TimeSpan.FromSeconds(2);
```

3. Do NOT change the existing properties (`Url`, `ContainerName`). Do NOT add `[Required]` attributes to the new properties — defaults make them optional.
4. Run `dotnet format` on the file (project rule §2).
5. Run `dotnet build` to confirm no compile errors elsewhere (e.g. consumers of the class).

**Tests to write:**

Add a test class `ProductExportOptionsTests` under `backend/test/Anela.Heblo.Tests/Features/FileStorage/Configuration/ProductExportOptionsTests.cs`:

- `Defaults_HeadTimeout_Is10Seconds` — `new ProductExportOptions().HeadTimeout` equals `TimeSpan.FromSeconds(10)`.
- `Defaults_DownloadTimeout_Is120Seconds` — `new ProductExportOptions().DownloadTimeout` equals `TimeSpan.FromSeconds(120)`.
- `Defaults_MaxRetryAttempts_Is3` — `new ProductExportOptions().MaxRetryAttempts` equals `3`.
- `Defaults_RetryBaseDelay_Is2Seconds` — `new ProductExportOptions().RetryBaseDelay` equals `TimeSpan.FromSeconds(2)`.
- `Configuration_BindsTimeSpanFromString` — bind `{"HeadTimeout":"00:00:30"}` via `ConfigurationBuilder().AddInMemoryCollection(...)` + `Get<ProductExportOptions>()` and assert `HeadTimeout == TimeSpan.FromSeconds(30)`.

**Acceptance criteria:**

- `ProductExportOptions` exposes the four new public properties with the documented defaults.
- `dotnet build` succeeds.
- All five new tests pass.
- No existing test is broken.
- File passes `dotnet format --verify-no-changes`.

---

### task: create-download-resilience-service-interface

**Goal:** Introduce `IDownloadResilienceService` — a feature-local resilience abstraction mirroring the shape of the existing `ICatalogResilienceService`.

**Context:**

The codebase already uses Polly v8 via `ICatalogResilienceService` (catalog feature). For the FileStorage download path we keep retries feature-local (no shared cross-cutting service yet — YAGNI) and mirror the shape exactly so reviewers see a familiar pattern. There is no circuit breaker (the job runs once daily — circuit-breaking has no value).

Required interface shape (from design):

```csharp
public interface IDownloadResilienceService
{
    Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default);
}
```

`operationName` is a constant string (e.g. `"ProductExportDownload"`) — never the URL — so it is safe to log without leaking tokens (NFR-2).

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/IDownloadResilienceService.cs` — new file.

**Implementation steps:**

1. Create directory `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/` if it does not already exist.
2. Create `IDownloadResilienceService.cs` with namespace `Anela.Heblo.Application.Features.FileStorage.Infrastructure`.
3. Add the interface verbatim:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Anela.Heblo.Application.Features.FileStorage.Infrastructure;

public interface IDownloadResilienceService
{
    Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default);
}
```

4. Run `dotnet format` on the file.

**Tests to write:**

No tests for the interface alone (an interface with no implementation has no behaviour to verify).

**Acceptance criteria:**

- File `IDownloadResilienceService.cs` exists at the specified path with the specified namespace and signature.
- `dotnet build` succeeds.
- File passes `dotnet format --verify-no-changes`.

---

### task: implement-download-resilience-service

**Goal:** Implement `DownloadResilienceService` as a Polly v8 pipeline (per-attempt timeout + retry with jitter) that distinguishes caller-cancel from inner timeout, emits `WARN` logs and `TrackException` telemetry on each retry, and enforces a wall-clock invariant.

**Context:**

This is the singleton resilience service consumed by `DownloadFromUrlHandler`. It must:

1. Read `ProductExportOptions` once at construction (via `IOptions<ProductExportOptions>`) — matches existing `CatalogResilienceService` pattern; option hot-reload is out of scope.
2. Build a Polly v8 `ResiliencePipeline<T>` per call (because retry predicate must close over the caller's `CancellationToken`).
3. Pipeline composition (from design, **outer → inner**):
   - Retry strategy (outer) — up to `MaxRetryAttempts` retries, exponential backoff with jitter, `Delay = RetryBaseDelay`. **Retry predicate:**
     - Retry on any `HttpRequestException`.
     - Retry on `OperationCanceledException` / `TaskCanceledException` **only** when `ex.CancellationToken != callerCt` (i.e. inner timeout fired, not caller cancel).
     - Otherwise do not retry.
   - **`OnRetry` callback:** log at `WARN` with `attemptNumber`, `delay`, `ex.GetType().Name`, `operationName`. Call `ITelemetryService.TrackException(args.Outcome.Exception, properties)` where `properties` ⊇ `{ "Job": operationName, "AttemptNumber": (1-indexed retry number), "IsTerminal": "false" }`. Do NOT log `args.Outcome.Exception?.Message` directly if it could contain the URL — log the type name and a sanitised message only.
   - Timeout strategy (inner) — `Timeout = DownloadTimeout`, applied per attempt. The Polly v8 `AddTimeout` strategy creates a linked CTS internally; the linked token is passed to the operation, so an inner timeout produces an `OperationCanceledException` whose `CancellationToken` is **not** the caller's token (this is what the retry predicate keys off).
4. **Constructor invariant:** `MaxRetryAttempts * DownloadTimeout < TimeSpan.FromMinutes(20)` — throw `InvalidOperationException` if violated. This protects against the configured worst-case wall-clock exceeding Hangfire's `InvisibilityTimeout` (30 min).
5. Execute the pipeline with the caller's `CancellationToken`. The user-supplied `operation` delegate receives the per-attempt token from Polly.
6. The service is registered **Singleton** (next task wires DI).

`ITelemetryService` already exists in the codebase (`Anela.Heblo.Application.Common.Telemetry` or similar — locate via `grep`); use the existing canonical interface.

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs` — new file.

**Implementation steps:**

1. Locate the existing `ICatalogResilienceService` implementation file (`grep -r "class CatalogResilienceService"`) and read it to mirror logger/telemetry usage and `using` directives.
2. Create `DownloadResilienceService.cs` with namespace `Anela.Heblo.Application.Features.FileStorage.Infrastructure`.
3. Class outline:

```csharp
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Domain.Features.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Anela.Heblo.Application.Features.FileStorage.Infrastructure;

public class DownloadResilienceService : IDownloadResilienceService
{
    private readonly ILogger<DownloadResilienceService> _logger;
    private readonly ITelemetryService _telemetry;
    private readonly ProductExportOptions _options;

    public DownloadResilienceService(
        IOptions<ProductExportOptions> options,
        ITelemetryService telemetry,
        ILogger<DownloadResilienceService> logger)
    {
        _logger = logger;
        _telemetry = telemetry;
        _options = options.Value;

        var worstCase = TimeSpan.FromTicks(_options.DownloadTimeout.Ticks * (_options.MaxRetryAttempts + 1));
        if (worstCase >= TimeSpan.FromMinutes(20))
        {
            throw new InvalidOperationException(
                $"ProductExportOptions: MaxRetryAttempts ({_options.MaxRetryAttempts}) * DownloadTimeout ({_options.DownloadTimeout}) must be < 20 minutes; got worst-case {worstCase}.");
        }
    }

    public async Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        var pipeline = BuildPipeline<T>(operationName, cancellationToken);
        return await pipeline.ExecuteAsync(
            async ct => await operation(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    private ResiliencePipeline<T> BuildPipeline<T>(string operationName, CancellationToken callerCt)
    {
        return new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                MaxRetryAttempts = _options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = _options.RetryBaseDelay,
                ShouldHandle = args =>
                {
                    if (args.Outcome.Exception is HttpRequestException)
                        return PredicateResult.True();
                    if (args.Outcome.Exception is OperationCanceledException oce
                        && oce.CancellationToken != callerCt)
                        return PredicateResult.True();
                    return PredicateResult.False();
                },
                OnRetry = args =>
                {
                    var attemptNumber = args.AttemptNumber + 1; // 1-indexed
                    var ex = args.Outcome.Exception;
                    _logger.LogWarning(
                        "Retry {AttemptNumber} for {OperationName} after {Delay} due to {ExceptionType}",
                        attemptNumber, operationName, args.RetryDelay, ex?.GetType().Name);
                    if (ex != null)
                    {
                        _telemetry.TrackException(ex, new Dictionary<string, string>
                        {
                            ["Job"] = operationName,
                            ["AttemptNumber"] = attemptNumber.ToString(),
                            ["IsTerminal"] = "false",
                        });
                    }
                    return ValueTask.CompletedTask;
                },
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = _options.DownloadTimeout,
            })
            .Build();
    }
}
```

4. If `ITelemetryService.TrackException` has a different signature in this codebase, adapt the call — but the property dictionary keys (`Job`, `AttemptNumber`, `IsTerminal`) are mandatory.
5. Run `dotnet format` and `dotnet build`.

**Tests to write:**

Test file: `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/DownloadResilienceServiceTests.cs`. Use the existing test fakes for `ITelemetryService` and `ILogger<T>`.

- `Constructor_Throws_When_WorstCaseExceeds20Minutes` — set `MaxRetryAttempts=10`, `DownloadTimeout=2min` → ctor throws `InvalidOperationException`.
- `Constructor_Succeeds_With_Defaults` — defaults (3 × 120 s = 8 min) → no throw.
- `ExecuteWithResilienceAsync_ReturnsResult_OnFirstAttemptSuccess` — operation returns `42` immediately → result is `42`, no `TrackException` call, attempt counter is 1.
- `ExecuteWithResilienceAsync_RetriesOn_HttpRequestException_ThenSucceeds` — first call throws `HttpRequestException`, second returns `"ok"` → result is `"ok"`, exactly one `TrackException` call with `IsTerminal=false`, `AttemptNumber=1`.
- `ExecuteWithResilienceAsync_DoesNotRetry_OnCallerCancel` — operation observes the caller token and throws `OperationCanceledException(callerCt)` → exception propagates, zero retries, zero `TrackException` calls.
- `ExecuteWithResilienceAsync_RetriesOn_InnerTimeout` — operation delays 200 ms; configure `DownloadTimeout = 50 ms`, `MaxRetryAttempts = 1` → expect 2 attempts, 1 `TrackException` call, then final `OperationCanceledException` propagates.
- `ExecuteWithResilienceAsync_ExhaustsRetries_OnPersistentHttpRequestException` — every attempt throws `HttpRequestException("boom")`, `MaxRetryAttempts = 3` → 4 total attempts, 3 `TrackException` calls (`AttemptNumber` = 1, 2, 3), final `HttpRequestException` propagates.
- `ExecuteWithResilienceAsync_DoesNotRetry_OnNonRetryableException` — operation throws `InvalidOperationException` → 1 attempt, 0 retries, exception propagates.

For the inner-timeout test, set `RetryBaseDelay = TimeSpan.FromMilliseconds(1)` to keep the test fast.

**Acceptance criteria:**

- `DownloadResilienceService` compiles and all 8 tests pass.
- Constructor invariant rejects mis-configured defaults.
- Retry predicate distinguishes caller cancel from inner timeout (verified by tests).
- `TrackException` is called exactly once per non-terminal failure with the documented properties.
- File passes `dotnet format --verify-no-changes`.

---

### task: register-named-httpclient-and-resilience

**Goal:** In `FileStorageModule`, register the `"ProductExportDownload"` named `HttpClient` (with `SocketsHttpHandler`, 5-min connection lifetime, infinite timeout), register `IDownloadResilienceService` as Singleton, and remove the broken `AddTransient<HttpClient>()` registration.

**Context:**

`FileStorageModule.cs:28` currently registers `services.AddTransient<HttpClient>()` and `AzureBlobStorageService` as Singleton — the singleton captures the transient `HttpClient` for the application lifetime, sockets are never recycled and DNS is never re-resolved. This is a latent bug and is the root cause of long-running `Faulted` dependencies.

The fix:
- Remove `services.AddTransient<HttpClient>()`.
- Add a named `HttpClient` registration using `IHttpClientFactory` with:
  - `PooledConnectionLifetime = TimeSpan.FromMinutes(5)` — recycles sockets and refreshes DNS.
  - `AutomaticDecompression = DecompressionMethods.All`.
  - `HttpClient.Timeout = Timeout.InfiniteTimeSpan` — per-call timeout is enforced via linked `CancellationTokenSource`, **not** via `HttpClient.Timeout`. This is intentional and **must be documented inline**.
- Register `IDownloadResilienceService` as Singleton.

The named-client config (verbatim from design):

```csharp
services.AddHttpClient("ProductExportDownload")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression  = DecompressionMethods.All,
    })
    .ConfigureHttpClient(c => c.Timeout = Timeout.InfiniteTimeSpan);
```

Both `DownloadFromUrlHandler` (HEAD probe) and `AzureBlobStorageService.DownloadFromUrlAsync` (GET) must resolve **the same named client** so timeout/handler config is consistent. The constant `"ProductExportDownload"` is shared — define it as a `public const string` to avoid stringly-typed coupling across files.

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` — remove `AddTransient<HttpClient>()`, add `AddHttpClient("ProductExportDownload")` + `AddSingleton<IDownloadResilienceService, DownloadResilienceService>()`, and add the constant.

**Implementation steps:**

1. Open `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`.
2. Add `using` directives at top:

```csharp
using System;
using System.Net;
using System.Net.Http;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
```

3. Add a `public const string ProductExportDownloadClientName = "ProductExportDownload";` at class scope (or a `public static class FileStorageHttpClientNames` if the team prefers — pick the simpler `const`).
4. Inside `AddFileStorageModule` (or whatever the existing extension method is named), **remove** the line `services.AddTransient<HttpClient>();` if present.
5. **Add** the named-client registration immediately above the existing `services.AddSingleton<...AzureBlobStorageService...>()` line:

```csharp
services.AddHttpClient(ProductExportDownloadClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression  = DecompressionMethods.All,
    })
    .ConfigureHttpClient(c =>
    {
        // Intentional: per-call timeout is enforced by linked CancellationTokenSource
        // inside DownloadResilienceService and around the HEAD probe in
        // DownloadFromUrlHandler. HttpClient.Timeout is left infinite so it does
        // not race with the linked CTS.
        c.Timeout = Timeout.InfiniteTimeSpan;
    });

services.AddSingleton<IDownloadResilienceService, DownloadResilienceService>();
```

6. If `Microsoft.Extensions.Http` is not already a transitive dependency of the Application project, add a `<PackageReference Include="Microsoft.Extensions.Http" />` to `Anela.Heblo.Application.csproj` (it almost certainly already is — check `dotnet build` output).
7. Run `dotnet format` and `dotnet build`.

**Tests to write:**

Test file: `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs`.

- `AddFileStorageModule_RegistersNamedHttpClient_ProductExportDownload` — build a `ServiceCollection`, call `AddFileStorageModule(...)` (with required config), then resolve `IHttpClientFactory` from `BuildServiceProvider()` and assert `factory.CreateClient("ProductExportDownload")` returns a non-null client whose `Timeout == Timeout.InfiniteTimeSpan`.
- `AddFileStorageModule_DoesNotRegisterTransientHttpClient` — `serviceCollection.Any(d => d.ServiceType == typeof(HttpClient) && d.Lifetime == ServiceLifetime.Transient)` is `false`.
- `AddFileStorageModule_RegistersDownloadResilienceService_AsSingleton` — assert one descriptor for `IDownloadResilienceService` with `Lifetime == ServiceLifetime.Singleton` and `ImplementationType == typeof(DownloadResilienceService)`.
- `AddFileStorageModule_NamedClient_ConstantIsExported` — reflection assertion that `FileStorageModule.ProductExportDownloadClientName == "ProductExportDownload"` so refactors that change the constant break this test.

**Acceptance criteria:**

- `dotnet build` succeeds.
- `services.AddTransient<HttpClient>()` is gone from `FileStorageModule.cs`.
- The named client is registered exactly as specified; constant is exported.
- All 4 tests pass.
- No existing test fails.

---

### task: refactor-azure-blob-storage-service-to-httpclientfactory

**Goal:** Migrate `AzureBlobStorageService` from a constructor-injected `HttpClient` to `IHttpClientFactory`, resolving the `"ProductExportDownload"` named client per `DownloadFromUrlAsync` call. All other methods are unchanged.

**Context:**

`AzureBlobStorageService` is currently registered Singleton and takes `HttpClient` in its constructor — the client is held for the lifetime of the app, sockets never recycle. Only `DownloadFromUrlAsync` makes an external HTTP call; every other method talks to the Azure SDK. We migrate **only that path** to `IHttpClientFactory` (minimal blast radius). Other methods do not change behaviour.

The constructor change must be coordinated with the previous task's named-client registration. The constant `FileStorageModule.ProductExportDownloadClientName` ("ProductExportDownload") is the named client to resolve.

`DownloadFromUrlAsync` must:
- Resolve a fresh `HttpClient` via `_httpClientFactory.CreateClient(FileStorageModule.ProductExportDownloadClientName)` per call (never cached).
- Forward the `CancellationToken` it receives unchanged (timeout is already baked into the token by `DownloadResilienceService` — the service does **not** wrap it again).
- Call `EnsureSuccessStatusCode()` on the response so non-2xx surfaces as `HttpRequestException` for the retry predicate.
- Stream the response into the blob (existing behaviour preserved).

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs` — change constructor parameter, change one method body.

**Implementation steps:**

1. Open `backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs`.
2. Replace the `private readonly HttpClient _httpClient;` field with `private readonly IHttpClientFactory _httpClientFactory;`.
3. Update the constructor signature: replace `HttpClient httpClient` with `IHttpClientFactory httpClientFactory`. Update the assignment accordingly.
4. Add `using` directives if missing: `using Anela.Heblo.Application.Features.FileStorage;` (for the constant — if a circular-using arises, define the constant in a shared static class instead).
5. Inside `DownloadFromUrlAsync(string url, string containerName, string blobName, CancellationToken cancellationToken)` (signature exact name may differ — adapt to existing):
   - Replace any reference to `_httpClient` with a local `var httpClient = _httpClientFactory.CreateClient(FileStorageModule.ProductExportDownloadClientName);`.
   - Use `using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);`.
   - Call `response.EnsureSuccessStatusCode();` immediately after.
   - Continue with the existing streaming-to-blob logic, passing `cancellationToken` through to all async calls.
6. Search the codebase for any other call sites or tests that construct `AzureBlobStorageService` directly with an `HttpClient`. Update those constructions to pass an `IHttpClientFactory` (likely a Moq mock).
7. Run `dotnet format` and `dotnet build`.

**Tests to write:**

Update or add tests in `backend/test/Anela.Heblo.Tests/Features/FileStorage/Services/AzureBlobStorageServiceTests.cs`:

- `Constructor_AcceptsHttpClientFactory` — constructs the service via Moq `IHttpClientFactory` without exception.
- `DownloadFromUrlAsync_ResolvesNamedClient_ProductExportDownload` — Moq an `IHttpClientFactory`, configure `CreateClient("ProductExportDownload")` to return a `HttpClient` backed by a stub `HttpMessageHandler` that returns `200 OK` with a small body. Assert `factory.CreateClient` is called with the literal `"ProductExportDownload"`.
- `DownloadFromUrlAsync_ThrowsHttpRequestException_OnNon2xx` — stub handler returns `503 ServiceUnavailable`. Call `DownloadFromUrlAsync(...)` and assert it throws `HttpRequestException` (because of `EnsureSuccessStatusCode`).
- `DownloadFromUrlAsync_ForwardsCancellationToken` — stub handler observes the passed token; cancel the token before issuing the call → `OperationCanceledException` propagates and `factory.CreateClient` was still called once.
- `DownloadFromUrlAsync_StreamsBodyToBlob_OnSuccess` — happy path: assert the blob client received the expected bytes. (Use existing blob mocks already in this test file.)

**Acceptance criteria:**

- `dotnet build` succeeds.
- The `_httpClient` field is gone; `_httpClientFactory` field exists.
- All other methods of `AzureBlobStorageService` are byte-identical (no incidental changes).
- All new tests pass; existing tests pass after fixture updates.
- File passes `dotnet format --verify-no-changes`.

---

### task: update-download-from-url-handler

**Goal:** Refactor `DownloadFromUrlHandler` to (a) run a HEAD probe under a HEAD-only linked CTS that **never** cancels the parent download, (b) delegate the GET to `IDownloadResilienceService`, (c) preserve the existing `DownloadFromUrlResponse` envelope and add the new failure `Params` (`cause`, `attemptCount`, `elapsedMs`), and (d) propagate caller cancellation unchanged.

**Context:**

`DownloadFromUrlHandler` is the MediatR handler invoked by `ProductExportDownloadJob`. It currently makes an unguarded HTTP GET. After this task it must:

1. **Validate** URL/container (existing behaviour preserved — on validation failure return `Success=false`, `ErrorCode=FileDownloadFailed`, `Params["cause"]="validation"`).
2. **HEAD probe** under a CTS linked **only** to `callerCt + HeadTimeout` — must NOT share a CTS with the download path. On HEAD timeout or any HEAD exception: log at `Debug`, set probed size to `0`, **continue** to the download. This preserves the current contract (FR-1 acceptance).
3. **GET** via `IDownloadResilienceService.ExecuteWithResilienceAsync(ct => _blobStorageService.DownloadFromUrlAsync(..., ct), "ProductExportDownload", callerCt)`. Time the call with `Stopwatch` for `elapsedMs`. Track an `attemptCount` by counting `TrackException` callbacks if available, otherwise read from a Polly `ResilienceContext` property — simplest implementation: pass an `int attemptCount = 0` captured variable into the operation delegate and `Interlocked.Increment` on each invocation.
4. **Outcome mapping:**
   - **Success** → `DownloadFromUrlResponse { Success = true, ... }` (existing fields; `attemptCount` is informational only on success and is not required in `Params` per design — keep the existing envelope shape).
   - **`OperationCanceledException` where `ex.CancellationToken == callerCt`** → rethrow as-is. Do NOT emit a failure response (the job will handle "Cancelled").
   - **Inner timeout exhausted** (`OperationCanceledException` from inner CTS bubbling out after retries) → `Success=false`, `ErrorCode=FileDownloadFailed`, `Params["cause"]="timeout"`, `Params["attemptCount"]=…`, `Params["elapsedMs"]=…`, `Params["fileUrl"]=…(redacted)`.
   - **`HttpRequestException` after retries exhausted** → `Success=false`, `ErrorCode=FileDownloadFailed`, `Params["cause"]="retry-exhausted"`. If the exception's `StatusCode` (when present) maps to a non-retryable 4xx, use `Params["cause"]="http-status"` — distinguish via the predicate's behaviour: if Polly bubbled it without retrying, treat as `"http-status"`; if it tried `MaxRetryAttempts + 1` times, treat as `"retry-exhausted"`. Simplest reliable heuristic: if `attemptCount > 1` ⇒ `"retry-exhausted"`, else ⇒ `"http-status"`.
   - **Other exception** → `Success=false`, `ErrorCode=FileDownloadFailed`, `Params["cause"]="retry-exhausted"`, plus `Params["error"]=ex.Message`.
5. **URL redaction (NFR-2):** `Params["fileUrl"]` must have query string and any `?token=`/`?sig=` suffix replaced with `[redacted]`. Implement a small private helper `static string RedactUrl(string url)` that strips the query string entirely (`new UriBuilder(url) { Query = string.Empty }.Uri.ToString()`) — if `url` is malformed, return `"[redacted]"`.

The existing `DownloadFromUrlResponse` `Params` dictionary is `IDictionary<string, string>`. Keys (`fileUrl`, `error`) already exist; new keys (`cause`, `attemptCount`, `elapsedMs`) are additive.

`DownloadFromUrlRequest`, `DownloadFromUrlResponse`, and `ErrorCodes.FileDownloadFailed` are existing types — do not modify them.

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs` — refactor `Handle` method.

**Implementation steps:**

1. Read the existing handler to capture exact request/response types and validation logic. Preserve the validation block verbatim, but on validation failure attach `Params["cause"] = "validation"` to the failure response.
2. Add constructor dependencies:

```csharp
private readonly IDownloadResilienceService _resilience;
private readonly IHttpClientFactory _httpClientFactory;
private readonly IOptions<ProductExportOptions> _options; // if not already present
// existing: IBlobStorageService, ILogger<DownloadFromUrlHandler>, etc.
```

3. Implement the HEAD probe as a private method:

```csharp
private async Task<long> ProbeContentLengthAsync(string url, CancellationToken callerCt)
{
    using var headCts = CancellationTokenSource.CreateLinkedTokenSource(callerCt);
    headCts.CancelAfter(_options.Value.HeadTimeout);
    try
    {
        var client = _httpClientFactory.CreateClient(FileStorageModule.ProductExportDownloadClientName);
        using var req = new HttpRequestMessage(HttpMethod.Head, url);
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, headCts.Token).ConfigureAwait(false);
        return resp.Content.Headers.ContentLength ?? 0L;
    }
    catch (OperationCanceledException) when (!callerCt.IsCancellationRequested)
    {
        _logger.LogDebug("HEAD probe timed out for ProductExportDownload");
        return 0L;
    }
    catch (Exception ex)
    {
        _logger.LogDebug(ex, "HEAD probe failed for ProductExportDownload");
        return 0L;
    }
}
```

The `when (!callerCt.IsCancellationRequested)` guard ensures that if the caller cancelled, the cancellation **does** propagate (so the parent caller-cancel path remains intact) — but a HEAD-only timeout (caller still alive) is swallowed.

4. Refactor `Handle`:

```csharp
public async Task<DownloadFromUrlResponse> Handle(DownloadFromUrlRequest request, CancellationToken cancellationToken)
{
    // existing validation (preserve) — on failure set Params["cause"]="validation" and return.

    var sw = Stopwatch.StartNew();
    int attemptCount = 0;
    var redactedUrl = RedactUrl(request.FileUrl);

    // HEAD probe (best-effort; never cancels parent)
    var probedSize = await ProbeContentLengthAsync(request.FileUrl, cancellationToken).ConfigureAwait(false);

    try
    {
        var result = await _resilience.ExecuteWithResilienceAsync(
            async ct =>
            {
                Interlocked.Increment(ref attemptCount);
                return await _blobStorageService.DownloadFromUrlAsync(
                    request.FileUrl, request.ContainerName, request.BlobName, ct).ConfigureAwait(false);
            },
            "ProductExportDownload",
            cancellationToken).ConfigureAwait(false);

        sw.Stop();
        return new DownloadFromUrlResponse
        {
            Success = true,
            // existing success fields (BlobUri, Size, etc.) preserved
            // …
        };
    }
    catch (OperationCanceledException oce) when (oce.CancellationToken == cancellationToken)
    {
        // caller cancelled — propagate
        throw;
    }
    catch (OperationCanceledException oce)
    {
        sw.Stop();
        return Failure(redactedUrl, "timeout", attemptCount, sw.ElapsedMilliseconds, oce.Message);
    }
    catch (HttpRequestException hre)
    {
        sw.Stop();
        var cause = attemptCount > 1 ? "retry-exhausted" : "http-status";
        return Failure(redactedUrl, cause, attemptCount, sw.ElapsedMilliseconds, hre.Message);
    }
    catch (Exception ex)
    {
        sw.Stop();
        _logger.LogError(ex, "Unexpected failure during ProductExportDownload");
        return Failure(redactedUrl, "retry-exhausted", attemptCount, sw.ElapsedMilliseconds, ex.Message);
    }
}

private static DownloadFromUrlResponse Failure(string redactedUrl, string cause, int attemptCount, long elapsedMs, string error) =>
    new()
    {
        Success = false,
        ErrorCode = ErrorCodes.FileDownloadFailed,
        Params = new Dictionary<string, string>
        {
            ["fileUrl"]      = redactedUrl,
            ["cause"]        = cause,
            ["attemptCount"] = attemptCount.ToString(),
            ["elapsedMs"]    = elapsedMs.ToString(),
            ["error"]        = error,
        },
    };

private static string RedactUrl(string url)
{
    try
    {
        var ub = new UriBuilder(url) { Query = string.Empty };
        return ub.Uri.ToString();
    }
    catch
    {
        return "[redacted]";
    }
}
```

5. Adapt the success branch's exact field assignments to whatever `DownloadFromUrlResponse` already exposes (do not invent new success fields).
6. Run `dotnet format` and `dotnet build`.

**Tests to write:**

Test file: `backend/test/Anela.Heblo.Tests/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandlerTests.cs`. Use Moq for `IDownloadResilienceService`, `IHttpClientFactory`, `IBlobStorageService`.

- `Handle_ReturnsSuccess_OnHappyPath` — resilience executes operation once, returns success. Response has `Success = true`.
- `Handle_HeadProbeTimeout_DoesNotCancelDownload` — `IHttpClientFactory.CreateClient("ProductExportDownload")` returns an `HttpClient` whose handler delays 5 s. Set `HeadTimeout = 50 ms`. The download mock succeeds. Assert handler returns `Success = true` and the download mock was invoked with a non-cancelled token.
- `Handle_HeadProbeFailure_LogsDebug_AndContinues` — HEAD throws `HttpRequestException`. Download succeeds. Assert success response and a `Debug`-level log entry.
- `Handle_RetryExhausted_ReturnsFailure_With_Cause_RetryExhausted` — resilience throws `HttpRequestException` after 4 invocations of the operation delegate. Assert: `Success = false`, `Params["cause"] == "retry-exhausted"`, `Params["attemptCount"] == "4"`, `Params["elapsedMs"]` is numeric, `Params["fileUrl"]` does NOT contain a query string.
- `Handle_HardHttpStatus_ReturnsFailure_With_Cause_HttpStatus` — resilience throws `HttpRequestException` after exactly 1 invocation (no retry). Assert `Params["cause"] == "http-status"`, `attemptCount == "1"`.
- `Handle_InnerTimeout_ReturnsFailure_With_Cause_Timeout` — resilience throws `OperationCanceledException` whose token is NOT the caller token. Assert `Params["cause"] == "timeout"`.
- `Handle_CallerCancellation_PropagatesException` — caller token cancelled; resilience throws `OperationCanceledException(callerCt)`. Assert handler rethrows `OperationCanceledException` and **no** `DownloadFromUrlResponse` is returned.
- `Handle_RedactsUrl_RemovesQueryString` — request URL `https://example.com/export.csv?token=secret123`. Force a failure. Assert `Params["fileUrl"] == "https://example.com/export.csv"` (no `secret123`).
- `Handle_ValidationFailure_SetsCauseValidation` — invalid URL or container. Assert `Success=false`, `Params["cause"]=="validation"`.

**Acceptance criteria:**

- `dotnet build` succeeds.
- All 9 new tests pass; existing handler tests pass (update fixtures for new constructor deps).
- `Params["fileUrl"]` is always redacted on failure (verified by test).
- Caller cancel propagates as `OperationCanceledException`; does not produce a failure response.
- HEAD probe timeout never cancels the download (verified by test).
- File passes `dotnet format --verify-no-changes`.

---

### task: disable-hangfire-auto-retry

**Goal:** Decorate `ProductExportDownloadJob` with `[Hangfire.AutomaticRetry(Attempts = 0)]`, ensure exactly one terminal `TrackBusinessEvent` per run with `Status ∈ {Success, Failed, Skipped, Cancelled}`, and rethrow on failure so Hangfire's run history is accurate.

**Context:**

Hangfire's default `AutomaticRetryAttribute.Attempts = 10`. Without explicit override, layering Polly retries (3) on top of Hangfire retries (10) yields up to 4 × 11 = 44 attempts per scheduled run, with Hangfire backoff of 0/30/60/120/… seconds — exactly the shape of the "3 faults in a 24h window for a once-daily job" anomaly. The fix is surgical: add the attribute at class level on this one job (NOT globally — that would change behaviour for every other recurring job in the codebase).

After this task, retries live exclusively in Polly, and Hangfire records a single run per scheduled fire.

The job must emit **exactly one** `TrackBusinessEvent("ProductExportDownload", { Status, ... })` per invocation:
- `"Success"` — handler returned `Success = true`.
- `"Failed"` — handler returned `Success = false` (after Polly retries exhausted or hard failure).
- `"Skipped"` — job is disabled by configuration (existing behaviour, if applicable).
- `"Cancelled"` — `OperationCanceledException` propagated from handler (caller token fired).

On `Failed`, the job **must rethrow** the structured exception (or wrap the failure into an `Exception`) **after** emitting the business event, so Hangfire records the run as failed. With `[AutomaticRetry(Attempts = 0)]` this rethrow does NOT trigger another attempt.

`TrackBusinessEvent` payload (from design):

| Property      | Value                                       |
|---------------|---------------------------------------------|
| `Status`      | `"Success"` / `"Failed"` / `"Skipped"` / `"Cancelled"` |
| `AttemptCount`| total attempts (read from response.Params or counted) |
| `ElapsedMs`   | total wall-clock ms                         |
| `ErrorCode`   | present on `Failed` only; `"FileDownloadFailed"` |
| `Cause`       | present on `Failed` only; mirrors `Params["cause"]` |

**Files to create/modify:**
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs` — add attribute, restructure terminal-event emission and rethrow.

**Implementation steps:**

1. Open `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJob.cs`.
2. Add `using Hangfire;` if not present.
3. Add the class-level attribute:

```csharp
[Hangfire.AutomaticRetry(Attempts = 0)]
public class ProductExportDownloadJob : IRecurringJob
{
    // …
}
```

4. Refactor `ExecuteAsync` to:

```csharp
public async Task ExecuteAsync(CancellationToken cancellationToken)
{
    if (!_options.Value.Enabled) // existing guard, if present
    {
        _telemetry.TrackBusinessEvent("ProductExportDownload", new Dictionary<string, string>
        {
            ["Status"] = "Skipped",
        });
        return;
    }

    var sw = Stopwatch.StartNew();
    DownloadFromUrlResponse? response = null;
    try
    {
        response = await _mediator.Send(new DownloadFromUrlRequest
        {
            FileUrl = _options.Value.Url,
            ContainerName = _options.Value.ContainerName,
            // existing fields…
        }, cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        sw.Stop();
        _telemetry.TrackBusinessEvent("ProductExportDownload", new Dictionary<string, string>
        {
            ["Status"]    = "Cancelled",
            ["ElapsedMs"] = sw.ElapsedMilliseconds.ToString(),
        });
        throw;
    }

    sw.Stop();
    var elapsedMs = sw.ElapsedMilliseconds.ToString();
    var attemptCount = response?.Params != null && response.Params.TryGetValue("attemptCount", out var ac) ? ac : "1";

    if (response is { Success: true })
    {
        _telemetry.TrackBusinessEvent("ProductExportDownload", new Dictionary<string, string>
        {
            ["Status"]       = "Success",
            ["AttemptCount"] = attemptCount,
            ["ElapsedMs"]    = elapsedMs,
        });
        return;
    }

    var props = new Dictionary<string, string>
    {
        ["Status"]       = "Failed",
        ["AttemptCount"] = attemptCount,
        ["ElapsedMs"]    = elapsedMs,
        ["ErrorCode"]    = response?.ErrorCode.ToString() ?? "FileDownloadFailed",
    };
    if (response?.Params != null && response.Params.TryGetValue("cause", out var cause))
        props["Cause"] = cause;

    _telemetry.TrackBusinessEvent("ProductExportDownload", props);

    // Rethrow so Hangfire records run as Failed. AutomaticRetry(Attempts=0) prevents re-execution.
    throw new InvalidOperationException(
        $"ProductExportDownload failed (cause={(response?.Params != null && response.Params.TryGetValue("cause", out var c) ? c : "unknown")}, attempts={attemptCount}).");
}
```

5. If the existing job lacks an `Enabled` flag, omit the `Skipped` branch — but still treat the absence of a job invocation gracefully. Adapt to the existing job's `IRecurringJob` contract.
6. Run `dotnet format` and `dotnet build`.

**Tests to write:**

Test file: `backend/test/Anela.Heblo.Tests/Features/FileStorage/Infrastructure/Jobs/ProductExportDownloadJobTests.cs`. Mock `IMediator`, `ITelemetryService`, `IOptions<ProductExportOptions>`.

- `Job_HasAutomaticRetryAttribute_WithZeroAttempts` — reflection: assert `typeof(ProductExportDownloadJob).GetCustomAttribute<AutomaticRetryAttribute>()?.Attempts == 0`. (This is mandated by the spec and the architecture review.)
- `Execute_OnSuccess_EmitsExactlyOneSuccessEvent` — mediator returns `Success = true`. Assert `TrackBusinessEvent` called exactly once with `Status="Success"`, `ElapsedMs` numeric, `AttemptCount` present.
- `Execute_OnHandlerFailure_EmitsFailedEvent_AndRethrows` — mediator returns `Success = false, ErrorCode = FileDownloadFailed, Params = { ["cause"]="retry-exhausted", ["attemptCount"]="4" }`. Assert: exactly one `TrackBusinessEvent("ProductExportDownload", { Status="Failed", Cause="retry-exhausted", AttemptCount="4", ErrorCode="FileDownloadFailed", ... })`, then `InvalidOperationException` thrown.
- `Execute_OnCallerCancellation_EmitsCancelledEvent_AndRethrows` — mediator throws `OperationCanceledException` and `cancellationToken.IsCancellationRequested == true`. Assert: exactly one `TrackBusinessEvent` with `Status="Cancelled"`, then `OperationCanceledException` rethrown.
- `Execute_OnSuccess_DoesNotEmitFailedEvent` — verify `TrackBusinessEvent` is invoked exactly once, never with `Status="Failed"`.

**Acceptance criteria:**

- `dotnet build` succeeds.
- Reflection test confirms `[AutomaticRetry(Attempts = 0)]` is present.
- All 5 tests pass.
- Exactly one `TrackBusinessEvent` is emitted per run in every code path.
- Failure branch rethrows; success branch does not.
- Existing job tests continue to pass (update mocks for new behaviour).
- File passes `dotnet format --verify-no-changes`.

---

### task: document-shoptet-export-url-finding

**Goal:** Append a new section to `docs/integrations/shoptet-api.md` documenting the product-export-download URL: host, observed certificate validity, observed `Content-Length`, observed latency, and any quirks discovered during FR-4 investigation.

**Context:**

Project rule §9 (Shoptet API Knowledge Base) mandates that any new finding about the Shoptet REST API MUST be documented in `docs/integrations/shoptet-api.md` before being used in code or tests. The product-export download URL is configured via `ProductExportOptions.Url` and is hit once per day by `ProductExportDownloadJob`. The architecture review (FR-4) calls for an `curl -I` reachability check from a deployed environment (staging is sufficient) to capture certificate validity, observed `Content-Length`, observed latency, and any undocumented behaviour.

The doc is the canonical Shoptet knowledge base. Findings here unblock alerting decisions (e.g. is a `503` from the export host transient or a misconfigured URL?) and prevent flaky tests caused by undocumented behaviour.

This task is independent of the code changes and may be performed in parallel with them.

**Files to create/modify:**
- `docs/integrations/shoptet-api.md` — append a new section.

**Implementation steps:**

1. From a deployed staging environment shell (or any host with network access to the production export URL), run:

```bash
curl -sS -I --max-time 30 "$PRODUCT_EXPORT_URL"
curl -sS -o /dev/null -w "time_total=%{time_total}s\nhttp_code=%{http_code}\nsize_download=%{size_download}\n" --max-time 600 "$PRODUCT_EXPORT_URL"
echo | openssl s_client -servername "$EXPORT_HOST" -connect "$EXPORT_HOST:443" 2>/dev/null | openssl x509 -noout -dates -subject
```

2. Capture: HTTP status, `Content-Type`, `Content-Length`, observed total time, certificate `notBefore` / `notAfter`, certificate subject CN.
3. Open `docs/integrations/shoptet-api.md` and append a new top-level section:

```markdown
## Product Export Download

**Endpoint:** `<host>` (full URL stored in `ProductExportOptions.Url`; do NOT paste the URL with embedded tokens here).

**Method:** `GET` (with `HEAD` probe before the GET to estimate content size — best-effort).

**Frequency:** Once per day, scheduled by `ProductExportDownloadJob` (Hangfire recurring job).

**Observed behaviour (captured <YYYY-MM-DD> from staging):**
- HTTP status: `<code>`
- `Content-Type`: `<value>`
- `Content-Length`: `<bytes>` (~`<MB>` MB)
- Wall-clock latency for full GET: `<seconds>` s
- TLS certificate: subject `<CN>`, valid `<notBefore>` → `<notAfter>`.

**Quirks / gotchas:**
- `<list any observed behaviour: redirects, missing Content-Length on streaming, 503s during peak hours, etc.>`
- HEAD support: `<supported / not supported — if not, the HEAD probe will time out and the handler falls back to size=0; this is expected.>`

**Resilience configuration:** Per-call timeout 120 s, 3 Polly retries with exponential + jitter backoff (base 2 s). Hangfire auto-retry is disabled on this job (`[AutomaticRetry(Attempts = 0)]`); retries live in Polly only. See `ProductExportOptions` for tunables.

**Related code:** `ProductExportDownloadJob`, `DownloadFromUrlHandler`, `DownloadResilienceService`, `AzureBlobStorageService.DownloadFromUrlAsync`.
```

4. Replace each `<placeholder>` with the actual values captured in step 2. If a value cannot be captured (e.g. host blocks `HEAD`), state that explicitly in the "Quirks" subsection.
5. Do NOT paste any URL containing query-string tokens or auth secrets into the doc (NFR-2).
6. Commit the doc change.

**Tests to write:**

No automated tests — this is a documentation change.

**Acceptance criteria:**

- `docs/integrations/shoptet-api.md` contains a new `## Product Export Download` section with all the listed fields filled in (no remaining `<placeholder>` markers).
- No URLs with embedded tokens or secrets appear in the doc.
- The doc references the four code components (`ProductExportDownloadJob`, `DownloadFromUrlHandler`, `DownloadResilienceService`, `AzureBlobStorageService`) so on-call engineers can navigate from the finding to the implementation.
- The "Resilience configuration" subsection accurately describes the new behaviour (120 s timeout, 3 retries, Hangfire auto-retry disabled).