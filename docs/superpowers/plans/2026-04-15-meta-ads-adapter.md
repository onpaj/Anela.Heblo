# Meta Ads Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create `Anela.Heblo.Adapters.MetaAds` — fetches billing transactions from Meta Graph API and persists them using the shared marketing invoice import core.

**Architecture:** New .NET 8 class library adapter following the Comgate pattern (Polly for resilience). `MetaAdsTransactionSource` implements `IMarketingTransactionSource` and handles pagination + HTTP 429 retry. `MetaAdsInvoiceImportJob` instantiates `MarketingInvoiceImportService` directly with the concrete source (avoids future DI conflict with Google Ads).

**Tech Stack:** .NET 8, Polly 8.x (retry on HTTP 429), `System.Text.Json`, `IHttpClientFactory`, `IRecurringJob` (Hangfire auto-discovery), xUnit + Moq + FluentAssertions.

---

## File Map

| Action | Path |
|--------|------|
| Create | `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Anela.Heblo.Adapters.MetaAds.csproj` |
| Create | `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsSettings.cs` |
| Create | `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs` |
| Create | `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsInvoiceImportJob.cs` |
| Create | `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs` |
| Create | `backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsTransactionSourceTests.cs` |
| Modify | `Anela.Heblo.sln` — add new project |
| Modify | `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — add project reference |
| Modify | `backend/src/Anela.Heblo.API/Program.cs` — register adapter |
| Modify | `backend/src/Anela.Heblo.API/appsettings.json` — add MetaAds config stub |

---

## Task 1: Create adapter project and settings

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Anela.Heblo.Adapters.MetaAds.csproj`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsSettings.cs`
- Modify: `Anela.Heblo.sln`

- [ ] **Step 1: Create the project directory and .csproj**

Create `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Anela.Heblo.Adapters.MetaAds.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Anela.Heblo.Adapters.MetaAds</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
    <PackageReference Include="Polly" Version="8.4.1" />
    <PackageReference Include="Polly.Extensions" Version="8.4.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Anela.Heblo.Domain\Anela.Heblo.Domain.csproj" />
    <ProjectReference Include="..\..\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add project to solution**

Run from repo root (where `Anela.Heblo.sln` lives):

```bash
dotnet sln add backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Anela.Heblo.Adapters.MetaAds.csproj
```

Expected output: `Project 'backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Anela.Heblo.Adapters.MetaAds.csproj' added to the solution.`

- [ ] **Step 3: Create MetaAdsSettings.cs**

Create `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsSettings.cs`:

```csharp
namespace Anela.Heblo.Adapters.MetaAds;

public class MetaAdsSettings
{
    public const string ConfigurationKey = "MetaAds";

    /// <summary>Ad account ID in the form "act_123456789".</summary>
    public string AdAccountId { get; set; } = string.Empty;

    /// <summary>System User token from Meta Business Manager. Store in secrets.json / Key Vault.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Graph API version, e.g. "v21.0".</summary>
    public string ApiVersion { get; set; } = "v21.0";
}
```

- [ ] **Step 4: Verify the project builds**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Anela.Heblo.Adapters.MetaAds.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/ Anela.Heblo.sln
git commit -m "feat(marketing-invoices): scaffold MetaAds adapter project and settings"
```

---

## Task 2: Write tests for MetaAdsTransactionSource (TDD — red phase)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsTransactionSourceTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`

- [ ] **Step 1: Add project reference to test project**

In `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`, add inside the existing `<ItemGroup>` that contains other `<ProjectReference>` entries:

```xml
<ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.MetaAds\Anela.Heblo.Adapters.MetaAds.csproj" />
```

- [ ] **Step 2: Create the test file**

Create `backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsTransactionSourceTests.cs`:

```csharp
using System.Net;
using System.Text;
using Anela.Heblo.Adapters.MetaAds;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.MetaAds;

public class MetaAdsTransactionSourceTests
{
    private static MetaAdsTransactionSource CreateSource(
        HttpMessageHandler handler,
        MetaAdsSettings? settings = null)
    {
        settings ??= new MetaAdsSettings
        {
            AdAccountId = "act_123456789",
            AccessToken = "test-token",
            ApiVersion = "v21.0"
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.facebook.com/")
        };

        return new MetaAdsTransactionSource(
            httpClient,
            Options.Create(settings),
            NullLogger<MetaAdsTransactionSource>.Instance);
    }

    [Fact]
    public async Task GetTransactionsAsync_ValidResponse_ParsesFieldsCorrectly()
    {
        // Arrange
        var json = """
            {
              "data": [
                {
                  "id": "TX-001",
                  "time": 1744300800,
                  "amount": 150000,
                  "currency": "CZK",
                  "payment_type": "THRESHOLD"
                }
              ],
              "paging": {
                "cursors": { "before": "abc", "after": "def" }
              }
            }
            """;

        var handler = new StaticResponseHandler(HttpStatusCode.OK, json);
        var source = CreateSource(handler);

        var from = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(-1);
        var to = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(1);

        // Act
        var transactions = await source.GetTransactionsAsync(from, to, CancellationToken.None);

        // Assert
        transactions.Should().HaveCount(1);
        var tx = transactions[0];
        tx.TransactionId.Should().Be("TX-001");
        tx.Amount.Should().Be(1500.00m);
        tx.Currency.Should().Be("CZK");
        tx.Description.Should().Be("THRESHOLD");
        tx.Platform.Should().Be("MetaAds");
        tx.TransactionDate.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime);
    }

    [Fact]
    public async Task GetTransactionsAsync_Amount_ConvertedFromCentsToDecimal()
    {
        // Arrange
        var json = """
            {
              "data": [
                {
                  "id": "TX-002",
                  "time": 1744300800,
                  "amount": 150000,
                  "currency": "CZK",
                  "payment_type": "THRESHOLD"
                }
              ],
              "paging": {}
            }
            """;

        var handler = new StaticResponseHandler(HttpStatusCode.OK, json);
        var source = CreateSource(handler);

        var from = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(-1);
        var to = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(1);

        // Act
        var transactions = await source.GetTransactionsAsync(from, to, CancellationToken.None);

        // Assert
        transactions.Should().HaveCount(1);
        transactions[0].Amount.Should().Be(1500.00m);
    }

    [Fact]
    public async Task GetTransactionsAsync_Pagination_AllPagesCollected()
    {
        // Arrange — page 1 has paging.next, page 2 has none
        var page1Json = """
            {
              "data": [
                { "id": "TX-001", "time": 1744300800, "amount": 10000, "currency": "CZK", "payment_type": "THRESHOLD" }
              ],
              "paging": {
                "next": "https://graph.facebook.com/v21.0/act_123456789/transactions?after=cursor1&access_token=test-token"
              }
            }
            """;

        var page2Json = """
            {
              "data": [
                { "id": "TX-002", "time": 1744300800, "amount": 20000, "currency": "CZK", "payment_type": "THRESHOLD" }
              ],
              "paging": {
                "cursors": { "before": "cursor1", "after": "cursor2" }
              }
            }
            """;

        var handler = new SequentialResponseHandler(
            (HttpStatusCode.OK, page1Json),
            (HttpStatusCode.OK, page2Json));

        var source = CreateSource(handler);

        var from = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(-1);
        var to = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(1);

        // Act
        var transactions = await source.GetTransactionsAsync(from, to, CancellationToken.None);

        // Assert
        transactions.Should().HaveCount(2);
        transactions.Select(t => t.TransactionId).Should().Contain(["TX-001", "TX-002"]);
    }

    [Fact]
    public async Task GetTransactionsAsync_RateLimitRetry_SucceedsOnSecondAttempt()
    {
        // Arrange — first call returns 429, second returns 200 with a transaction
        var successJson = """
            {
              "data": [
                { "id": "TX-001", "time": 1744300800, "amount": 10000, "currency": "CZK", "payment_type": "THRESHOLD" }
              ],
              "paging": {}
            }
            """;

        var handler = new SequentialResponseHandler(
            (HttpStatusCode.TooManyRequests, "{}"),
            (HttpStatusCode.OK, successJson));

        // Build source with a fast-retry pipeline (no delay) for test speed
        var settings = new MetaAdsSettings
        {
            AdAccountId = "act_123456789",
            AccessToken = "test-token",
            ApiVersion = "v21.0"
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.facebook.com/")
        };

        var source = new MetaAdsTransactionSource(
            httpClient,
            Options.Create(settings),
            NullLogger<MetaAdsTransactionSource>.Instance,
            MetaAdsTransactionSource.BuildTestPipeline());

        var from = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(-1);
        var to = DateTimeOffset.FromUnixTimeSeconds(1744300800).UtcDateTime.AddDays(1);

        // Act
        var transactions = await source.GetTransactionsAsync(from, to, CancellationToken.None);

        // Assert — should succeed after retry, not throw
        transactions.Should().HaveCount(1);
        transactions[0].TransactionId.Should().Be("TX-001");
    }
}

/// <summary>Always returns the same response.</summary>
file sealed class StaticResponseHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}

/// <summary>Returns responses in sequence; repeats the last one if exhausted.</summary>
file sealed class SequentialResponseHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode, string)> _responses;

    public SequentialResponseHandler(params (HttpStatusCode, string)[] responses)
    {
        _responses = new Queue<(HttpStatusCode, string)>(responses);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Dequeue until the last entry; keep the last for any additional calls.
        var (status, body) = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
```

- [ ] **Step 3: Run tests — verify they fail (red)**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MetaAdsTransactionSourceTests" \
  --no-build 2>&1 | tail -20
```

Expected: Compilation errors because `MetaAdsTransactionSource` doesn't exist yet. That's correct.

- [ ] **Step 4: Commit tests**

```bash
git add backend/test/Anela.Heblo.Tests/
git commit -m "test(marketing-invoices): add MetaAdsTransactionSource unit tests (red)"
```

---

## Task 3: Implement MetaAdsTransactionSource (green phase)

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs`

- [ ] **Step 1: Create MetaAdsTransactionSource.cs**

Create `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs`:

```csharp
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Anela.Heblo.Adapters.MetaAds;

public class MetaAdsTransactionSource : IMarketingTransactionSource
{
    private readonly HttpClient _httpClient;
    private readonly MetaAdsSettings _settings;
    private readonly ILogger<MetaAdsTransactionSource> _logger;
    private readonly ResiliencePipeline _pipeline;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public string Platform => "MetaAds";

    public MetaAdsTransactionSource(
        HttpClient httpClient,
        IOptions<MetaAdsSettings> options,
        ILogger<MetaAdsTransactionSource> logger,
        ResiliencePipeline? pipeline = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pipeline = pipeline ?? BuildDefaultPipeline();
    }

    public async Task<List<MarketingTransaction>> GetTransactionsAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        var results = new List<MarketingTransaction>();
        var url = BuildInitialUrl();

        _logger.LogInformation(
            "MetaAds: fetching transactions for account {AccountId} from {From:yyyy-MM-dd} to {To:yyyy-MM-dd}",
            _settings.AdAccountId, from, to);

        while (url is not null)
        {
            var page = await FetchPageAsync(url, ct);

            foreach (var item in page.Data)
            {
                var txDate = DateTimeOffset.FromUnixTimeSeconds(item.Time).UtcDateTime;

                if (txDate < from || txDate > to)
                    continue;

                results.Add(new MarketingTransaction
                {
                    TransactionId = item.Id,
                    Platform = Platform,
                    Amount = item.Amount / 100m,
                    TransactionDate = txDate,
                    Currency = item.Currency,
                    Description = item.PaymentType,
                    RawData = JsonSerializer.Serialize(item, JsonOptions),
                });
            }

            url = page.Paging?.Next;
        }

        _logger.LogInformation(
            "MetaAds: fetched {Count} transactions within date range", results.Count);

        return results;
    }

    private async Task<MetaTransactionsResponse> FetchPageAsync(string url, CancellationToken ct)
    {
        _logger.LogDebug("MetaAds: GET {Url}", RedactToken(url));

        return await _pipeline.ExecuteAsync(async innerCt =>
        {
            var response = await _httpClient.GetAsync(url, innerCt);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(innerCt);
            return JsonSerializer.Deserialize<MetaTransactionsResponse>(json, JsonOptions)
                   ?? throw new InvalidOperationException("MetaAds API returned null response body.");
        }, ct);
    }

    private string BuildInitialUrl() =>
        $"https://graph.facebook.com/{_settings.ApiVersion}/{_settings.AdAccountId}/transactions" +
        $"?fields=id,time,amount,currency,payment_type" +
        $"&access_token={_settings.AccessToken}";

    private static string RedactToken(string url)
    {
        var idx = url.IndexOf("access_token=", StringComparison.Ordinal);
        if (idx < 0) return url;
        var end = url.IndexOf('&', idx);
        return end < 0
            ? url[..idx] + "access_token=***"
            : url[..idx] + "access_token=***" + url[end..];
    }

    /// <summary>Production resilience pipeline: retry on HTTP 429 with exponential backoff.</summary>
    internal static ResiliencePipeline BuildDefaultPipeline() =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>(ex => ex.StatusCode == HttpStatusCode.TooManyRequests),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
            })
            .Build();

    /// <summary>Zero-delay pipeline for unit tests.</summary>
    internal static ResiliencePipeline BuildTestPipeline() =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>(ex => ex.StatusCode == HttpStatusCode.TooManyRequests),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.Zero,
            })
            .Build();

    // ── Internal deserialization models ──────────────────────────────────────

    private sealed class MetaTransactionsResponse
    {
        [JsonPropertyName("data")]
        public List<MetaTransactionItem> Data { get; set; } = [];

        [JsonPropertyName("paging")]
        public MetaPaging? Paging { get; set; }
    }

    private sealed class MetaTransactionItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("time")]
        public long Time { get; set; }

        /// <summary>Amount in cents (integer).</summary>
        [JsonPropertyName("amount")]
        public long Amount { get; set; }

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = string.Empty;

        [JsonPropertyName("payment_type")]
        public string PaymentType { get; set; } = string.Empty;
    }

    private sealed class MetaPaging
    {
        [JsonPropertyName("next")]
        public string? Next { get; set; }
    }
}
```

- [ ] **Step 2: Run tests — verify they pass (green)**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~MetaAdsTransactionSourceTests"
```

Expected:
```
Passed!  - Failed: 0, Passed: 4, Skipped: 0
```

- [ ] **Step 3: Run dotnet format**

```bash
dotnet format backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Anela.Heblo.Adapters.MetaAds.csproj
```

Expected: exits with code 0, no output (or "Format complete").

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs
git commit -m "feat(marketing-invoices): implement MetaAdsTransactionSource with Polly retry"
```

---

## Task 4: Implement MetaAdsInvoiceImportJob

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsInvoiceImportJob.cs`

- [ ] **Step 1: Create MetaAdsInvoiceImportJob.cs**

Create `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsInvoiceImportJob.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.MetaAds;

public class MetaAdsInvoiceImportJob : IRecurringJob
{
    private readonly MetaAdsTransactionSource _source;
    private readonly IImportedMarketingTransactionRepository _repository;
    private readonly ILogger<MarketingInvoiceImportService> _importLogger;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<MetaAdsInvoiceImportJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "meta-ads-invoice-import",
        DisplayName = "Meta Ads Invoice Import",
        Description = "Fetches billing transactions from Meta Ads Graph API (7-day lookback)",
        CronExpression = "0 6,18 * * *", // 6 AM and 6 PM Prague time
        DefaultIsEnabled = true,
    };

    public MetaAdsInvoiceImportJob(
        MetaAdsTransactionSource source,
        IImportedMarketingTransactionRepository repository,
        ILogger<MarketingInvoiceImportService> importLogger,
        IRecurringJobStatusChecker statusChecker,
        ILogger<MetaAdsInvoiceImportJob> logger)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _importLogger = importLogger ?? throw new ArgumentNullException(nameof(importLogger));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        try
        {
            _logger.LogInformation("Starting {JobName}", Metadata.JobName);

            var to = DateTime.UtcNow;
            var from = to.AddDays(-7);

            var service = new MarketingInvoiceImportService(_source, _repository, _importLogger);
            var result = await service.ImportAsync(from, to, cancellationToken);

            _logger.LogInformation(
                "{JobName} completed. Imported={Imported}, Skipped={Skipped}, Failed={Failed}",
                Metadata.JobName, result.Imported, result.Skipped, result.Failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed", Metadata.JobName);
            throw;
        }
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Anela.Heblo.Adapters.MetaAds.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsInvoiceImportJob.cs
git commit -m "feat(marketing-invoices): add MetaAdsInvoiceImportJob recurring job"
```

---

## Task 5: Wire DI, configuration, and Program.cs

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs`
- Modify: `backend/src/Anela.Heblo.API/Program.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` (add project reference)

- [ ] **Step 1: Create MetaAdsAdapterServiceCollectionExtensions.cs**

Create `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs`:

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.MetaAds;

public static class MetaAdsAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddMetaAdsAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MetaAdsSettings>(configuration.GetSection(MetaAdsSettings.ConfigurationKey));
        services.AddHttpClient<MetaAdsTransactionSource>();
        services.AddScoped<IRecurringJob, MetaAdsInvoiceImportJob>();
        return services;
    }
}
```

- [ ] **Step 2: Add project reference in API .csproj**

Open `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` and add inside the `<ItemGroup>` block that contains other adapter `<ProjectReference>` entries:

```xml
<ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.MetaAds\Anela.Heblo.Adapters.MetaAds.csproj" />
```

- [ ] **Step 3: Register adapter in Program.cs**

In `backend/src/Anela.Heblo.API/Program.cs`, add the using at the top:

```csharp
using Anela.Heblo.Adapters.MetaAds;
```

Then add the registration after `builder.Services.AddComgateAdapter(builder.Configuration);`:

```csharp
builder.Services.AddMetaAdsAdapter(builder.Configuration);
```

- [ ] **Step 4: Add config stub to appsettings.json**

In `backend/src/Anela.Heblo.API/appsettings.json`, add after the `"Comgate"` block:

```json
"MetaAds": {
  "AdAccountId": "act_XXXXXXXXX",
  "ApiVersion": "v21.0"
},
```

(`AccessToken` is intentionally omitted from `appsettings.json` — it goes in `secrets.json` / Key Vault only.)

- [ ] **Step 5: Verify full solution build**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 6: Run all backend tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: All previously passing tests still pass, plus the 4 new MetaAds tests.

- [ ] **Step 7: Run dotnet format on changed projects**

```bash
dotnet format backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Anela.Heblo.Adapters.MetaAds.csproj
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: exits 0.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/ \
        backend/src/Anela.Heblo.API/Program.cs \
        backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj \
        backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat(marketing-invoices): wire MetaAds adapter into DI and configuration"
```

---

## Task 6: Final verification and PR

- [ ] **Step 1: Full solution build**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` (warnings from other projects are pre-existing and acceptable).

- [ ] **Step 2: Full test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: `Failed: 0` — all tests pass.

- [ ] **Step 3: Final format check**

```bash
dotnet format --verify-no-changes
```

Expected: exits 0. If not, run `dotnet format` and re-commit.

- [ ] **Step 4: Push branch**

```bash
git push -u origin feat/marketing-invoices-meta-ads
```

- [ ] **Step 5: Create PR**

```bash
gh pr create \
  --title "feat(marketing-invoices): Meta Ads transaction fetching adapter (#607)" \
  --body "## Summary
- Adds \`Anela.Heblo.Adapters.MetaAds\` project
- \`MetaAdsTransactionSource\` fetches billing transactions from Meta Graph API with pagination and HTTP 429 retry (Polly)
- \`MetaAdsInvoiceImportJob\` runs at 6 AM / 6 PM Prague time with 7-day lookback
- 4 unit tests covering parsing, amount conversion, pagination, and rate-limit retry
- Closes #607

## Test plan
- [ ] \`dotnet build\` passes
- [ ] \`dotnet test\` passes (4 new MetaAds tests + all existing tests)
- [ ] \`dotnet format --verify-no-changes\` passes
- [ ] Adapter registered in DI and \`appsettings.json\` has non-secret config stub

🤖 Generated with [Claude Code](https://claude.com/claude-code)" \
  --base feat/marketing-invoices-shared-core
```

Note: Base branch is `feat/marketing-invoices-shared-core` (not `main`) because this depends on the shared core from #606.
