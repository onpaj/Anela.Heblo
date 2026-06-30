# SocketException / Polly Exhaustion Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop `ProductPairingDqtJob` from generating 19 duplicate SocketException / Polly-exhaustion noise events by wiring the Hangfire retry attribute, merging the named CSV HTTP client into the typed client, adding a Hangfire Activity filter for telemetry, and demoting/adding log-level calls in the right places.

**Architecture:** Four independent surgical changes touch three layers (Application job, Application service, ShoptetApi adapter, API infrastructure) with no cross-task compile dependencies. The HTTP client refactor migrates from `IHttpClientFactory.CreateClient("ShoptetStockCsv")` to the typed `HttpClient` already injected by `AddHttpClient<IEshopStockClient, ShoptetStockClient>`, which requires attaching the existing resilience pipeline to that typed registration and removing the named registration. The Activity filter is registered as a global Hangfire server filter via `GlobalJobFilters.Filters.Add`.

**Tech Stack:** .NET 8, Hangfire 1.8, Microsoft.ApplicationInsights.AspNetCore 2.22, Microsoft.Extensions.Http.Resilience, Polly, xUnit + Moq + FluentAssertions.

---

## File Map

| Action | Path |
|--------|------|
| Modify | `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs` |
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` |
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs` |
| Create | `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobActivityFilter.cs` |
| Modify | `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` |
| Modify | `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetStockClientTests.cs` |
| Modify | `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetStockClientResilienceTests.cs` |

---

### task: add-automatic-retry-attribute

Stop Hangfire from retrying `ProductPairingDqtJob` when it fails — Polly resilience inside the job already handles retry/backoff, so double-retrying amplifies the noise.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs`

- [ ] **Step 1:** Open the file. Note the current `ExecuteAsync` signature at line 42 — it has no attributes above it.

- [ ] **Step 2:** Add `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` to `ExecuteAsync`. Add the using directive for `Hangfire` at the top.

  The top of the file after edit (usings block):
  ```csharp
  using Anela.Heblo.Application.Features.DataQuality.Services;
  using Anela.Heblo.Domain.Features.BackgroundJobs;
  using Anela.Heblo.Domain.Features.DataQuality;
  using Hangfire;
  using Microsoft.Extensions.Logging;
  ```

  The method signature after edit:
  ```csharp
  [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
  public async Task ExecuteAsync(CancellationToken cancellationToken = default)
  ```

- [ ] **Step 3:** Verify the build compiles cleanly.

  ```bash
  cd /home/user/worktrees/feature-3193-socket-exception-polly/backend
  dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore -v q
  ```

  Expected: `Build succeeded.` with 0 errors.

  > **Note:** If you get `The type or namespace name 'Hangfire' could not be found`, check that the Application .csproj already references Hangfire. If not, the attribute must be placed differently — see the note below.

  > **Hangfire reference check:** Run `grep -r "Hangfire" /home/user/worktrees/feature-3193-socket-exception-polly/backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`. If there is no Hangfire reference, the `[AutomaticRetry]` attribute cannot live in the Application project. In that case, keep the attribute on the Hangfire job registration instead: in `HangfireJobRegistrationHelper.cs`, add `GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute { Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail })` as a type-specific filter, OR register via `app.UseHangfireDashboard` context. Confirm which approach the codebase uses and apply accordingly. Most likely Hangfire is already a transitive reference — the build will tell you.

- [ ] **Step 4:** Commit.

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs
  git commit -m "fix: disable Hangfire auto-retry on ProductPairingDqtJob (Polly handles retries)"
  ```

---

### task: create-hangfire-activity-filter

Create a global Hangfire server filter that opens a named `Activity` per job execution so App Insights can correlate Hangfire telemetry under the correct `operation_Name`.

**Files:**
- Create: `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobActivityFilter.cs`
- Modify: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1:** Create the filter file. The `ActivitySource` name `"Anela.Heblo.Hangfire"` must be registered with the App Insights SDK (version 2.22 already supports custom `ActivitySource` via `AddActivitySourceListener` — no extra package needed).

  Full file content:
  ```csharp
  using System.Diagnostics;
  using Hangfire.Common;
  using Hangfire.Server;

  namespace Anela.Heblo.API.Infrastructure.Hangfire;

  /// <summary>
  /// Global Hangfire server filter that starts a named <see cref="Activity"/> for each job
  /// execution so Application Insights can associate Hangfire telemetry under the correct
  /// operation_Name instead of the generic "PUT" dependency.
  /// </summary>
  public sealed class HangfireJobActivityFilter : JobFilterAttribute, IServerFilter
  {
      private static readonly ActivitySource Source = new("Anela.Heblo.Hangfire");

      public void OnPerforming(PerformingContext context)
      {
          var jobName = context.BackgroundJob.Job.Type.Name;
          var activity = Source.StartActivity($"Hangfire.Job.{jobName}", ActivityKind.Internal);
          if (activity is not null)
          {
              activity.SetTag("hangfire.job.id", context.BackgroundJob.Id);
              activity.SetTag("hangfire.job.type", context.BackgroundJob.Job.Type.FullName);
              context.Items["HangfireActivity"] = activity;
          }
      }

      public void OnPerformed(PerformedContext context)
      {
          if (context.Items.TryGetValue("HangfireActivity", out var obj) && obj is Activity activity)
          {
              if (context.Exception is not null)
                  activity.SetStatus(ActivityStatusCode.Error, context.Exception.Message);
              activity.Dispose();
          }
      }
  }
  ```

- [ ] **Step 2:** Register the filter as a global Hangfire filter in `ServiceCollectionExtensions.cs`. Open the file and find the `AddHangfireServices` method (around line 273). The filter must be added **after** `services.AddHangfire(...)` is called, using `GlobalJobFilters.Filters.Add`. Add it at the end of the `AddHangfireServices` method, just before `return services;`:

  ```csharp
  // Register global Hangfire server filter for Activity-based telemetry
  GlobalJobFilters.Filters.Add(new HangfireJobActivityFilter());
  ```

  The `using Hangfire;` directive is already present at the top of `ServiceCollectionExtensions.cs` (line 14). Add `using Anela.Heblo.API.Infrastructure.Hangfire;` if not already present — check the existing usings block (lines 1–31).

- [ ] **Step 3:** Register the ActivitySource with the App Insights SDK so it listens to the `"Anela.Heblo.Hangfire"` source. Open `ServiceCollectionExtensions.cs`, find `AddApplicationInsightsServices` (line 37). Inside the `if (!string.IsNullOrEmpty(appInsightsConnectionString))` block, after `services.AddOptimizedApplicationInsights(...)`, add:

  ```csharp
  services.Configure<Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration>(telemetryConfig =>
  {
      telemetryConfig.AddActivitySourceListener("Anela.Heblo.Hangfire");
  });
  ```

  > **Check first:** `TelemetryConfiguration.AddActivitySourceListener` was added in `Microsoft.ApplicationInsights.AspNetCore` 2.21. The csproj shows 2.22 — this is safe. If IntelliSense can't find it, check `Microsoft.ApplicationInsights` namespace — the method is on `TelemetryConfiguration`.

- [ ] **Step 4:** Build the API project.

  ```bash
  cd /home/user/worktrees/feature-3193-socket-exception-polly/backend
  dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj --no-restore -v q
  ```

  Expected: `Build succeeded.` 0 errors.

- [ ] **Step 5:** Commit.

  ```bash
  git add backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobActivityFilter.cs \
          backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
  git commit -m "feat: add HangfireJobActivityFilter for Activity-based telemetry enrichment"
  ```

---

### task: merge-named-client-into-typed-client

`ShoptetStockClient.ListAsync` currently creates an HTTP client via `_httpClientFactory.CreateClient("ShoptetStockCsv")` — a named client that does not emit App Insights dependency telemetry. This task moves the resilience pipeline onto the typed client registration (`IEshopStockClient`) and removes both `IHttpClientFactory` from the constructor and the separate named client registration.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetStockClientTests.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetStockClientResilienceTests.cs`

#### Part A — ShoptetStockClient.cs

- [ ] **Step 1:** Remove `IHttpClientFactory` from the `ShoptetStockClient` constructor and all usages. The `_http` field (the typed injected `HttpClient`) already has `BaseAddress` set by the DI registration, but `ListAsync` does not use it for the CSV call — it calls an external URL from `_stockClientOptions.Value.Url`. The URL is absolute (it is the Shoptet CSV export URL, e.g. `https://feed.myshoptet.com/csv?token=...`), so using `_http.GetAsync(url)` directly is safe because an absolute URL on an `HttpClient` with a set `BaseAddress` overrides the base address.

  Remove the `IHttpClientFactory _httpClientFactory` field and its constructor parameter. Replace the `ListAsync` method body so it uses `_http` directly.

  The updated constructor (remove `IHttpClientFactory` parameter):
  ```csharp
  public ShoptetStockClient(
      HttpClient http,
      IOptions<Orders.ShoptetApiSettings> settings,
      IOptions<ShoptetStockClientOptions> stockClientOptions,
      ILogger<ShoptetStockClient> logger)
  {
      _http = http;
      _settings = settings;
      _stockClientOptions = stockClientOptions;
      _logger = logger;
  }
  ```

  Remove the field declaration `private readonly IHttpClientFactory _httpClientFactory;`.

  Update the `using` directives — remove `Microsoft.Extensions.Http` if it was only needed for `IHttpClientFactory` (it isn't a separate package here; `IHttpClientFactory` is in `Microsoft.Extensions.Http` which is part of the framework).

  The updated `ListAsync` body — replace `var client = _httpClientFactory.CreateClient("ShoptetStockCsv");` with `var client = _http;`:

  ```csharp
  public async Task<List<EshopStock>> ListAsync(CancellationToken cancellationToken)
  {
      const string OperationName = "ShoptetStockClient.ListAsync";

      var url = _stockClientOptions.Value.Url;
      var redactedUrl = RedactToken(url);
      var client = _http;
      var stopwatch = Stopwatch.StartNew();

      try
      {
          using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
          response.EnsureSuccessStatusCode();

          using Stream csvStream = await response.Content.ReadAsStreamAsync(cancellationToken);
          using StreamReader reader = new StreamReader(csvStream, Encoding.GetEncoding("windows-1250"));
          using CsvReader csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" });
          csv.Context.RegisterClassMap<StockDataMap>();
          return csv.GetRecords<EshopStock>().ToList();
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
          throw;
      }
      catch (HttpRequestException ex)
      {
          _logger.LogError(ex,
              "Operation {Operation} failed. Url={Url} ExceptionType={ExceptionType} Message={Message} InnerExceptionType={InnerExceptionType} InnerMessage={InnerMessage} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
              OperationName,
              redactedUrl,
              ex.GetType().FullName,
              ex.Message,
              ex.InnerException?.GetType().FullName,
              ex.InnerException?.Message,
              (int?)ex.StatusCode,
              stopwatch.ElapsedMilliseconds);
          throw;
      }
      catch (Exception ex)
      {
          _logger.LogError(ex,
              "Operation {Operation} failed. Url={Url} ExceptionType={ExceptionType} Message={Message} InnerExceptionType={InnerExceptionType} InnerMessage={InnerMessage} ElapsedMs={ElapsedMs}",
              OperationName,
              redactedUrl,
              ex.GetType().FullName,
              ex.Message,
              ex.InnerException?.GetType().FullName,
              ex.InnerException?.Message,
              stopwatch.ElapsedMilliseconds);
          throw;
      }
  }
  ```

#### Part B — ShoptetApiAdapterServiceCollectionExtensions.cs

- [ ] **Step 2:** Move the resilience pipeline from the named `"ShoptetStockCsv"` client onto the typed `IEshopStockClient` / `ShoptetStockClient` registration. Replace the existing typed registration (which currently has no resilience) and remove the named client block entirely.

  Find the current typed client block (lines ~52–59):
  ```csharp
  services.AddHttpClient<IEshopStockClient, ShoptetStockClient>((sp, client) =>
  {
      var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
      client.BaseAddress = new Uri(settings.BaseUrl);
      client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
  });
  ```

  Replace it with:
  ```csharp
  services.AddHttpClient<IEshopStockClient, ShoptetStockClient>((sp, client) =>
  {
      var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
      client.BaseAddress = new Uri(settings.BaseUrl);
      client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
      // Infinite timeout — per-attempt timeout is enforced by the Polly AddTimeout below.
      client.Timeout = Timeout.InfiniteTimeSpan;
  })
  .AddResilienceHandler("shoptet-stock-csv", (builder, context) =>
  {
      var opts = context.ServiceProvider.GetRequiredService<IOptions<ShoptetStockClientOptions>>().Value;
      var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>()
          .CreateLogger("ShoptetStockCsvResilience");

      builder
          .AddRetry(new HttpRetryStrategyOptions
          {
              MaxRetryAttempts = opts.MaxRetryAttempts,
              BackoffType = DelayBackoffType.Exponential,
              UseJitter = true,
              Delay = TimeSpan.FromSeconds(opts.RetryBaseDelaySeconds),
              ShouldHandle = args =>
              {
                  if (args.Outcome.Exception is OperationCanceledException oce &&
                      oce.CancellationToken.IsCancellationRequested)
                  {
                      return new ValueTask<bool>(false);
                  }
                  return new ValueTask<bool>(HttpClientResiliencePredicates.IsTransient(args.Outcome));
              },
              OnRetry = args =>
              {
                  logger.LogWarning(
                      "Retrying {OperationName}. Attempt {AttemptNumber} of {MaxAttempts}. ExceptionType={ExceptionType} ExceptionMessage={ExceptionMessage}",
                      "ShoptetStockClient.ListAsync",
                      args.AttemptNumber + 1,
                      opts.MaxRetryAttempts,
                      args.Outcome.Exception?.GetType().Name,
                      args.Outcome.Exception?.Message);
                  return ValueTask.CompletedTask;
              }
          })
          .AddTimeout(TimeSpan.FromSeconds(opts.TimeoutSeconds));
  });
  ```

  Find and **delete** the entire named client block (the `services.AddHttpClient("ShoptetStockCsv", ...)...AddResilienceHandler(...)` chain). It starts with:
  ```csharp
  services.AddHttpClient("ShoptetStockCsv", (sp, client) =>
  ```
  and ends with the closing `});` of the resilience handler lambda. Delete that entire block.

#### Part C — Fix unit tests that passed `IHttpClientFactory` to the constructor

- [ ] **Step 3:** Update `BuildClient` in `ShoptetStockClientTests.cs`. The helper currently passes a mock `IHttpClientFactory` as the second constructor argument. Remove it.

  Find the `BuildClient` helper (line 15–27):
  ```csharp
  private static ShoptetStockClient BuildClient(
      Func<HttpRequestMessage, HttpResponseMessage> handler,
      int stockId = 1)
  {
      var http = new HttpClient(new FakeDelegatingHandler(handler))
      {
          BaseAddress = new Uri("https://fake.shoptet.cz"),
      };
      var settings = Options.Create(new ShoptetApiSettings { StockId = stockId });
      var stockClientOptions = Options.Create(new ShoptetStockClientOptions());
      var httpClientFactory = new Mock<IHttpClientFactory>().Object;
      return new ShoptetStockClient(http, httpClientFactory, settings, stockClientOptions, NullLogger<ShoptetStockClient>.Instance);
  }
  ```

  Replace with:
  ```csharp
  private static ShoptetStockClient BuildClient(
      Func<HttpRequestMessage, HttpResponseMessage> handler,
      int stockId = 1)
  {
      var http = new HttpClient(new FakeDelegatingHandler(handler))
      {
          BaseAddress = new Uri("https://fake.shoptet.cz"),
      };
      var settings = Options.Create(new ShoptetApiSettings { StockId = stockId });
      var stockClientOptions = Options.Create(new ShoptetStockClientOptions());
      return new ShoptetStockClient(http, settings, stockClientOptions, NullLogger<ShoptetStockClient>.Instance);
  }
  ```

  Also remove the `using Moq;` import if it is no longer needed after this change. Check the rest of the file — if `Mock` appears nowhere else, remove it.

- [ ] **Step 4:** Update `BuildClientForCsv` in `ShoptetStockClientTests.cs`. This helper creates a mock `IHttpClientFactory` and wires it so that `CreateClient(...)` returns a handler-backed `HttpClient`. After the refactor, `ListAsync` uses `_http` directly, so the CSV handler must be on the primary `HttpClient` passed to the constructor, not via the factory.

  Find `BuildClientForCsv` (lines 219–238):
  ```csharp
  private static ShoptetStockClient BuildClientForCsv(
      Func<HttpRequestMessage, HttpResponseMessage> handler,
      string csvUrl = "https://test.com/stock-export.csv")
  {
      var dummyHttp = new HttpClient(new FakeDelegatingHandler(_ =>
          new HttpResponseMessage(HttpStatusCode.OK)))
      {
          BaseAddress = new Uri("https://fake.shoptet.cz"),
      };

      var factoryMock = new Mock<IHttpClientFactory>();
      factoryMock
          .Setup(f => f.CreateClient(It.IsAny<string>()))
          .Returns(new HttpClient(new FakeDelegatingHandler(handler)));

      var settings = Options.Create(new ShoptetApiSettings { StockId = 1 });
      var stockClientOptions = Options.Create(new ShoptetStockClientOptions { Url = csvUrl });

      return new ShoptetStockClient(dummyHttp, factoryMock.Object, settings, stockClientOptions, NullLogger<ShoptetStockClient>.Instance);
  }
  ```

  Replace with:
  ```csharp
  private static ShoptetStockClient BuildClientForCsv(
      Func<HttpRequestMessage, HttpResponseMessage> handler,
      string csvUrl = "https://test.com/stock-export.csv")
  {
      // The primary HttpClient is used for both CSV (ListAsync) and REST calls.
      // Use the handler-backed client so ListAsync hits the stub.
      var http = new HttpClient(new FakeDelegatingHandler(handler))
      {
          BaseAddress = new Uri("https://fake.shoptet.cz"),
      };

      var settings = Options.Create(new ShoptetApiSettings { StockId = 1 });
      var stockClientOptions = Options.Create(new ShoptetStockClientOptions { Url = csvUrl });

      return new ShoptetStockClient(http, settings, stockClientOptions, NullLogger<ShoptetStockClient>.Instance);
  }
  ```

- [ ] **Step 5:** Update the resilience integration test `BuildProvider` in `ShoptetStockClientResilienceTests.cs`. This test overrides the `"ShoptetStockCsv"` named client handler. After the refactor, the resilience pipeline is on the typed client, so the override must target it instead.

  Find this block (lines 56–58):
  ```csharp
  services.AddHttpClient("ShoptetStockCsv")
      .ConfigurePrimaryHttpMessageHandler(() => new DelegatingStubHandler(handler));
  ```

  Replace with:
  ```csharp
  // Override the primary handler on the typed client so the stub is used instead of a real socket.
  services.AddHttpClient<IEshopStockClient, ShoptetStockClient>()
      .ConfigurePrimaryHttpMessageHandler(() => new DelegatingStubHandler(handler));
  ```

  > **Why this works:** `ConfigurePrimaryHttpMessageHandler` on a named/typed registration overrides the message handler for that specific client. The typed registration is keyed by `typeof(ShoptetStockClient)` in the `IHttpClientFactory` internal name. Calling `AddHttpClient<IEshopStockClient, ShoptetStockClient>()` a second time in the test DI returns the same builder for that name, so the handler override takes effect without duplicating the resilience pipeline (resilience is already chained in `AddShoptetApiAdapter`).

- [ ] **Step 6:** Run the unit tests for both test files.

  ```bash
  cd /home/user/worktrees/feature-3193-socket-exception-polly/backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShoptetStockClient" -v n
  dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ShoptetStockClient" -v n
  ```

  Expected: all tests pass. If `ListAsync_AbortsRequest_WhenPerAttemptTimeoutExceeded` is flaky (timing-sensitive), re-run once — the assertion ceiling is 10 s and per-attempt timeout is 1 s.

- [ ] **Step 7:** Run the full backend build.

  ```bash
  cd /home/user/worktrees/feature-3193-socket-exception-polly/backend
  dotnet build --no-restore -v q
  ```

  Expected: `Build succeeded.` 0 errors.

- [ ] **Step 8:** Commit.

  ```bash
  git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs \
          backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs \
          backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetStockClientTests.cs \
          backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetStockClientResilienceTests.cs
  git commit -m "fix: merge ShoptetStockCsv named client into typed client to enable App Insights dependency telemetry"
  ```

---

### task: fix-log-levels

Demote the duplicate `LogError` in `CatalogResilienceService` to `LogDebug` (the caller's `LogWarning` in `ProductPairingDqtComparer` will be the canonical signal), and add that caller-side `LogWarning`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogResilienceService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs`

#### Part A — CatalogResilienceService.cs

- [ ] **Step 1:** Find the generic catch block in `ExecuteWithResilienceAsync` (around line 47 of the file):

  ```csharp
  catch (Exception ex)
  {
      _logger.LogError(ex, "Failed to execute {OperationName} after all retry attempts", operationName);
      throw;
  }
  ```

  Change `LogError` to `LogDebug`:

  ```csharp
  catch (Exception ex)
  {
      _logger.LogDebug(ex, "Failed to execute {OperationName} after all retry attempts", operationName);
      throw;
  }
  ```

  Leave the `BrokenCircuitException` catch block with `LogWarning` unchanged.

#### Part B — ProductPairingDqtComparer.cs

- [ ] **Step 2:** `CompareAsync` currently calls `_resilienceService.ExecuteWithResilienceAsync` for both eshop and ERP fetches without any catch block. If resilience exhausts retries, the exception propagates silently from the comparer's perspective. Add a `try/catch` around the eshop call to log a structured `LogWarning` with context before rethrowing. Do the same for the ERP call.

  The current eshop call (lines 25–28 of the file):
  ```csharp
  var eshopProducts = await _resilienceService.ExecuteWithResilienceAsync(
      async cancellationToken => await _eshopStockClient.ListAsync(cancellationToken),
      "ProductPairingDqtComparer.EshopList",
      ct);
  ```

  The current ERP call (lines 30–33):
  ```csharp
  var erpProducts = await _resilienceService.ExecuteWithResilienceAsync(
      async cancellationToken => await _erpStockClient.ListAsync(cancellationToken),
      "ProductPairingDqtComparer.ErpList",
      ct);
  ```

  The class has no `ILogger` injected. Add it to the constructor. The updated constructor and field declarations:

  ```csharp
  using Anela.Heblo.Application.Features.Catalog.Infrastructure;
  using Anela.Heblo.Domain.Features.Catalog;
  using Anela.Heblo.Domain.Features.Catalog.Stock;
  using Anela.Heblo.Domain.Features.DataQuality;
  using Microsoft.Extensions.Logging;

  namespace Anela.Heblo.Application.Features.DataQuality.Services;

  public class ProductPairingDqtComparer : IDriftDqtComparer
  {
      private readonly IEshopStockClient _eshopStockClient;
      private readonly IErpStockClient _erpStockClient;
      private readonly ICatalogResilienceService _resilienceService;
      private readonly ILogger<ProductPairingDqtComparer> _logger;

      public DqtTestType TestType => DqtTestType.ProductPairing;

      public ProductPairingDqtComparer(
          IEshopStockClient eshopStockClient,
          IErpStockClient erpStockClient,
          ICatalogResilienceService resilienceService,
          ILogger<ProductPairingDqtComparer> logger)
      {
          _eshopStockClient = eshopStockClient;
          _erpStockClient = erpStockClient;
          _resilienceService = resilienceService;
          _logger = logger;
      }
  ```

  Update the two resilience calls in `CompareAsync` to catch and re-log:

  ```csharp
  List<EshopStock> eshopProducts;
  try
  {
      eshopProducts = await _resilienceService.ExecuteWithResilienceAsync(
          async cancellationToken => await _eshopStockClient.ListAsync(cancellationToken),
          "ProductPairingDqtComparer.EshopList",
          ct);
  }
  catch (Exception ex)
  {
      _logger.LogWarning(ex,
          "ProductPairingDqtComparer failed to fetch eshop products after resilience exhaustion. Operation={Operation} ExceptionType={ExceptionType}",
          "ProductPairingDqtComparer.EshopList",
          ex.GetType().Name);
      throw;
  }

  List<ErpStock> erpProducts;
  try
  {
      erpProducts = await _resilienceService.ExecuteWithResilienceAsync(
          async cancellationToken => await _erpStockClient.ListAsync(cancellationToken),
          "ProductPairingDqtComparer.ErpList",
          ct);
  }
  catch (Exception ex)
  {
      _logger.LogWarning(ex,
          "ProductPairingDqtComparer failed to fetch ERP products after resilience exhaustion. Operation={Operation} ExceptionType={ExceptionType}",
          "ProductPairingDqtComparer.ErpList",
          ex.GetType().Name);
      throw;
  }
  ```

  > **Note on `List<ErpStock>` type:** In the original file, `erpProducts` is `var erpProducts = await ...`. After wrapping in a try/catch, you must declare the variable before the try block. The return type of `_erpStockClient.ListAsync` is `List<ErpStock>` — verify by checking `IErpStockClient`. If it differs, use the correct type.

- [ ] **Step 3:** Build to confirm compilation.

  ```bash
  cd /home/user/worktrees/feature-3193-socket-exception-polly/backend
  dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore -v q
  ```

  Expected: `Build succeeded.` 0 errors.

- [ ] **Step 4:** Commit.

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogResilienceService.cs \
          backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs
  git commit -m "fix: demote CatalogResilienceService generic catch to LogDebug; add LogWarning in ProductPairingDqtComparer"
  ```

---

### task: final-build-and-format

Verify the complete solution builds and passes dotnet format before opening the PR.

**Files:** (no new files)

- [ ] **Step 1:** Run the full solution build.

  ```bash
  cd /home/user/worktrees/feature-3193-socket-exception-polly/backend
  dotnet build --no-restore -v q
  ```

  Expected: `Build succeeded.` 0 errors, 0 warnings (or only pre-existing warnings).

- [ ] **Step 2:** Run dotnet format to check for style violations.

  ```bash
  cd /home/user/worktrees/feature-3193-socket-exception-polly/backend
  dotnet format --verify-no-changes --verbosity diagnostic
  ```

  If format reports diffs, run `dotnet format` without `--verify-no-changes` to apply them, then re-run with it to confirm clean.

- [ ] **Step 3:** Run the unit test suite for all touched test projects.

  ```bash
  cd /home/user/worktrees/feature-3193-socket-exception-polly/backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v n
  dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj -v n
  ```

  Expected: all tests pass.

- [ ] **Step 4:** Commit any format-only changes (if any).

  ```bash
  git add -u
  git commit -m "style: apply dotnet format"
  ```

  Skip this step if Step 2 produced no changes.
