# ShoptetStockClient.ListAsync Resilience Hardening — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden `ShoptetStockClient.ListAsync` against transient HTTP failures by adding a configured named `HttpClient` with explicit timeout, HTTP-layer retry policy via `Microsoft.Extensions.Http.Resilience`, structured terminal-failure logging with token redaction, and consistent caller-side resilience wrapping in `ProductPairingDqtComparer`.

**Architecture:** A new **named** `HttpClient` (`"ShoptetStockCsv"`) is registered in `ShoptetApiAdapterServiceCollectionExtensions` with `Timeout` and an attached `AddResilienceHandler(...)` providing exponential-with-jitter retry and per-attempt timeout. `ShoptetStockClient.ListAsync` resolves this named client via `IHttpClientFactory`, wraps the call in a `try/catch` that emits structured `ILogger` records with redacted URL, status code, inner exception details and elapsed milliseconds on terminal failure. `ProductPairingDqtComparer` gains an `ICatalogResilienceService` dependency and wraps both stock-list calls so DQT failures match the refresh-path resilience semantics.

**Tech Stack:** .NET 8, C#, `Microsoft.Extensions.Http.Resilience` 8.x (transitively Polly 8.4.1+), xUnit, FluentAssertions, Moq + `Moq.Protected` for `HttpMessageHandler` stubbing, `CsvHelper` (unchanged).

---

## File Structure

**Created:**
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetStockClientResilienceTests.cs` — DelegatingHandler-driven unit tests for retry, timeout, terminal logging, and URL redaction.

**Modified:**
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj` — add `Microsoft.Extensions.Http.Resilience` package reference.
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClientOptions.cs` — add `TimeoutSeconds`, `MaxRetryAttempts`, `RetryBaseDelaySeconds` properties.
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` — register named `"ShoptetStockCsv"` HttpClient with timeout and `AddResilienceHandler(...)`.
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs` — rewrite `ListAsync` to resolve the named client, wrap in stopwatch+try/catch, emit structured terminal log with redacted URL; add `static RedactToken(...)` helper.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs` — inject `ICatalogResilienceService`, wrap both `ListAsync` calls.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs` — add `Mock<ICatalogResilienceService>` whose `ExecuteWithResilienceAsync` passes through to the operation; update `CreateSut()`.
- `docs/integrations/shoptet-api.md` — new subsection under §4 documenting CSV endpoint, retry policy, and observability notes.

---

## Task 1: Extend `ShoptetStockClientOptions` with timeout and retry properties

Lays down the configuration surface that all later tasks depend on. Defaults reflect Amendment 1 from the architecture review (per-attempt timeout fits inside the outer `CatalogResilienceService` 30-second budget when combined with 3 retries).

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClientOptions.cs`

- [ ] **Step 1: Replace the file with the extended options class**

```csharp
namespace Anela.Heblo.Adapters.ShoptetApi.Stock;

public class ShoptetStockClientOptions
{
    public const string SettingsKey = "StockClient";

    public string Url { get; set; } = "http://";

    public int TimeoutSeconds { get; set; } = 8;

    public int MaxRetryAttempts { get; set; } = 3;

    public int RetryBaseDelaySeconds { get; set; } = 1;
}
```

- [ ] **Step 2: Build to verify the change compiles**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClientOptions.cs
git commit -m "feat(shoptet-stock-client): add timeout and retry options"
```

---

## Task 2: Add `Microsoft.Extensions.Http.Resilience` package reference

Resilience handler is a new dependency for this adapter. Pin to a version whose transitive Polly is >= 8.4.1 (matches what `CatalogResilienceService` uses).

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj`

- [ ] **Step 1: Add the package reference to the existing `<ItemGroup>` that lists packages**

Locate the first `<ItemGroup>` containing `<PackageReference ...>` entries and add this line just below the `Microsoft.Extensions.Http` reference:

```xml
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="8.10.0" />
```

After the edit, the relevant `<ItemGroup>` looks like:

```xml
  <ItemGroup>
    <PackageReference Include="CsvHelper" Version="32.0.2" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="8.10.0" />
    <PackageReference Include="System.Text.Encoding.CodePages" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
    <PackageReference Include="QuestPDF" Version="2024.3.4" />
    <PackageReference Include="SkiaSharp.NativeAssets.Linux" Version="2.88.3" />
    <PackageReference Include="ZXing.Net.Bindings.SkiaSharp" Version="0.16.13" />
  </ItemGroup>
```

- [ ] **Step 2: Restore and verify transitive Polly version is compatible**

Run: `dotnet restore backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj`
Expected: Restore succeeds.

Run: `dotnet list backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj package --include-transitive | grep -i polly`
Expected: One or more `Polly` / `Polly.Core` lines reporting versions `>= 8.4.1`. If a lower version is reported, raise the `Microsoft.Extensions.Http.Resilience` version (e.g. to `8.11.0`) until Polly is `>= 8.4.1`.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
git commit -m "chore(shoptet): add Microsoft.Extensions.Http.Resilience package"
```

---

## Task 3: Register the named `"ShoptetStockCsv"` HttpClient with timeout and resilience handler

This puts retry, jittered exponential backoff, and per-attempt timeout on the HTTP transport so any code that resolves `"ShoptetStockCsv"` benefits automatically. The retry predicate explicitly does not retry when the **caller's** cancellation was requested (Amendment 2).

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs`

- [ ] **Step 1: Add the new `using` directives at the top of the file**

Add these lines to the existing using block:

```csharp
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
```

- [ ] **Step 2: Insert the named HttpClient registration immediately after the existing `services.Configure<ShoptetStockClientOptions>(...)` call**

Locate the call at line 73-74 in `ShoptetApiAdapterServiceCollectionExtensions.cs`:

```csharp
        services.Configure<ShoptetStockClientOptions>(
            configuration.GetSection(ShoptetStockClientOptions.SettingsKey));
```

Insert the following block **immediately after** it:

```csharp
        services.AddHttpClient("ShoptetStockCsv", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<ShoptetStockClientOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds * opts.MaxRetryAttempts + 5);
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
                        // Do not retry when the caller's token requested cancellation.
                        if (args.Outcome.Exception is OperationCanceledException &&
                            context.CancellationToken.IsCancellationRequested)
                        {
                            return new ValueTask<bool>(false);
                        }
                        return HttpClientResiliencePredicates.IsTransient(args.Outcome);
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

**Why the outer `client.Timeout` is `TimeoutSeconds * MaxRetryAttempts + 5`:** `HttpClient.Timeout` is enforced across the full pipeline including retries. If it were set to just `TimeoutSeconds`, the outer timeout would fire before any retry could run. The per-attempt timeout inside `AddTimeout(...)` is what bounds each individual request.

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs
git commit -m "feat(shoptet-stock-client): register named HttpClient with resilience handler"
```

---

## Task 4: Rewrite `ShoptetStockClient.ListAsync` to use the named client + structured logging

`ListAsync` switches from `_httpClientFactory.CreateClient()` (default unnamed client) to `_httpClientFactory.CreateClient("ShoptetStockCsv")`. A `Stopwatch` measures wall-clock; a `try/catch` captures terminal failures and logs structured fields with the URL redacted. The exception is rethrown.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs`

- [ ] **Step 1: Add the missing `using System.Diagnostics;` at the top of the file**

Add to the existing using block:

```csharp
using System.Diagnostics;
```

- [ ] **Step 2: Replace the body of `ListAsync` (currently lines 41-58) with the resilient + instrumented version**

Replace the entire existing method:

```csharp
    public async Task<List<EshopStock>> ListAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        List<EshopStock> stockDataList = new List<EshopStock>();
        using (HttpResponseMessage response = await client.GetAsync(_stockClientOptions.Value.Url, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            using (Stream csvStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            using (StreamReader reader = new StreamReader(csvStream, Encoding.GetEncoding("windows-1250")))
            using (CsvReader csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" }))
            {
                csv.Context.RegisterClassMap<StockDataMap>();
                stockDataList = csv.GetRecords<EshopStock>().ToList();
            }
        }

        return stockDataList;
    }
```

With:

```csharp
    public async Task<List<EshopStock>> ListAsync(CancellationToken cancellationToken)
    {
        const string OperationName = "ShoptetStockClient.ListAsync";

        var url = _stockClientOptions.Value.Url;
        var redactedUrl = RedactToken(url);
        var client = _httpClientFactory.CreateClient("ShoptetStockCsv");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Operation {Operation} failed. Url={Url} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                    OperationName, redactedUrl, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
                response.EnsureSuccessStatusCode();
            }

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

    internal static string RedactToken(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return url;
        }

        var queryIndex = url.IndexOf('?');
        if (queryIndex < 0)
        {
            return url;
        }

        var prefix = url.Substring(0, queryIndex + 1);
        var query = url.Substring(queryIndex + 1);
        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < pairs.Length; i++)
        {
            var equalsIndex = pairs[i].IndexOf('=');
            var key = equalsIndex < 0 ? pairs[i] : pairs[i].Substring(0, equalsIndex);
            if (key.Equals("token", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("hash", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("key", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("apiToken", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("access_token", StringComparison.OrdinalIgnoreCase))
            {
                pairs[i] = key + "=***";
            }
        }

        return prefix + string.Join('&', pairs);
    }
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Stock/ShoptetStockClient.cs
git commit -m "feat(shoptet-stock-client): use named client + structured terminal logging"
```

---

## Task 5: Write a unit test that the named client retries on transient 503

This is the first of several TDD-style unit tests for the resilience behavior. We start the new test file here and prove the retry policy works end-to-end through DI.

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetStockClientResilienceTests.cs`

- [ ] **Step 1: Write the new test file with the first failing test**

```csharp
using System.Net;
using System.Text;
using Anela.Heblo.Adapters.ShoptetApi;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Stock;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class ShoptetStockClientResilienceTests
{
    private const string CsvSample =
        "CODE;PAIR;NAME;IMG;IMG2;X;X;X;X;X;X;X;X;X;X;NS;X;X;X;X;X;X;X;X;X;STOCK;LOC;W;H;D;WD;AT\n" +
        "P001;;Product 1;;;;;;;;;;;;;;;;;;;;;;;5;A1;100;10;10;10;0\n";

    private static IServiceProvider BuildProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        Action<ShoptetStockClientOptions>? configureOptions = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var services = new ServiceCollection();
        services.AddLogging();

        var configBuilder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ShoptetApi:BaseUrl"] = "https://api.myshoptet.com/",
            ["ShoptetApi:ApiToken"] = "test-token",
            ["ShoptetApi:StockId"] = "1",
            ["StockClient:Url"] = "https://csv.example.com/export?token=secret-token-123",
            ["StockClient:TimeoutSeconds"] = "1",
            ["StockClient:MaxRetryAttempts"] = "3",
            ["StockClient:RetryBaseDelaySeconds"] = "0"
        });
        var configuration = configBuilder.Build();

        services.AddShoptetApiAdapter(configuration);
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        // Override the SocketsHttpHandler for the named client with a stub.
        services.ConfigureHttpClientDefaults(b => { });
        services.AddHttpClient("ShoptetStockCsv")
            .ConfigurePrimaryHttpMessageHandler(() => new DelegatingStubHandler(handler));

        return services.BuildServiceProvider();
    }

    private static HttpResponseMessage CsvOk() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(CsvSample, Encoding.GetEncoding("windows-1250"))
    };

    private static HttpResponseMessage TransientFailure() => new(HttpStatusCode.ServiceUnavailable)
    {
        Content = new StringContent("transient")
    };

    [Fact]
    public async Task ListAsync_RetriesOnTransient503_AndSucceedsOnThirdAttempt()
    {
        // Arrange
        var calls = 0;
        var provider = BuildProvider((req, ct) =>
        {
            calls++;
            return Task.FromResult(calls < 3 ? TransientFailure() : CsvOk());
        });
        var client = provider.GetRequiredService<IEshopStockClient>();

        // Act
        var result = await client.ListAsync(CancellationToken.None);

        // Assert
        calls.Should().Be(3);
        result.Should().HaveCount(1);
        result[0].Code.Should().Be("P001");
    }

    private sealed class DelegatingStubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public DelegatingStubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
```

- [ ] **Step 2: Run the test to confirm it passes**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ShoptetStockClientResilienceTests"`
Expected: 1 passed, 0 failed.

If this test does not pass — typical reasons:
- The `"ShoptetStockCsv"` registration in Task 3 did not pick up `MaxRetryAttempts` from configuration. Verify the in-memory config is bound. Inspect the actual call count.
- `IsTransient(...)` does not classify 503 as transient. Confirm the predicate by reading `HttpClientResiliencePredicates.IsTransient` reference.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetStockClientResilienceTests.cs
git commit -m "test(shoptet-stock-client): retry succeeds after two transient 503 responses"
```

---

## Task 6: Add a test that 4 consecutive 503s surface as `HttpRequestException`

Proves the retry budget is bounded.

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetStockClientResilienceTests.cs`

- [ ] **Step 1: Add the new test method just above the `DelegatingStubHandler` private class**

```csharp
    [Fact]
    public async Task ListAsync_ExhaustsRetries_AndThrowsHttpRequestException()
    {
        // Arrange
        var calls = 0;
        var provider = BuildProvider((req, ct) =>
        {
            calls++;
            return Task.FromResult(TransientFailure());
        });
        var client = provider.GetRequiredService<IEshopStockClient>();

        // Act
        Func<Task> act = async () => await client.ListAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        // 1 initial attempt + 3 retries = 4 total
        calls.Should().Be(4);
    }
```

- [ ] **Step 2: Run the test**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ListAsync_ExhaustsRetries"`
Expected: PASS, exactly 4 calls.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetStockClientResilienceTests.cs
git commit -m "test(shoptet-stock-client): exhausted retries surface as HttpRequestException"
```

---

## Task 7: Add a test that caller cancellation short-circuits retries

Mirrors the cancellation-aware predicate from Amendment 2.

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetStockClientResilienceTests.cs`

- [ ] **Step 1: Add the new test method just above `DelegatingStubHandler`**

```csharp
    [Fact]
    public async Task ListAsync_WhenCallerCancels_DoesNotRetry()
    {
        // Arrange
        var calls = 0;
        using var cts = new CancellationTokenSource();
        var provider = BuildProvider(async (req, ct) =>
        {
            calls++;
            cts.Cancel(); // simulate the caller cancelling between transport attempts
            await Task.Delay(20, ct); // throws OperationCanceledException
            return CsvOk();
        });
        var client = provider.GetRequiredService<IEshopStockClient>();

        // Act
        Func<Task> act = async () => await client.ListAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(1, "caller cancellation must not trigger retries");
    }
```

- [ ] **Step 2: Run the test**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~WhenCallerCancels"`
Expected: PASS, exactly 1 transport call.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetStockClientResilienceTests.cs
git commit -m "test(shoptet-stock-client): caller cancellation short-circuits retries"
```

---

## Task 8: Add a test that the redacted URL never contains the raw token in logs

Wires an `InMemoryLogger` (xUnit-friendly capture) into the DI container and verifies no log scope contains the raw token after a terminal failure.

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetStockClientResilienceTests.cs`

- [ ] **Step 1: Add a capturing logger provider and a test**

Add this nested type (just above `DelegatingStubHandler`):

```csharp
    private sealed class CapturingLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider
    {
        public readonly List<string> Lines = new();

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new CapturingLogger(Lines);
        public void Dispose() { }

        private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
        {
            private readonly List<string> _sink;
            public CapturingLogger(List<string> sink) => _sink = sink;
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
            public void Log<TState>(
                Microsoft.Extensions.Logging.LogLevel logLevel,
                Microsoft.Extensions.Logging.EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _sink.Add(formatter(state, exception));
            }
            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }
```

Then update `BuildProvider` to accept and register the capturing provider. Replace the existing `BuildProvider` method with:

```csharp
    private static IServiceProvider BuildProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        Action<ShoptetStockClientOptions>? configureOptions = null,
        CapturingLoggerProvider? logCapture = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            if (logCapture is not null) b.AddProvider(logCapture);
        });

        var configBuilder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ShoptetApi:BaseUrl"] = "https://api.myshoptet.com/",
            ["ShoptetApi:ApiToken"] = "test-token",
            ["ShoptetApi:StockId"] = "1",
            ["StockClient:Url"] = "https://csv.example.com/export?token=secret-token-123",
            ["StockClient:TimeoutSeconds"] = "1",
            ["StockClient:MaxRetryAttempts"] = "3",
            ["StockClient:RetryBaseDelaySeconds"] = "0"
        });
        var configuration = configBuilder.Build();

        services.AddShoptetApiAdapter(configuration);
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        services.AddHttpClient("ShoptetStockCsv")
            .ConfigurePrimaryHttpMessageHandler(() => new DelegatingStubHandler(handler));

        return services.BuildServiceProvider();
    }
```

Now add the test (just above `DelegatingStubHandler`):

```csharp
    [Fact]
    public async Task ListAsync_OnTerminalFailure_LogsRedactedUrl_AndStructuredFields()
    {
        // Arrange
        var capture = new CapturingLoggerProvider();
        var provider = BuildProvider(
            (req, ct) => Task.FromResult(TransientFailure()),
            logCapture: capture);
        var client = provider.GetRequiredService<IEshopStockClient>();

        // Act
        try { await client.ListAsync(CancellationToken.None); } catch (HttpRequestException) { /* expected */ }

        // Assert
        var terminalLogs = capture.Lines.Where(l => l.Contains("ShoptetStockClient.ListAsync")).ToList();
        terminalLogs.Should().NotBeEmpty();
        terminalLogs.Should().Contain(l => l.Contains("token=***"),
            "terminal log line must include redacted URL");
        capture.Lines.Should().NotContain(l => l.Contains("secret-token-123"),
            "raw token must never appear in any log line");
    }
```

- [ ] **Step 2: Run the test**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~LogsRedactedUrl"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetStockClientResilienceTests.cs
git commit -m "test(shoptet-stock-client): terminal log redacts token and includes structured fields"
```

---

## Task 9: Add a test that the configured per-attempt timeout aborts a slow request

Validates FR-2 (per-attempt timeout) end-to-end through the resilience handler.

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetStockClientResilienceTests.cs`

- [ ] **Step 1: Add the timeout test (just above `DelegatingStubHandler`)**

```csharp
    [Fact]
    public async Task ListAsync_AbortsRequest_WhenPerAttemptTimeoutExceeded()
    {
        // Arrange — per-attempt timeout = 1s (from BuildProvider config); handler sleeps 5s.
        var calls = 0;
        var provider = BuildProvider(async (req, ct) =>
        {
            calls++;
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return CsvOk();
        });
        var client = provider.GetRequiredService<IEshopStockClient>();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        Func<Task> act = async () => await client.ListAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<Exception>(); // either HttpRequestException or TimeoutRejectedException
        stopwatch.Stop();
        // 1 initial attempt @ 1s + 3 retries @ 1s + backoff (0s) = ~4s — must complete in < 8s.
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8));
        calls.Should().BeGreaterThan(1, "timeouts should be classified transient and retried");
    }
```

- [ ] **Step 2: Run the test**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~AbortsRequest"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetStockClientResilienceTests.cs
git commit -m "test(shoptet-stock-client): per-attempt timeout aborts slow request"
```

---

## Task 10: Add unit tests for the `RedactToken` helper

Cover the helper directly so future edits keep token redaction safe.

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetStockClientResilienceTests.cs`

- [ ] **Step 1: Add a `RedactTokenTests` nested test class to the same file**

Add at the end of `ShoptetStockClientResilienceTests` (just before the closing brace of the outer class):

```csharp
    public class RedactTokenTests
    {
        [Theory]
        [InlineData("https://csv.example.com/export?token=abc",                    "https://csv.example.com/export?token=***")]
        [InlineData("https://csv.example.com/export?other=1&token=abc&x=y",        "https://csv.example.com/export?other=1&token=***&x=y")]
        [InlineData("https://csv.example.com/export?hash=zzz",                     "https://csv.example.com/export?hash=***")]
        [InlineData("https://csv.example.com/export?TOKEN=upper",                  "https://csv.example.com/export?TOKEN=***")]
        [InlineData("https://csv.example.com/export",                              "https://csv.example.com/export")]
        [InlineData("",                                                            "")]
        public void RedactToken_ReplacesSensitiveQueryValues(string input, string expected)
        {
            var actual = typeof(ShoptetStockClient)
                .GetMethod("RedactToken", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(null, new object[] { input }) as string;

            actual.Should().Be(expected);
        }
    }
```

Note: this uses reflection because `RedactToken` is `internal static`. If `InternalsVisibleTo` is preferred, add `[assembly: InternalsVisibleTo("Anela.Heblo.Adapters.Shoptet.Tests")]` to the adapter project and call the helper directly — but reflection avoids a wider surface-area change.

- [ ] **Step 2: Run the redact tests**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~RedactToken"`
Expected: 6 passed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetStockClientResilienceTests.cs
git commit -m "test(shoptet-stock-client): RedactToken helper covers sensitive query keys"
```

---

## Task 11: Inject `ICatalogResilienceService` into `ProductPairingDqtComparer`

Brings DQT's stock-list calls under the same circuit-breaker + timeout outer pipeline used by `CatalogDataRefreshService.RefreshEshopStockData`. Operation names match Amendment 5.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs`

- [ ] **Step 1: Replace the constructor and `CompareAsync` to add the new dependency and wrap both list calls**

Replace the existing class body (lines 7-93) — preserving file-level usings — with:

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class ProductPairingDqtComparer : IDriftDqtComparer
{
    private readonly IEshopStockClient _eshopStockClient;
    private readonly IErpStockClient _erpStockClient;
    private readonly ICatalogResilienceService _resilienceService;

    public DqtTestType TestType => DqtTestType.ProductPairing;

    public ProductPairingDqtComparer(
        IEshopStockClient eshopStockClient,
        IErpStockClient erpStockClient,
        ICatalogResilienceService resilienceService)
    {
        _eshopStockClient = eshopStockClient;
        _erpStockClient = erpStockClient;
        _resilienceService = resilienceService;
    }

    public async Task<DriftComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // Date range is intentionally unused — product pairing is a current-state snapshot
        var eshopProducts = await _resilienceService.ExecuteWithResilienceAsync(
            async cancellationToken => await _eshopStockClient.ListAsync(cancellationToken),
            "ProductPairingDqtComparer.EshopList",
            ct);

        var erpProducts = await _resilienceService.ExecuteWithResilienceAsync(
            async cancellationToken => await _erpStockClient.ListAsync(cancellationToken),
            "ProductPairingDqtComparer.ErpList",
            ct);

        var sellableErpProducts = erpProducts.Where(IsSellable).ToList();

        var erpCodeSet = sellableErpProducts
            .Select(p => p.ProductCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // All Shoptet identifiers (Code + PairCode) used when checking ERP → Shoptet direction
        var shoptetIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in eshopProducts)
        {
            shoptetIdentifiers.Add(p.Code);
            if (!string.IsNullOrWhiteSpace(p.PairCode))
                shoptetIdentifiers.Add(p.PairCode);
        }

        var mismatches = new List<DriftMismatch>();

        // Check A: each Shoptet product must resolve to an ERP code
        foreach (var eshopProduct in eshopProducts)
        {
            var hasPairCode = !string.IsNullOrWhiteSpace(eshopProduct.PairCode);
            var resolvedCode = hasPairCode ? eshopProduct.PairCode : eshopProduct.Code;

            if (erpCodeSet.Contains(resolvedCode))
                continue;

            var mismatch = ProductPairingMismatch.MissingInErp;
            if (hasPairCode)
                mismatch |= ProductPairingMismatch.PairCodeUnresolved;

            mismatches.Add(new DriftMismatch
            {
                EntityKey = eshopProduct.Code,
                MismatchCode = (int)mismatch,
                ShoptetValue = eshopProduct.Name,
                HebloValue = null,
                Details = hasPairCode
                    ? $"Shoptet product '{eshopProduct.Code}' PairCode '{eshopProduct.PairCode}' not found in ERP"
                    : $"Shoptet product '{eshopProduct.Code}' not found in ERP"
            });
        }

        // Check B: each sellable ERP product must appear in Shoptet
        foreach (var erpProduct in sellableErpProducts)
        {
            if (shoptetIdentifiers.Contains(erpProduct.ProductCode))
                continue;

            mismatches.Add(new DriftMismatch
            {
                EntityKey = erpProduct.ProductCode,
                MismatchCode = (int)ProductPairingMismatch.MissingInShoptet,
                HebloValue = erpProduct.ProductName,
                ShoptetValue = null,
                Details = $"Sellable ERP product '{erpProduct.ProductCode}' not in Shoptet catalog"
            });
        }

        var totalChecked = shoptetIdentifiers
            .Union(erpCodeSet, StringComparer.OrdinalIgnoreCase)
            .Count();

        return new DriftComparisonResult { Mismatches = mismatches, TotalChecked = totalChecked };
    }

    private static bool IsSellable(ErpStock product) =>
        product.ProductTypeId == (int)ProductType.Goods ||
        product.ProductTypeId == (int)ProductType.Product;
}
```

- [ ] **Step 2: Build the Application project**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Services/ProductPairingDqtComparer.cs
git commit -m "feat(dqt): wrap ProductPairingDqtComparer stock calls with resilience"
```

---

## Task 12: Update `ProductPairingDqtComparerTests` to inject the resilience mock

The existing tests construct the SUT directly with two stock clients. Add an `ICatalogResilienceService` mock whose `ExecuteWithResilienceAsync` delegates to the wrapped operation.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs`

- [ ] **Step 1: Add the new using directive and update `CreateSut()` plus the mock fields**

Replace lines 1-16 of the existing file:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class ProductPairingDqtComparerTests
{
    private readonly Mock<IEshopStockClient> _eshopMock = new();
    private readonly Mock<IErpStockClient> _erpMock = new();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private ProductPairingDqtComparer CreateSut() =>
        new(_eshopMock.Object, _erpMock.Object);
```

With:

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class ProductPairingDqtComparerTests
{
    private readonly Mock<IEshopStockClient> _eshopMock = new();
    private readonly Mock<IErpStockClient> _erpMock = new();
    private readonly Mock<ICatalogResilienceService> _resilienceMock = new();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    public ProductPairingDqtComparerTests()
    {
        // Pass-through resilience: invoke the inner operation directly.
        _resilienceMock
            .Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<List<EshopStock>>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<List<EshopStock>>>, string, CancellationToken>(
                (op, _, ct) => op(ct));

        _resilienceMock
            .Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<IReadOnlyList<ErpStock>>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IReadOnlyList<ErpStock>>>, string, CancellationToken>(
                (op, _, ct) => op(ct));
    }

    private ProductPairingDqtComparer CreateSut() =>
        new(_eshopMock.Object, _erpMock.Object, _resilienceMock.Object);
```

- [ ] **Step 2: Run the affected tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ProductPairingDqtComparerTests"`
Expected: All 4 existing tests pass.

- [ ] **Step 3: Add a test that asserts the resilience wrap was invoked with the expected operation names**

Insert just before the closing brace of the class:

```csharp
    [Fact]
    public async Task CompareAsync_WrapsBothListCalls_WithResilience()
    {
        // Arrange
        SetupEshop(new EshopStock { Code = "P001", PairCode = "", Name = "Product 1" });
        SetupErp(new ErpStock { ProductCode = "P001", ProductName = "Product 1", ProductTypeId = 1 });

        // Act
        _ = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        _resilienceMock.Verify(r => r.ExecuteWithResilienceAsync(
            It.IsAny<Func<CancellationToken, Task<List<EshopStock>>>>(),
            "ProductPairingDqtComparer.EshopList",
            It.IsAny<CancellationToken>()), Times.Once);

        _resilienceMock.Verify(r => r.ExecuteWithResilienceAsync(
            It.IsAny<Func<CancellationToken, Task<IReadOnlyList<ErpStock>>>>(),
            "ProductPairingDqtComparer.ErpList",
            It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 4: Run the new test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~WrapsBothListCalls"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtComparerTests.cs
git commit -m "test(dqt): ProductPairingDqtComparer uses ICatalogResilienceService"
```

---

## Task 13: Document the CSV endpoint and retry behavior in `shoptet-api.md`

Per the project rule "Shoptet API findings must be documented before use."

**Files:**
- Modify: `docs/integrations/shoptet-api.md`

- [ ] **Step 1: Insert a new subsection `4.4 Stock CSV export — resilience characteristics` immediately after section `4.3 Getting all variant codes efficiently`**

Locate the lines around 305-309:

```markdown
### 4.3 Getting all variant codes efficiently

**Do NOT use N+1 product detail calls.** Use the stock CSV export instead — it lists all variant codes in a single request and is already parsed by `IEshopStockClient.ListAsync()`. The `EshopStock.Code` field is the variant-level SKU accepted by `POST /api/orders`.

The snapshot endpoint (`GET /api/products/snapshot`) exists but requires a registered webhook for `job:finished` — not usable without webhook infrastructure.
```

Immediately after that block (before the `---` separator), add:

```markdown
### 4.4 Stock CSV export — resilience characteristics

The stock CSV export URL is configured via `StockClient:Url` and **is not** on `api.myshoptet.com` — it is the per-store CSV export host (e.g. `https://<store>.myshoptet.com/action/...`). Two consequences:

- The dependency tracker (which targets `api.myshoptet.com`) does **not** record these calls. Failures must be queried by exception name (`System.Net.Http.HttpRequestException` with `outerMethod contains "ShoptetStockClient.ListAsync"`).
- The URL contains an access token as a query parameter (e.g. `?token=...` / `?hash=...`). Logging code redacts `token`, `hash`, `key`, `apiToken`, `access_token` keys to `***`.

**Encoding:** `windows-1250`. **Delimiter:** `;`. Parsed via `CsvHelper` with `StockDataMap`.

**Observed transient failure rate (baseline):** ~1.1 `HttpRequestException` / day across all callers (telemetry window 2026-06-05 → 2026-06-12).

**Resilience policy (HTTP layer, registered against the named HttpClient `"ShoptetStockCsv"`):**

| Property | Value | Configurable via |
|---|---|---|
| Per-attempt timeout | 8s (default) | `StockClient:TimeoutSeconds` |
| Max retry attempts | 3 (default) | `StockClient:MaxRetryAttempts` |
| Retry base delay | 1s exponential + jitter | `StockClient:RetryBaseDelaySeconds` |
| Retry triggers | `HttpRequestException`, 5xx, 408, 429, `TimeoutRejectedException`, `OperationCanceledException` (only when caller's token has **not** requested cancellation) | — |
| Outer `HttpClient.Timeout` | `TimeoutSeconds × MaxRetryAttempts + 5` | derived |

Worst-case wall clock with defaults: ≈ 8 + 1 + 8 + 2 + 8 + 4 + 8 ≈ 39 s — but `CatalogDataRefreshService` invocations are wrapped by `CatalogResilienceService` whose 30 s pipeline timeout will surface first. Tune `TimeoutSeconds` down if the outer pipeline still aborts retries; raise it for ad-hoc callers that do not use the outer pipeline.

**Caller-side wrapping:** Both `CatalogDataRefreshService.RefreshEshopStockData` and `ProductPairingDqtComparer.CompareAsync` wrap `ListAsync` with `ICatalogResilienceService` for circuit-breaker + outer-timeout semantics. New callers must follow the same pattern.
```

- [ ] **Step 2: Commit**

```bash
git add docs/integrations/shoptet-api.md
git commit -m "docs(shoptet-api): document stock CSV resilience characteristics"
```

---

## Task 14: Full backend build + test sweep

Final guardrail before declaring done.

- [ ] **Step 1: Run the whole backend build**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: 0 errors, 0 new warnings.

- [ ] **Step 2: Run `dotnet format` (project rule: validation before completion)**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: 0 formatting issues. If any, run `dotnet format backend/Anela.Heblo.sln` and recommit with `chore(format): apply dotnet format`.

- [ ] **Step 3: Run the affected unit test projects**

Run: `dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "Category!=Integration"`
Expected: All tests pass.

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ProductPairingDqtComparerTests|FullyQualifiedName~CatalogDataRefreshService"`
Expected: All tests pass.

- [ ] **Step 4: Confirm the integration test resolves the named client without errors (smoke check only — does not run against staging in CI)**

Run: `dotnet build backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj`
Expected: Build succeeds. The integration test `ShoptetStockClientIntegrationTests` already constructs DI through `AddShoptetApiAdapter`, so the new named-client registration is picked up automatically.

- [ ] **Step 5: If anything failed, fix and recommit; otherwise nothing to commit. Done.**

---

## Self-Review Notes

**Spec coverage check:**

| Spec item | Task |
|---|---|
| FR-1: Use the typed/named HttpClient | Tasks 3, 4 (rewrite `ListAsync` to resolve `"ShoptetStockCsv"`) |
| FR-2: Explicit timeout | Tasks 1, 3 (options + `client.Timeout` + per-attempt `AddTimeout`) |
| FR-3: Polly retry at HTTP layer | Tasks 3, 5, 6, 7 (handler registration + 3 unit tests) |
| FR-4: Structured terminal logging | Tasks 4, 8 (try/catch with structured fields + redaction test) |
| FR-5: ProductPairingDqtComparer resilience | Tasks 11, 12 |
| FR-6: Documentation | Task 13 |
| NFR-1: Performance bound | Tasks 1, 3 (defaults fit within outer 30s pipeline) |
| NFR-2: Reliability target | covered by FR-1..FR-3 + observability |
| NFR-3: Token redaction | Tasks 4, 8, 10 |
| NFR-4: Observability (operation name constant) | Task 4 |
| NFR-5: Unit-testable HTTP layer | Tasks 5–10 (all use `DelegatingHandler` stub) |
| Amendment 1: TimeoutSeconds default 8 | Task 1 |
| Amendment 2: Cancellation-aware retry | Tasks 3, 7 |
| Amendment 3: New unit test class location | Task 5 (under `Unit/ShoptetStockClientResilienceTests.cs`) |
| Amendment 4: Redaction contract | Tasks 4, 8, 10 |
| Amendment 5: DQT operation names | Tasks 11, 12 |
