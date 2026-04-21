# Campaign Meta Ads Adapter — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the Meta Ads adapter project (`Anela.Heblo.Adapters.MetaAds`) that calls the Meta Marketing API v21.0, plus the `SyncMetaAdsHandler` MediatR handler and daily background sync task registration.

**Architecture:** Follows the existing ShoptetApi adapter pattern exactly — typed `HttpClient`, `IOptions<MetaAdsSettings>`, `DelegatingHandler` for token refresh, and a `ServiceCollectionExtensions` registration class. The sync handler lives in `Application/Features/Campaigns/UseCases/SyncMetaAds/`.

**Tech Stack:** .NET 8, `System.Text.Json`, `HttpClient`, MediatR, BackgroundRefreshTaskRegistry

**Prerequisite:** `campaign-domain-persistence` plan must be completed (entities, `IMetaAdsClient`, `ICampaignRepository` exist).

---

## File Map

**Create:**
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Anela.Heblo.Adapters.MetaAds.csproj`
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsSettings.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaTokenRefreshHandler.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsClient.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Model/MetaCampaignResponse.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Model/MetaAdSetResponse.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Model/MetaAdResponse.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Model/MetaInsightsResponse.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs`
- `backend/src/Anela.Heblo.Application/Features/Campaigns/UseCases/SyncMetaAds/SyncMetaAdsRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Campaigns/UseCases/SyncMetaAds/SyncMetaAdsHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Campaigns/CampaignsModule.cs`
- `backend/test/Anela.Heblo.Tests/Campaigns/SyncMetaAdsHandlerTests.cs`

**Modify:**
- `backend/src/Anela.Heblo.API/appsettings.json` — add `MetaAds` section
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — call `AddCampaignsModule`
- `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` — reference MetaAds adapter

---

### Task 1: Meta Ads Adapter Project

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/`

- [ ] **Step 1: Create .csproj**

`Anela.Heblo.Adapters.MetaAds.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Anela.Heblo.Domain\Anela.Heblo.Domain.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `MetaAdsSettings.cs`**

```csharp
namespace Anela.Heblo.Adapters.MetaAds;

public class MetaAdsSettings
{
    public const string ConfigurationKey = "MetaAds";

    public string AdAccountId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v21.0";
}
```

- [ ] **Step 3: Create JSON response models**

`Model/MetaCampaignResponse.cs`:
```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.MetaAds.Model;

public class MetaCampaignListResponse
{
    [JsonPropertyName("data")]
    public List<MetaCampaignData> Data { get; set; } = new();
}

public class MetaCampaignData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("objective")]
    public string Objective { get; set; } = string.Empty;

    [JsonPropertyName("daily_budget")]
    public string? DailyBudget { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("created_time")]
    public string CreatedTime { get; set; } = string.Empty;
}
```

`Model/MetaAdSetResponse.cs`:
```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.MetaAds.Model;

public class MetaAdSetListResponse
{
    [JsonPropertyName("data")]
    public List<MetaAdSetData> Data { get; set; } = new();
}

public class MetaAdSetData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("campaign_id")]
    public string CampaignId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("targeting")]
    public MetaTargeting? Targeting { get; set; }
}

public class MetaTargeting
{
    [JsonPropertyName("age_min")]
    public int? AgeMin { get; set; }

    [JsonPropertyName("age_max")]
    public int? AgeMax { get; set; }
}
```

`Model/MetaAdResponse.cs`:
```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.MetaAds.Model;

public class MetaAdListResponse
{
    [JsonPropertyName("data")]
    public List<MetaAdData> Data { get; set; } = new();
}

public class MetaAdData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("adset_id")]
    public string AdSetId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("creative")]
    public MetaCreative? Creative { get; set; }
}

public class MetaCreative
{
    [JsonPropertyName("thumbnail_url")]
    public string? ThumbnailUrl { get; set; }
}
```

`Model/MetaInsightsResponse.cs`:
```csharp
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.MetaAds.Model;

public class MetaInsightsListResponse
{
    [JsonPropertyName("data")]
    public List<MetaInsightData> Data { get; set; } = new();
}

public class MetaInsightData
{
    [JsonPropertyName("ad_id")]
    public string AdId { get; set; } = string.Empty;

    [JsonPropertyName("date_start")]
    public string DateStart { get; set; } = string.Empty;

    [JsonPropertyName("impressions")]
    public string Impressions { get; set; } = "0";

    [JsonPropertyName("clicks")]
    public string Clicks { get; set; } = "0";

    [JsonPropertyName("spend")]
    public string Spend { get; set; } = "0";

    [JsonPropertyName("actions")]
    public List<MetaAction>? Actions { get; set; }

    [JsonPropertyName("action_values")]
    public List<MetaAction>? ActionValues { get; set; }
}

public class MetaAction
{
    [JsonPropertyName("action_type")]
    public string ActionType { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = "0";
}
```

- [ ] **Step 4: Create `MetaTokenRefreshHandler.cs`**

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.MetaAds;

/// <summary>
/// DelegatingHandler that injects access_token query param into every Meta API request.
/// The token is a 60-day long-lived token managed by Business Manager System User.
/// </summary>
public class MetaTokenRefreshHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<MetaAdsSettings> _settings;
    private readonly ILogger<MetaTokenRefreshHandler> _logger;

    public MetaTokenRefreshHandler(
        IOptionsMonitor<MetaAdsSettings> settings,
        ILogger<MetaTokenRefreshHandler> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = _settings.CurrentValue.AccessToken;

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("MetaAds AccessToken is not configured");
        }

        var uriBuilder = new UriBuilder(request.RequestUri!);
        var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
        query["access_token"] = token;
        uriBuilder.Query = query.ToString();
        request.RequestUri = uriBuilder.Uri;

        return await base.SendAsync(request, cancellationToken);
    }
}
```

- [ ] **Step 5: Commit project skeleton**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/
git commit -m "feat(campaigns): scaffold MetaAds adapter project with settings, models, token handler"
```

---

### Task 2: MetaAdsClient Implementation

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsClient.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs`

- [ ] **Step 1: Implement `MetaAdsClient.cs`**

```csharp
using System.Text.Json;
using Anela.Heblo.Adapters.MetaAds.Model;
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.MetaAds;

public class MetaAdsClient : IMetaAdsClient
{
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<MetaAdsSettings> _settings;
    private readonly ILogger<MetaAdsClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MetaAdsClient(
        HttpClient http,
        IOptionsMonitor<MetaAdsSettings> settings,
        ILogger<MetaAdsClient> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MetaCampaignDto>> GetCampaignsAsync(CancellationToken ct)
    {
        var accountId = _settings.CurrentValue.AdAccountId;
        var version = _settings.CurrentValue.ApiVersion;
        var url = $"/{version}/{accountId}/campaigns?fields=id,name,status,objective,daily_budget,currency,created_time";

        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<MetaCampaignListResponse>(content, JsonOptions)
                     ?? new MetaCampaignListResponse();

        return result.Data.Select(d => new MetaCampaignDto
        {
            Id = d.Id,
            Name = d.Name,
            Status = d.Status,
            Objective = d.Objective,
            DailyBudget = d.DailyBudget is not null ? decimal.Parse(d.DailyBudget) / 100m : null,
            Currency = d.Currency,
            CreatedTime = DateTime.Parse(d.CreatedTime)
        }).ToList();
    }

    public async Task<IReadOnlyList<MetaAdSetDto>> GetAdSetsAsync(string campaignId, CancellationToken ct)
    {
        var accountId = _settings.CurrentValue.AdAccountId;
        var version = _settings.CurrentValue.ApiVersion;
        var url = $"/{version}/{accountId}/adsets?fields=id,campaign_id,name,status,targeting&filtering=[{{\"field\":\"campaign.id\",\"operator\":\"EQUAL\",\"value\":\"{campaignId}\"}}]";

        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<MetaAdSetListResponse>(content, JsonOptions)
                     ?? new MetaAdSetListResponse();

        return result.Data.Select(d => new MetaAdSetDto
        {
            Id = d.Id,
            CampaignId = d.CampaignId,
            Name = d.Name,
            Status = d.Status,
            TargetingDescription = d.Targeting is not null
                ? $"Age {d.Targeting.AgeMin}-{d.Targeting.AgeMax}"
                : null
        }).ToList();
    }

    public async Task<IReadOnlyList<MetaAdDto>> GetAdsAsync(string adSetId, CancellationToken ct)
    {
        var accountId = _settings.CurrentValue.AdAccountId;
        var version = _settings.CurrentValue.ApiVersion;
        var url = $"/{version}/{accountId}/ads?fields=id,adset_id,name,status,creative{{thumbnail_url}}&filtering=[{{\"field\":\"adset.id\",\"operator\":\"EQUAL\",\"value\":\"{adSetId}\"}}]";

        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<MetaAdListResponse>(content, JsonOptions)
                     ?? new MetaAdListResponse();

        return result.Data.Select(d => new MetaAdDto
        {
            Id = d.Id,
            AdSetId = d.AdSetId,
            Name = d.Name,
            Status = d.Status,
            CreativePreviewUrl = d.Creative?.ThumbnailUrl
        }).ToList();
    }

    public async Task<IReadOnlyList<MetaInsightDto>> GetInsightsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var accountId = _settings.CurrentValue.AdAccountId;
        var version = _settings.CurrentValue.ApiVersion;
        var timeRange = $"{{\"since\":\"{from:yyyy-MM-dd}\",\"until\":\"{to:yyyy-MM-dd}\"}}";
        var url = $"/{version}/{accountId}/insights?fields=ad_id,impressions,clicks,spend,actions,action_values&level=ad&time_range={Uri.EscapeDataString(timeRange)}&time_increment=1";

        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<MetaInsightsListResponse>(content, JsonOptions)
                     ?? new MetaInsightsListResponse();

        return result.Data.Select(d =>
        {
            var purchases = d.Actions?
                .Where(a => a.ActionType == "purchase" || a.ActionType == "offsite_conversion.fb_pixel_purchase")
                .Sum(a => int.TryParse(a.Value, out var v) ? v : 0) ?? 0;

            var purchaseValue = d.ActionValues?
                .Where(a => a.ActionType == "purchase" || a.ActionType == "offsite_conversion.fb_pixel_purchase")
                .Sum(a => decimal.TryParse(a.Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0m) ?? 0m;

            return new MetaInsightDto
            {
                AdId = d.AdId,
                Date = DateOnly.Parse(d.DateStart),
                Impressions = long.TryParse(d.Impressions, out var imp) ? imp : 0,
                Clicks = long.TryParse(d.Clicks, out var clk) ? clk : 0,
                Spend = decimal.TryParse(d.Spend, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var spd) ? spd : 0m,
                Conversions = purchases,
                ConversionValue = purchaseValue
            };
        }).ToList();
    }
}
```

- [ ] **Step 2: Create `MetaAdsAdapterServiceCollectionExtensions.cs`**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.MetaAds;

public static class MetaAdsAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddMetaAdsAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MetaAdsSettings>()
            .Bind(configuration.GetSection(MetaAdsSettings.ConfigurationKey));

        services.AddTransient<MetaTokenRefreshHandler>();

        services.AddHttpClient<IMetaAdsClient, MetaAdsClient>((_, client) =>
        {
            client.BaseAddress = new Uri("https://graph.facebook.com");
        })
        .AddHttpMessageHandler<MetaTokenRefreshHandler>();

        return services;
    }
}
```

- [ ] **Step 3: Add `MetaAds` config section to `appsettings.json`**

In `backend/src/Anela.Heblo.API/appsettings.json`, add after the existing sections:
```json
"MetaAds": {
  "AdAccountId": "act_REPLACE_WITH_YOUR_AD_ACCOUNT_ID",
  "AccessToken": "-- stored in secrets.json --",
  "ApiVersion": "v21.0"
}
```

Note: The actual `AccessToken` goes in `secrets.json` (never committed). In secrets.json add:
```json
"MetaAds": {
  "AccessToken": "YOUR_60_DAY_TOKEN_HERE"
}
```

- [ ] **Step 4: Build the adapter project**

```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/Anela.Heblo.Adapters.MetaAds.csproj
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/
git add backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat(campaigns): implement MetaAdsClient and adapter DI registration"
```

---

### Task 3: SyncMetaAds Handler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Campaigns/UseCases/SyncMetaAds/SyncMetaAdsRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Campaigns/UseCases/SyncMetaAds/SyncMetaAdsHandler.cs`

- [ ] **Step 1: Write failing handler test**

`backend/test/Anela.Heblo.Tests/Campaigns/SyncMetaAdsHandlerTests.cs`:
```csharp
using Anela.Heblo.Application.Features.Campaigns.UseCases.SyncMetaAds;
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Anela.Heblo.Tests.Campaigns;

public class SyncMetaAdsHandlerTests
{
    private readonly IMetaAdsClient _metaClient = Substitute.For<IMetaAdsClient>();
    private readonly ICampaignRepository _repository = Substitute.For<ICampaignRepository>();
    private SyncMetaAdsHandler CreateHandler() =>
        new SyncMetaAdsHandler(_metaClient, _repository, NullLogger<SyncMetaAdsHandler>.Instance);

    [Fact]
    public async Task Handle_WithCampaigns_UpsertsCampaignsAndAdSets()
    {
        var campaigns = new List<MetaCampaignDto>
        {
            new() { Id = "c1", Name = "Campaign 1", Status = "ACTIVE", Objective = "CONVERSIONS", Currency = "CZK", CreatedTime = DateTime.UtcNow }
        };
        var adSets = new List<MetaAdSetDto>
        {
            new() { Id = "as1", CampaignId = "c1", Name = "Ad Set 1", Status = "ACTIVE" }
        };
        var ads = new List<MetaAdDto>
        {
            new() { Id = "a1", AdSetId = "as1", Name = "Ad 1", Status = "ACTIVE" }
        };
        var insights = new List<MetaInsightDto>
        {
            new() { AdId = "a1", Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), Impressions = 1000, Clicks = 50, Spend = 100m, Conversions = 5, ConversionValue = 200m }
        };

        _metaClient.GetCampaignsAsync(Arg.Any<CancellationToken>()).Returns(campaigns);
        _metaClient.GetAdSetsAsync("c1", Arg.Any<CancellationToken>()).Returns(adSets);
        _metaClient.GetAdsAsync("as1", Arg.Any<CancellationToken>()).Returns(ads);
        _metaClient.GetInsightsAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(insights);

        _repository.LogSyncStartedAsync(Arg.Any<AdSyncLog>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repository.UpsertCampaignsAsync(Arg.Any<IEnumerable<AdCampaign>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repository.UpsertAdSetsAsync(Arg.Any<IEnumerable<AdAdSet>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repository.UpsertAdsAsync(Arg.Any<IEnumerable<Ad>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repository.UpsertDailyMetricsAsync(Arg.Any<IEnumerable<AdDailyMetric>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repository.UpdateSyncLogAsync(Arg.Any<AdSyncLog>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var handler = CreateHandler();
        var result = await handler.Handle(new SyncMetaAdsRequest(), CancellationToken.None);

        result.Should().BeTrue();
        await _repository.Received(1).UpsertCampaignsAsync(Arg.Any<IEnumerable<AdCampaign>>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).UpsertDailyMetricsAsync(Arg.Any<IEnumerable<AdDailyMetric>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenClientThrows_LogsFailedSyncAndReturnsFalse()
    {
        _metaClient.GetCampaignsAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("API timeout"));

        _repository.LogSyncStartedAsync(Arg.Any<AdSyncLog>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repository.UpdateSyncLogAsync(Arg.Any<AdSyncLog>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var handler = CreateHandler();
        var result = await handler.Handle(new SyncMetaAdsRequest(), CancellationToken.None);

        result.Should().BeFalse();
        await _repository.Received(1).UpdateSyncLogAsync(
            Arg.Is<AdSyncLog>(l => l.Status == "Failed"),
            Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run test — expect compilation failure**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "SyncMetaAdsHandlerTests" -v minimal
```

Expected: `SyncMetaAdsRequest` and `SyncMetaAdsHandler` not found.

- [ ] **Step 3: Create `SyncMetaAdsRequest.cs`**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Campaigns.UseCases.SyncMetaAds;

public class SyncMetaAdsRequest : IRequest<bool>
{
}
```

- [ ] **Step 4: Create `SyncMetaAdsHandler.cs`**

```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Campaigns.UseCases.SyncMetaAds;

public class SyncMetaAdsHandler : IRequestHandler<SyncMetaAdsRequest, bool>
{
    private readonly IMetaAdsClient _metaClient;
    private readonly ICampaignRepository _repository;
    private readonly ILogger<SyncMetaAdsHandler> _logger;

    public SyncMetaAdsHandler(
        IMetaAdsClient metaClient,
        ICampaignRepository repository,
        ILogger<SyncMetaAdsHandler> logger)
    {
        _metaClient = metaClient;
        _repository = repository;
        _logger = logger;
    }

    public async Task<bool> Handle(SyncMetaAdsRequest request, CancellationToken cancellationToken)
    {
        var syncLog = AdSyncLog.StartNew(AdPlatform.Meta);
        await _repository.LogSyncStartedAsync(syncLog, cancellationToken);

        try
        {
            var to = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            var from = to.AddDays(-6); // sync last 7 days

            // 1. Sync campaigns
            var metaCampaigns = await _metaClient.GetCampaignsAsync(cancellationToken);
            var domainCampaigns = metaCampaigns.Select(c => new AdCampaign
            {
                Id = Guid.NewGuid(),
                Platform = AdPlatform.Meta,
                PlatformCampaignId = c.Id,
                Name = c.Name,
                Status = c.Status,
                Objective = c.Objective,
                DailyBudget = c.DailyBudget,
                Currency = c.Currency,
                CreatedAt = c.CreatedTime,
                SyncedAt = DateTime.UtcNow
            }).ToList();
            await _repository.UpsertCampaignsAsync(domainCampaigns, cancellationToken);

            // 2. Sync ad sets for each campaign
            var allAdSets = new List<AdAdSet>();
            foreach (var campaign in metaCampaigns)
            {
                var metaAdSets = await _metaClient.GetAdSetsAsync(campaign.Id, cancellationToken);
                var campaignId = domainCampaigns.First(c => c.PlatformCampaignId == campaign.Id).Id;

                allAdSets.AddRange(metaAdSets.Select(s => new AdAdSet
                {
                    Id = Guid.NewGuid(),
                    CampaignId = campaignId,
                    Platform = AdPlatform.Meta,
                    PlatformAdSetId = s.Id,
                    Name = s.Name,
                    Status = s.Status,
                    TargetingDescription = s.TargetingDescription
                }));
            }
            await _repository.UpsertAdSetsAsync(allAdSets, cancellationToken);

            // 3. Sync ads for each ad set
            var allAds = new List<Ad>();
            foreach (var adSet in allAdSets)
            {
                var metaAds = await _metaClient.GetAdsAsync(
                    allAdSets.First(s => s.Id == adSet.Id).PlatformAdSetId,
                    cancellationToken);

                allAds.AddRange(metaAds.Select(a => new Ad
                {
                    Id = Guid.NewGuid(),
                    AdSetId = adSet.Id,
                    Platform = AdPlatform.Meta,
                    PlatformAdId = a.Id,
                    Name = a.Name,
                    Status = a.Status,
                    CreativePreviewUrl = a.CreativePreviewUrl
                }));
            }
            await _repository.UpsertAdsAsync(allAds, cancellationToken);

            // 4. Sync insights (metrics)
            var insights = await _metaClient.GetInsightsAsync(from, to, cancellationToken);
            var metrics = insights.Select(i =>
            {
                var adId = allAds.FirstOrDefault(a => a.PlatformAdId == i.AdId)?.Id ?? Guid.NewGuid();
                return AdDailyMetric.Compute(
                    Guid.NewGuid(), adId, i.Date,
                    i.Impressions, i.Clicks, i.Spend,
                    i.Conversions, i.ConversionValue);
            }).ToList();
            await _repository.UpsertDailyMetricsAsync(metrics, cancellationToken);

            syncLog.MarkSucceeded(domainCampaigns.Count + allAdSets.Count + allAds.Count + metrics.Count);
            await _repository.UpdateSyncLogAsync(syncLog, cancellationToken);

            _logger.LogInformation(
                "Meta Ads sync completed: {Campaigns} campaigns, {AdSets} ad sets, {Ads} ads, {Metrics} metric rows",
                domainCampaigns.Count, allAdSets.Count, allAds.Count, metrics.Count);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Meta Ads sync failed");
            syncLog.MarkFailed(ex.Message);
            await _repository.UpdateSyncLogAsync(syncLog, cancellationToken);
            return false;
        }
    }
}
```

- [ ] **Step 5: Run tests — expect PASS**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "SyncMetaAdsHandlerTests" -v minimal
```

Expected: 2 tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Campaigns/
git add backend/test/Anela.Heblo.Tests/Campaigns/SyncMetaAdsHandlerTests.cs
git commit -m "feat(campaigns): add SyncMetaAdsHandler with error handling and sync log"
```

---

### Task 4: Module Wiring and Background Sync

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Campaigns/CampaignsModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (BackgroundRefresh section)

- [ ] **Step 1: Create `CampaignsModule.cs`**

```csharp
using Anela.Heblo.Adapters.MetaAds;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Campaigns;

public static class CampaignsModule
{
    public static IServiceCollection AddCampaignsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMetaAdsAdapter(configuration);
        // Google adapter added in campaign-google-adapter plan
        return services;
    }
}
```

- [ ] **Step 2: Register in `ApplicationModule.cs`**

Add after `services.AddGridLayoutsModule();`:
```csharp
services.AddCampaignsModule(configuration);
```

Add using:
```csharp
using Anela.Heblo.Application.Features.Campaigns;
```

- [ ] **Step 3: Reference MetaAds adapter in API .csproj**

In `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`, add:
```xml
<ProjectReference Include="..\..\..\Adapters\Anela.Heblo.Adapters.MetaAds\Anela.Heblo.Adapters.MetaAds.csproj" />
```

- [ ] **Step 4: Register Meta sync as background task**

In `appsettings.json`, add to `BackgroundRefresh` section:
```json
"SyncMetaAds": {
  "InitialDelay": "00:05:00",
  "RefreshInterval": "24:00:00",
  "Enabled": true,
  "HydrationTier": 5,
  "Description": "Syncs Meta Ads campaign data from Marketing API (runs daily)"
}
```

Register the background task. Find the file where other background tasks are registered (e.g., in the Xcc or a module setup file). Add:

```csharp
registry.RegisterTask(
    "SyncMetaAds",
    async (sp, ct) =>
    {
        var mediator = sp.GetRequiredService<IMediator>();
        await mediator.Send(new SyncMetaAdsRequest(), ct);
    },
    configuration.GetSection("BackgroundRefresh:SyncMetaAds").Get<RefreshTaskConfiguration>()
        ?? new RefreshTaskConfiguration());
```

- [ ] **Step 5: Full solution build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Run all campaign tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Campaigns" -v minimal
```

Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Campaigns/CampaignsModule.cs
git add backend/src/Anela.Heblo.Application/ApplicationModule.cs
git add backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
git add backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat(campaigns): wire CampaignsModule, register MetaAds adapter and background sync task"
```
