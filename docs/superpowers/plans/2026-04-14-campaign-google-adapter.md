# Campaign Google Ads Adapter — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the Google Ads adapter project (`Anela.Heblo.Adapters.GoogleAds`) using the official `Google.Ads.GoogleAds` NuGet SDK, plus the `SyncGoogleAdsHandler` MediatR handler and daily background sync task registration.

**Architecture:** Follows the same adapter pattern as MetaAds and ShoptetApi. The SDK handles OAuth token refresh automatically. The adapter wraps the SDK's `GoogleAdsClient` behind `IGoogleAdsClient` for testability. Sync handler mirrors `SyncMetaAdsHandler`.

**Tech Stack:** .NET 8, `Google.Ads.GoogleAds` NuGet SDK, GAQL (Google Ads Query Language), MediatR, BackgroundRefreshTaskRegistry

**Prerequisite:** `campaign-domain-persistence` plan must be completed (entities, `IGoogleAdsClient`, `ICampaignRepository` exist). `campaign-meta-adapter` plan must be completed (`CampaignsModule.cs` already exists).

---

## File Map

**Create:**
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Anela.Heblo.Adapters.GoogleAds.csproj`
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsSettings.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsClientWrapper.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs`
- `backend/src/Anela.Heblo.Application/Features/Campaigns/UseCases/SyncGoogleAds/SyncGoogleAdsRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Campaigns/UseCases/SyncGoogleAds/SyncGoogleAdsHandler.cs`
- `backend/test/Anela.Heblo.Tests/Campaigns/SyncGoogleAdsHandlerTests.cs`

**Modify:**
- `backend/src/Anela.Heblo.Application/Features/Campaigns/CampaignsModule.cs` — add `AddGoogleAdsAdapter`
- `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` — reference GoogleAds adapter
- `backend/src/Anela.Heblo.API/appsettings.json` — add `GoogleAds` section + BackgroundRefresh entry

---

### Task 1: Google Ads Adapter Project

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/`

- [ ] **Step 1: Create .csproj with Google Ads SDK**

`Anela.Heblo.Adapters.GoogleAds.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Google.Ads.GoogleAds" Version="21.1.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Anela.Heblo.Domain\Anela.Heblo.Domain.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Restore packages**

```bash
dotnet restore backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Anela.Heblo.Adapters.GoogleAds.csproj
```

Expected: Packages restored successfully.

- [ ] **Step 3: Create `GoogleAdsSettings.cs`**

```csharp
namespace Anela.Heblo.Adapters.GoogleAds;

public class GoogleAdsSettings
{
    public const string ConfigurationKey = "GoogleAds";

    public string CustomerId { get; set; } = string.Empty;
    public string DeveloperToken { get; set; } = string.Empty;
    public string OAuth2ClientId { get; set; } = string.Empty;
    public string OAuth2ClientSecret { get; set; } = string.Empty;
    public string OAuth2RefreshToken { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Commit project skeleton**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/
git commit -m "feat(campaigns): scaffold GoogleAds adapter project with settings"
```

---

### Task 2: GoogleAdsClientWrapper Implementation

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsClientWrapper.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs`

- [ ] **Step 1: Implement `GoogleAdsClientWrapper.cs`**

```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using Google.Ads.Gax.Config;
using Google.Ads.GoogleAds;
using Google.Ads.GoogleAds.Lib;
using Google.Ads.GoogleAds.V18.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAds;

public class GoogleAdsClientWrapper : IGoogleAdsClient
{
    private readonly IOptionsMonitor<GoogleAdsSettings> _settings;
    private readonly ILogger<GoogleAdsClientWrapper> _logger;

    public GoogleAdsClientWrapper(
        IOptionsMonitor<GoogleAdsSettings> settings,
        ILogger<GoogleAdsClientWrapper> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    private GoogleAdsClient BuildClient()
    {
        var s = _settings.CurrentValue;
        var config = new GoogleAdsConfig
        {
            DeveloperToken = s.DeveloperToken,
            OAuth2ClientId = s.OAuth2ClientId,
            OAuth2ClientSecret = s.OAuth2ClientSecret,
            OAuth2RefreshToken = s.OAuth2RefreshToken,
            LoginCustomerId = s.CustomerId
        };
        return new GoogleAdsClient(config);
    }

    private string NormalizeCustomerId(string customerId) =>
        customerId.Replace("-", "");

    public async Task<IReadOnlyList<GoogleCampaignDto>> GetCampaignsAsync(CancellationToken ct)
    {
        var client = BuildClient();
        var customerId = NormalizeCustomerId(_settings.CurrentValue.CustomerId);
        var service = client.GetService(Services.V18.GoogleAdsService);

        const string query = @"
            SELECT
                campaign.id,
                campaign.name,
                campaign.status,
                campaign.advertising_channel_type,
                campaign_budget.amount_micros
            FROM campaign
            WHERE campaign.status != 'REMOVED'";

        var results = new List<GoogleCampaignDto>();

        await foreach (var row in service.SearchStreamAsync(customerId, query)
            .WithCancellation(ct))
        {
            results.Add(new GoogleCampaignDto
            {
                Id = row.Campaign.Id.ToString(),
                Name = row.Campaign.Name,
                Status = row.Campaign.Status.ToString(),
                AdvertisingChannelType = row.Campaign.AdvertisingChannelType.ToString(),
                DailyBudgetMicros = row.CampaignBudget?.AmountMicros
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<GoogleAdGroupDto>> GetAdGroupsAsync(string campaignId, CancellationToken ct)
    {
        var client = BuildClient();
        var customerId = NormalizeCustomerId(_settings.CurrentValue.CustomerId);
        var service = client.GetService(Services.V18.GoogleAdsService);

        var query = $@"
            SELECT
                ad_group.id,
                ad_group.name,
                ad_group.status,
                ad_group.campaign
            FROM ad_group
            WHERE campaign.id = {campaignId}
              AND ad_group.status != 'REMOVED'";

        var results = new List<GoogleAdGroupDto>();

        await foreach (var row in service.SearchStreamAsync(customerId, query)
            .WithCancellation(ct))
        {
            results.Add(new GoogleAdGroupDto
            {
                Id = row.AdGroup.Id.ToString(),
                CampaignId = campaignId,
                Name = row.AdGroup.Name,
                Status = row.AdGroup.Status.ToString()
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<GoogleAdDto>> GetAdsAsync(string adGroupId, CancellationToken ct)
    {
        var client = BuildClient();
        var customerId = NormalizeCustomerId(_settings.CurrentValue.CustomerId);
        var service = client.GetService(Services.V18.GoogleAdsService);

        var query = $@"
            SELECT
                ad_group_ad.ad.id,
                ad_group_ad.ad.name,
                ad_group_ad.status
            FROM ad_group_ad
            WHERE ad_group.id = {adGroupId}
              AND ad_group_ad.status != 'REMOVED'";

        var results = new List<GoogleAdDto>();

        await foreach (var row in service.SearchStreamAsync(customerId, query)
            .WithCancellation(ct))
        {
            results.Add(new GoogleAdDto
            {
                Id = row.AdGroupAd.Ad.Id.ToString(),
                AdGroupId = adGroupId,
                Name = row.AdGroupAd.Ad.Name ?? string.Empty,
                Status = row.AdGroupAd.Status.ToString()
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<GoogleMetricDto>> GetMetricsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var client = BuildClient();
        var customerId = NormalizeCustomerId(_settings.CurrentValue.CustomerId);
        var service = client.GetService(Services.V18.GoogleAdsService);

        var query = $@"
            SELECT
                segments.date,
                metrics.impressions,
                metrics.clicks,
                metrics.cost_micros,
                metrics.conversions,
                metrics.conversions_value,
                ad_group_ad.ad.id
            FROM ad_group_ad
            WHERE segments.date BETWEEN '{from:yyyy-MM-dd}' AND '{to:yyyy-MM-dd}'
              AND ad_group_ad.status != 'REMOVED'";

        var results = new List<GoogleMetricDto>();

        await foreach (var row in service.SearchStreamAsync(customerId, query)
            .WithCancellation(ct))
        {
            results.Add(new GoogleMetricDto
            {
                AdId = row.AdGroupAd.Ad.Id.ToString(),
                Date = DateOnly.ParseExact(row.Segments.Date, "yyyy-MM-dd"),
                Impressions = row.Metrics.Impressions,
                Clicks = row.Metrics.Clicks,
                CostMicros = row.Metrics.CostMicros,
                Conversions = (int)row.Metrics.Conversions,
                ConversionsValue = (decimal)row.Metrics.ConversionsValue
            });
        }

        return results;
    }
}
```

- [ ] **Step 2: Create `GoogleAdsAdapterServiceCollectionExtensions.cs`**

```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.GoogleAds;

public static class GoogleAdsAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddGoogleAdsAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<GoogleAdsSettings>()
            .Bind(configuration.GetSection(GoogleAdsSettings.ConfigurationKey));

        services.AddScoped<IGoogleAdsClient, GoogleAdsClientWrapper>();

        return services;
    }
}
```

- [ ] **Step 3: Add `GoogleAds` config section to `appsettings.json`**

In `backend/src/Anela.Heblo.API/appsettings.json`, add:
```json
"GoogleAds": {
  "CustomerId": "XXX-XXX-XXXX",
  "DeveloperToken": "-- stored in secrets.json --",
  "OAuth2ClientId": "-- stored in secrets.json --",
  "OAuth2ClientSecret": "-- stored in secrets.json --",
  "OAuth2RefreshToken": "-- stored in secrets.json --"
}
```

In `secrets.json` (never committed), add:
```json
"GoogleAds": {
  "DeveloperToken": "YOUR_DEVELOPER_TOKEN",
  "OAuth2ClientId": "YOUR_CLIENT_ID",
  "OAuth2ClientSecret": "YOUR_CLIENT_SECRET",
  "OAuth2RefreshToken": "YOUR_REFRESH_TOKEN"
}
```

- [ ] **Step 4: Build**

```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/Anela.Heblo.Adapters.GoogleAds.csproj
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/
git add backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat(campaigns): implement GoogleAdsClientWrapper using official SDK and adapter DI registration"
```

---

### Task 3: SyncGoogleAds Handler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Campaigns/UseCases/SyncGoogleAds/SyncGoogleAdsRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Campaigns/UseCases/SyncGoogleAds/SyncGoogleAdsHandler.cs`

- [ ] **Step 1: Write failing handler test**

`backend/test/Anela.Heblo.Tests/Campaigns/SyncGoogleAdsHandlerTests.cs`:
```csharp
using Anela.Heblo.Application.Features.Campaigns.UseCases.SyncGoogleAds;
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Anela.Heblo.Tests.Campaigns;

public class SyncGoogleAdsHandlerTests
{
    private readonly IGoogleAdsClient _googleClient = Substitute.For<IGoogleAdsClient>();
    private readonly ICampaignRepository _repository = Substitute.For<ICampaignRepository>();
    private SyncGoogleAdsHandler CreateHandler() =>
        new SyncGoogleAdsHandler(_googleClient, _repository, NullLogger<SyncGoogleAdsHandler>.Instance);

    [Fact]
    public async Task Handle_WithCampaigns_UpsertsCampaignsAndMetrics()
    {
        var campaigns = new List<GoogleCampaignDto>
        {
            new() { Id = "123", Name = "Google Campaign", Status = "ENABLED", AdvertisingChannelType = "SEARCH" }
        };
        var adGroups = new List<GoogleAdGroupDto>
        {
            new() { Id = "456", CampaignId = "123", Name = "Ad Group 1", Status = "ENABLED" }
        };
        var ads = new List<GoogleAdDto>
        {
            new() { Id = "789", AdGroupId = "456", Name = "Ad 1", Status = "ENABLED" }
        };
        var metrics = new List<GoogleMetricDto>
        {
            new() { AdId = "789", Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), Impressions = 2000, Clicks = 100, CostMicros = 50000000m, Conversions = 10, ConversionsValue = 500m }
        };

        _googleClient.GetCampaignsAsync(Arg.Any<CancellationToken>()).Returns(campaigns);
        _googleClient.GetAdGroupsAsync("123", Arg.Any<CancellationToken>()).Returns(adGroups);
        _googleClient.GetAdsAsync("456", Arg.Any<CancellationToken>()).Returns(ads);
        _googleClient.GetMetricsAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(metrics);

        _repository.LogSyncStartedAsync(Arg.Any<AdSyncLog>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repository.UpsertCampaignsAsync(Arg.Any<IEnumerable<AdCampaign>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repository.UpsertAdSetsAsync(Arg.Any<IEnumerable<AdAdSet>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repository.UpsertAdsAsync(Arg.Any<IEnumerable<Ad>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repository.UpsertDailyMetricsAsync(Arg.Any<IEnumerable<AdDailyMetric>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repository.UpdateSyncLogAsync(Arg.Any<AdSyncLog>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var handler = CreateHandler();
        var result = await handler.Handle(new SyncGoogleAdsRequest(), CancellationToken.None);

        result.Should().BeTrue();
        await _repository.Received(1).UpsertCampaignsAsync(Arg.Any<IEnumerable<AdCampaign>>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).UpsertDailyMetricsAsync(Arg.Any<IEnumerable<AdDailyMetric>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenClientThrows_LogsFailedSyncAndReturnsFalse()
    {
        _googleClient.GetCampaignsAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("gRPC error"));

        _repository.LogSyncStartedAsync(Arg.Any<AdSyncLog>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _repository.UpdateSyncLogAsync(Arg.Any<AdSyncLog>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var handler = CreateHandler();
        var result = await handler.Handle(new SyncGoogleAdsRequest(), CancellationToken.None);

        result.Should().BeFalse();
        await _repository.Received(1).UpdateSyncLogAsync(
            Arg.Is<AdSyncLog>(l => l.Status == "Failed"),
            Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run test — expect compilation failure**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "SyncGoogleAdsHandlerTests" -v minimal
```

Expected: `SyncGoogleAdsRequest` and `SyncGoogleAdsHandler` not found.

- [ ] **Step 3: Create `SyncGoogleAdsRequest.cs`**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Campaigns.UseCases.SyncGoogleAds;

public class SyncGoogleAdsRequest : IRequest<bool>
{
}
```

- [ ] **Step 4: Create `SyncGoogleAdsHandler.cs`**

```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Campaigns.UseCases.SyncGoogleAds;

public class SyncGoogleAdsHandler : IRequestHandler<SyncGoogleAdsRequest, bool>
{
    private readonly IGoogleAdsClient _googleClient;
    private readonly ICampaignRepository _repository;
    private readonly ILogger<SyncGoogleAdsHandler> _logger;

    public SyncGoogleAdsHandler(
        IGoogleAdsClient googleClient,
        ICampaignRepository repository,
        ILogger<SyncGoogleAdsHandler> logger)
    {
        _googleClient = googleClient;
        _repository = repository;
        _logger = logger;
    }

    public async Task<bool> Handle(SyncGoogleAdsRequest request, CancellationToken cancellationToken)
    {
        var syncLog = AdSyncLog.StartNew(AdPlatform.Google);
        await _repository.LogSyncStartedAsync(syncLog, cancellationToken);

        try
        {
            var to = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            var from = to.AddDays(-6);

            // 1. Sync campaigns
            var googleCampaigns = await _googleClient.GetCampaignsAsync(cancellationToken);
            var domainCampaigns = googleCampaigns.Select(c => new AdCampaign
            {
                Id = Guid.NewGuid(),
                Platform = AdPlatform.Google,
                PlatformCampaignId = c.Id,
                Name = c.Name,
                Status = c.Status,
                Objective = c.AdvertisingChannelType,
                DailyBudget = c.DailyBudgetMicros.HasValue ? c.DailyBudgetMicros.Value / 1_000_000m : null,
                Currency = "CZK", // Google uses account currency; adjust if needed
                CreatedAt = DateTime.UtcNow,
                SyncedAt = DateTime.UtcNow
            }).ToList();
            await _repository.UpsertCampaignsAsync(domainCampaigns, cancellationToken);

            // 2. Sync ad groups
            var allAdSets = new List<AdAdSet>();
            foreach (var campaign in googleCampaigns)
            {
                var adGroups = await _googleClient.GetAdGroupsAsync(campaign.Id, cancellationToken);
                var campaignId = domainCampaigns.First(c => c.PlatformCampaignId == campaign.Id).Id;

                allAdSets.AddRange(adGroups.Select(g => new AdAdSet
                {
                    Id = Guid.NewGuid(),
                    CampaignId = campaignId,
                    Platform = AdPlatform.Google,
                    PlatformAdSetId = g.Id,
                    Name = g.Name,
                    Status = g.Status
                }));
            }
            await _repository.UpsertAdSetsAsync(allAdSets, cancellationToken);

            // 3. Sync ads
            var allAds = new List<Ad>();
            foreach (var adSet in allAdSets)
            {
                var googleAds = await _googleClient.GetAdsAsync(adSet.PlatformAdSetId, cancellationToken);
                allAds.AddRange(googleAds.Select(a => new Ad
                {
                    Id = Guid.NewGuid(),
                    AdSetId = adSet.Id,
                    Platform = AdPlatform.Google,
                    PlatformAdId = a.Id,
                    Name = a.Name,
                    Status = a.Status
                }));
            }
            await _repository.UpsertAdsAsync(allAds, cancellationToken);

            // 4. Sync metrics (micros → currency: divide cost by 1,000,000)
            var googleMetrics = await _googleClient.GetMetricsAsync(from, to, cancellationToken);
            var domainMetrics = googleMetrics.Select(m =>
            {
                var adId = allAds.FirstOrDefault(a => a.PlatformAdId == m.AdId)?.Id ?? Guid.NewGuid();
                return AdDailyMetric.Compute(
                    Guid.NewGuid(), adId, m.Date,
                    m.Impressions, m.Clicks,
                    m.CostMicros / 1_000_000m,
                    m.Conversions, m.ConversionsValue);
            }).ToList();
            await _repository.UpsertDailyMetricsAsync(domainMetrics, cancellationToken);

            syncLog.MarkSucceeded(domainCampaigns.Count + allAdSets.Count + allAds.Count + domainMetrics.Count);
            await _repository.UpdateSyncLogAsync(syncLog, cancellationToken);

            _logger.LogInformation(
                "Google Ads sync completed: {Campaigns} campaigns, {AdGroups} ad groups, {Ads} ads, {Metrics} metric rows",
                domainCampaigns.Count, allAdSets.Count, allAds.Count, domainMetrics.Count);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google Ads sync failed");
            syncLog.MarkFailed(ex.Message);
            await _repository.UpdateSyncLogAsync(syncLog, cancellationToken);
            return false;
        }
    }
}
```

- [ ] **Step 5: Run tests — expect PASS**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "SyncGoogleAdsHandlerTests" -v minimal
```

Expected: 2 tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Campaigns/UseCases/SyncGoogleAds/
git add backend/test/Anela.Heblo.Tests/Campaigns/SyncGoogleAdsHandlerTests.cs
git commit -m "feat(campaigns): add SyncGoogleAdsHandler with cost-micros conversion and error handling"
```

---

### Task 4: Wire Google Adapter into Module and Background Sync

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Campaigns/CampaignsModule.cs`
- Modify: `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

- [ ] **Step 1: Update `CampaignsModule.cs` to add Google adapter**

Replace the file content:
```csharp
using Anela.Heblo.Adapters.GoogleAds;
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
        services.AddGoogleAdsAdapter(configuration);
        return services;
    }
}
```

- [ ] **Step 2: Reference GoogleAds adapter in API .csproj**

In `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`, add:
```xml
<ProjectReference Include="..\..\..\Adapters\Anela.Heblo.Adapters.GoogleAds\Anela.Heblo.Adapters.GoogleAds.csproj" />
```

- [ ] **Step 3: Add background sync for Google Ads**

In `appsettings.json`, add to `BackgroundRefresh` section:
```json
"SyncGoogleAds": {
  "InitialDelay": "00:05:15",
  "RefreshInterval": "24:00:00",
  "Enabled": true,
  "HydrationTier": 5,
  "Description": "Syncs Google Ads campaign data via GAQL (runs daily)"
}
```

Find where `SyncMetaAds` background task is registered and add next to it:
```csharp
registry.RegisterTask(
    "SyncGoogleAds",
    async (sp, ct) =>
    {
        var mediator = sp.GetRequiredService<IMediator>();
        await mediator.Send(new SyncGoogleAdsRequest(), ct);
    },
    configuration.GetSection("BackgroundRefresh:SyncGoogleAds").Get<RefreshTaskConfiguration>()
        ?? new RefreshTaskConfiguration());
```

- [ ] **Step 4: Full solution build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Run all campaign tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Campaigns" -v minimal
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Campaigns/CampaignsModule.cs
git add backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
git add backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat(campaigns): wire GoogleAds adapter into CampaignsModule and register daily background sync"
```
