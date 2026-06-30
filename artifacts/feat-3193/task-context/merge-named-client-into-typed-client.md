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