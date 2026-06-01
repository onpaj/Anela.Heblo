# Google Ads Campaign Adapter (Issue #631) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `GoogleAdsClientWrapper` (implements `IGoogleAdsClient`) and `SyncGoogleAdsHandler` to sync Google Ads campaign hierarchy and daily metrics into the campaign database.

**Architecture:** Extend the existing `Anela.Heblo.Adapters.GoogleAds` project with `GoogleAdsClientWrapper` that executes four GAQL queries (campaigns, ad groups, ads, daily metrics) using the already-configured `Google.Ads.GoogleAds` v21.1.0 SDK. Mirror `SyncMetaAdsHandler` exactly: create a sync log, walk campaign→adgroup→ad→metrics, upsert every entity, mark success or fail. Register a recurring job at 5:15 AM daily (15 min after Meta).

**Tech Stack:** C# / .NET 8, `Google.Ads.GoogleAds` v21.1.0 SDK (GAQL / `SearchStream`), MediatR, Moq + FluentAssertions for tests.

---

## File Map

| Action | Path |
|--------|------|
| **Create** | `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsClientWrapper.cs` |
| **Modify** | `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs` |
| **Create** | `backend/src/Anela.Heblo.Application/Features/Campaigns/SyncGoogleAdsRequest.cs` |
| **Create** | `backend/src/Anela.Heblo.Application/Features/Campaigns/SyncGoogleAdsHandler.cs` |
| **Create** | `backend/src/Anela.Heblo.Application/Features/Campaigns/Infrastructure/Jobs/SyncGoogleAdsJob.cs` |
| **Create** | `backend/test/Anela.Heblo.Tests/Features/Campaigns/SyncGoogleAdsHandlerTests.cs` |

---

## Pre-flight: Create Worktree

- [ ] Create a git worktree branched off `feat/629-campaign-domain-persistence` (which contains `IGoogleAdsClient` and all campaign domain entities):

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git fetch origin feat/629-campaign-domain-persistence
git worktree add ../Anela.Heblo-feat-631 -b feat/631-google-ads-adapter origin/feat/629-campaign-domain-persistence
cd ../Anela.Heblo-feat-631
```

All subsequent steps run from `/Users/pajgrtondrej/Work/GitHub/Anela.Heblo-feat-631`.

---

## Task 1: Write failing tests for SyncGoogleAdsHandler

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Campaigns/SyncGoogleAdsHandlerTests.cs`

- [ ] **Step 1.1: Create the test file**

```csharp
// backend/test/Anela.Heblo.Tests/Features/Campaigns/SyncGoogleAdsHandlerTests.cs
using Anela.Heblo.Application.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.Campaigns;

public class SyncGoogleAdsHandlerTests
{
    private readonly Mock<IGoogleAdsClient> _googleAdsClient;
    private readonly Mock<ICampaignRepository> _repository;
    private readonly SyncGoogleAdsHandler _handler;

    public SyncGoogleAdsHandlerTests()
    {
        _googleAdsClient = new Mock<IGoogleAdsClient>();
        _repository = new Mock<ICampaignRepository>();
        _handler = new SyncGoogleAdsHandler(
            _googleAdsClient.Object,
            _repository.Object,
            NullLogger<SyncGoogleAdsHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WithOneCampaignOneAdGroupOneAdOneMetric_UpsertsAllEntities()
    {
        // Arrange
        var campaign = new GoogleCampaignDto { Id = "camp1", Name = "Campaign 1", Status = "ENABLED" };
        var adGroup = new GoogleAdGroupDto { Id = "ag1", CampaignId = "camp1", Name = "AdGroup 1", Status = "ENABLED" };
        var ad = new GoogleAdDto { Id = "ad1", AdGroupId = "ag1", Name = "Ad 1", Status = "ENABLED" };
        var metric = new GoogleMetricDto
        {
            AdId = "ad1",
            Date = DateTime.UtcNow.Date.AddDays(-1),
            Impressions = 2000,
            Clicks = 80,
            CostMicros = 15_000_000m,
            ConversionsValue = 300m,
            Conversions = 10,
        };

        _googleAdsClient.Setup(c => c.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleCampaignDto> { campaign });
        _googleAdsClient.Setup(c => c.GetAdGroupsAsync("camp1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleAdGroupDto> { adGroup });
        _googleAdsClient.Setup(c => c.GetAdsAsync("ag1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleAdDto> { ad });
        _googleAdsClient.Setup(c => c.GetMetricsAsync("ad1", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleMetricDto> { metric });

        // Act
        await _handler.Handle(new SyncGoogleAdsRequest(), CancellationToken.None);

        // Assert
        _repository.Verify(r => r.UpsertCampaignAsync(
            It.Is<AdCampaign>(c => c.PlatformCampaignId == "camp1" && c.Name == "Campaign 1" && c.Platform == AdPlatform.Google),
            It.IsAny<CancellationToken>()), Times.Once());

        _repository.Verify(r => r.UpsertAdSetAsync(
            It.Is<AdAdSet>(s => s.PlatformAdSetId == "ag1" && s.Name == "AdGroup 1"),
            It.IsAny<CancellationToken>()), Times.Once());

        _repository.Verify(r => r.UpsertAdAsync(
            It.Is<Ad>(a => a.PlatformAdId == "ad1" && a.Name == "Ad 1"),
            It.IsAny<CancellationToken>()), Times.Once());

        _repository.Verify(r => r.UpsertDailyMetricAsync(
            It.Is<AdDailyMetric>(m => m.Impressions == 2000 && m.Clicks == 80 && m.Spend == 15m),
            It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_WithNoCampaigns_NoUpsertsCalled()
    {
        // Arrange
        _googleAdsClient.Setup(c => c.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleCampaignDto>());

        // Act
        await _handler.Handle(new SyncGoogleAdsRequest(), CancellationToken.None);

        // Assert
        _repository.Verify(r => r.UpsertCampaignAsync(It.IsAny<AdCampaign>(), It.IsAny<CancellationToken>()), Times.Never());
        _repository.Verify(r => r.UpsertAdSetAsync(It.IsAny<AdAdSet>(), It.IsAny<CancellationToken>()), Times.Never());
        _repository.Verify(r => r.UpsertAdAsync(It.IsAny<Ad>(), It.IsAny<CancellationToken>()), Times.Never());
        _repository.Verify(r => r.UpsertDailyMetricAsync(It.IsAny<AdDailyMetric>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_WhenGetCampaignsThrows_SyncLogMarkedAsFailed()
    {
        // Arrange
        AdSyncLog? capturedLog = null;
        _repository
            .Setup(r => r.AddSyncLogAsync(It.IsAny<AdSyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<AdSyncLog, CancellationToken>((log, _) => capturedLog = log)
            .Returns(Task.CompletedTask);

        _googleAdsClient.Setup(c => c.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Google Ads API unavailable"));

        // Act
        var act = async () => await _handler.Handle(new SyncGoogleAdsRequest(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        capturedLog.Should().NotBeNull();
        capturedLog!.Status.Should().Be(AdSyncStatus.Failed);
        capturedLog.ErrorMessage.Should().Contain("Google Ads API unavailable");
    }

    [Fact]
    public async Task Handle_SyncLogHasGooglePlatform()
    {
        // Arrange
        AdSyncLog? capturedLog = null;
        _repository
            .Setup(r => r.AddSyncLogAsync(It.IsAny<AdSyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<AdSyncLog, CancellationToken>((log, _) => capturedLog = log)
            .Returns(Task.CompletedTask);

        _googleAdsClient.Setup(c => c.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleCampaignDto>());

        // Act
        await _handler.Handle(new SyncGoogleAdsRequest(), CancellationToken.None);

        // Assert
        capturedLog.Should().NotBeNull();
        capturedLog!.Platform.Should().Be(AdPlatform.Google);
        capturedLog.Status.Should().Be(AdSyncStatus.Success);
    }

    [Fact]
    public async Task Handle_MetricSpend_ConvertedFromMicros()
    {
        // Arrange - cost_micros = 12_345_678 → spend = 12.345678m
        var campaign = new GoogleCampaignDto { Id = "c1", Name = "C1", Status = "ENABLED" };
        var adGroup = new GoogleAdGroupDto { Id = "ag1", CampaignId = "c1", Name = "AG1", Status = "ENABLED" };
        var ad = new GoogleAdDto { Id = "a1", AdGroupId = "ag1", Name = "A1", Status = "ENABLED" };
        var metric = new GoogleMetricDto
        {
            AdId = "a1",
            Date = DateTime.UtcNow.Date,
            Impressions = 1,
            Clicks = 1,
            CostMicros = 12_345_678m,
            ConversionsValue = 0m,
            Conversions = 0,
        };

        _googleAdsClient.Setup(c => c.GetCampaignsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([campaign]);
        _googleAdsClient.Setup(c => c.GetAdGroupsAsync("c1", It.IsAny<CancellationToken>())).ReturnsAsync([adGroup]);
        _googleAdsClient.Setup(c => c.GetAdsAsync("ag1", It.IsAny<CancellationToken>())).ReturnsAsync([ad]);
        _googleAdsClient.Setup(c => c.GetMetricsAsync("a1", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([metric]);

        AdDailyMetric? captured = null;
        _repository
            .Setup(r => r.UpsertDailyMetricAsync(It.IsAny<AdDailyMetric>(), It.IsAny<CancellationToken>()))
            .Callback<AdDailyMetric, CancellationToken>((m, _) => captured = m)
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(new SyncGoogleAdsRequest(), CancellationToken.None);

        // Assert
        captured.Should().NotBeNull();
        captured!.Spend.Should().Be(12.345678m);
    }
}
```

- [ ] **Step 1.2: Verify tests fail (types not defined yet)**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SyncGoogleAdsHandlerTests" \
  --no-build 2>&1 | tail -10
```

Expected: Build error — `SyncGoogleAdsHandler`, `SyncGoogleAdsRequest`, `IGoogleAdsClient` not found.

---

## Task 2: Create SyncGoogleAdsRequest

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Campaigns/SyncGoogleAdsRequest.cs`

- [ ] **Step 2.1: Create the request**

```csharp
// backend/src/Anela.Heblo.Application/Features/Campaigns/SyncGoogleAdsRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.Campaigns;

public class SyncGoogleAdsRequest : IRequest
{
}
```

---

## Task 3: Create SyncGoogleAdsHandler

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Campaigns/SyncGoogleAdsHandler.cs`

- [ ] **Step 3.1: Create the handler (mirrors SyncMetaAdsHandler)**

```csharp
// backend/src/Anela.Heblo.Application/Features/Campaigns/SyncGoogleAdsHandler.cs
using Anela.Heblo.Domain.Features.Campaigns;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Campaigns;

public class SyncGoogleAdsHandler : IRequestHandler<SyncGoogleAdsRequest>
{
    private readonly IGoogleAdsClient _googleAdsClient;
    private readonly ICampaignRepository _repository;
    private readonly ILogger<SyncGoogleAdsHandler> _logger;

    public SyncGoogleAdsHandler(
        IGoogleAdsClient googleAdsClient,
        ICampaignRepository repository,
        ILogger<SyncGoogleAdsHandler> logger)
    {
        _googleAdsClient = googleAdsClient;
        _repository = repository;
        _logger = logger;
    }

    public async Task Handle(SyncGoogleAdsRequest request, CancellationToken cancellationToken)
    {
        var syncLog = new AdSyncLog
        {
            Id = Guid.NewGuid(),
            Platform = AdPlatform.Google,
            Status = AdSyncStatus.Running,
            StartedAt = DateTime.UtcNow,
        };

        await _repository.AddSyncLogAsync(syncLog, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        try
        {
            var since = DateTime.UtcNow.Date.AddDays(-7);
            var until = DateTime.UtcNow.Date;

            int campaignCount = 0, adGroupCount = 0, adCount = 0, metricCount = 0;

            var campaigns = await _googleAdsClient.GetCampaignsAsync(cancellationToken);
            _logger.LogInformation("GoogleAds sync: fetched {Count} campaigns", campaigns.Count);

            foreach (var campaignDto in campaigns)
            {
                var campaign = new AdCampaign
                {
                    Id = Guid.NewGuid(),
                    Platform = AdPlatform.Google,
                    PlatformCampaignId = campaignDto.Id,
                    Name = campaignDto.Name,
                    Status = campaignDto.Status,
                    Objective = campaignDto.Objective,
                    DailyBudget = campaignDto.DailyBudget,
                    LifetimeBudget = null,
                    StartDate = campaignDto.StartDate,
                    EndDate = campaignDto.EndDate,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };

                await _repository.UpsertCampaignAsync(campaign, cancellationToken);
                campaignCount++;

                var adGroups = await _googleAdsClient.GetAdGroupsAsync(campaignDto.Id, cancellationToken);

                foreach (var adGroupDto in adGroups)
                {
                    var adSet = new AdAdSet
                    {
                        Id = Guid.NewGuid(),
                        CampaignId = campaign.Id,
                        PlatformAdSetId = adGroupDto.Id,
                        Name = adGroupDto.Name,
                        Status = adGroupDto.Status,
                        DailyBudget = adGroupDto.CpcBidMicros.HasValue
                            ? adGroupDto.CpcBidMicros.Value / 1_000_000m
                            : null,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    };

                    await _repository.UpsertAdSetAsync(adSet, cancellationToken);
                    adGroupCount++;

                    var ads = await _googleAdsClient.GetAdsAsync(adGroupDto.Id, cancellationToken);

                    foreach (var adDto in ads)
                    {
                        var ad = new Ad
                        {
                            Id = Guid.NewGuid(),
                            AdSetId = adSet.Id,
                            PlatformAdId = adDto.Id,
                            Name = adDto.Name,
                            Status = adDto.Status,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow,
                        };

                        await _repository.UpsertAdAsync(ad, cancellationToken);
                        adCount++;

                        var metrics = await _googleAdsClient.GetMetricsAsync(adDto.Id, since, until, cancellationToken);

                        foreach (var metricDto in metrics)
                        {
                            var metric = new AdDailyMetric
                            {
                                Id = Guid.NewGuid(),
                                AdId = ad.Id,
                                Date = metricDto.Date,
                                Impressions = metricDto.Impressions,
                                Clicks = metricDto.Clicks,
                                Spend = metricDto.CostMicros / 1_000_000m,
                                Revenue = metricDto.ConversionsValue,
                                Conversions = metricDto.Conversions,
                                CreatedAt = DateTime.UtcNow,
                            };

                            await _repository.UpsertDailyMetricAsync(metric, cancellationToken);
                            metricCount++;
                        }
                    }
                }
            }

            await _repository.SaveChangesAsync(cancellationToken);

            syncLog.Complete(campaignCount, adGroupCount, adCount, metricCount);
            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "GoogleAds sync completed: {Campaigns} campaigns, {AdGroups} ad groups, {Ads} ads, {Metrics} metric rows",
                campaignCount, adGroupCount, adCount, metricCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GoogleAds sync failed");
            syncLog.Fail(ex.Message);
            await _repository.SaveChangesAsync(cancellationToken);
            throw;
        }
    }
}
```

- [ ] **Step 3.2: Run tests — they should now compile and pass**

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SyncGoogleAdsHandlerTests" 2>&1 | tail -20
```

Expected: All 5 tests pass.

- [ ] **Step 3.3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Campaigns/SyncGoogleAdsRequest.cs \
        backend/src/Anela.Heblo.Application/Features/Campaigns/SyncGoogleAdsHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Campaigns/SyncGoogleAdsHandlerTests.cs
git commit -m "feat(campaigns): add SyncGoogleAdsHandler + request + tests"
```

---

## Task 4: Create SyncGoogleAdsJob

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Campaigns/Infrastructure/Jobs/SyncGoogleAdsJob.cs`

- [ ] **Step 4.1: Create the job (mirrors SyncMetaAdsJob, 15 min offset)**

Check that the `Infrastructure/Jobs/` directory exists from the Meta adapter branch:

```bash
ls backend/src/Anela.Heblo.Application/Features/Campaigns/Infrastructure/Jobs/
```

Then create:

```csharp
// backend/src/Anela.Heblo.Application/Features/Campaigns/Infrastructure/Jobs/SyncGoogleAdsJob.cs
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Campaigns.Infrastructure.Jobs;

public class SyncGoogleAdsJob : IRecurringJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<SyncGoogleAdsJob> _logger;
    private readonly IRecurringJobStatusChecker _statusChecker;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "daily-google-ads-sync",
        DisplayName = "Daily Google Ads Sync",
        Description = "Syncs campaigns, ad groups, ads, and daily metrics from Google Ads",
        CronExpression = "15 5 * * *", // Daily at 5:15 AM (15 min after Meta at 5:00 AM)
        DefaultIsEnabled = true,
    };

    public SyncGoogleAdsJob(
        IMediator mediator,
        ILogger<SyncGoogleAdsJob> logger,
        IRecurringJobStatusChecker statusChecker)
    {
        _mediator = mediator;
        _logger = logger;
        _statusChecker = statusChecker;
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
            await _mediator.Send(new SyncGoogleAdsRequest(), cancellationToken);
            _logger.LogInformation("{JobName} completed successfully", Metadata.JobName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed", Metadata.JobName);
            throw;
        }
    }
}
```

- [ ] **Step 4.2: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Campaigns/Infrastructure/Jobs/SyncGoogleAdsJob.cs
git commit -m "feat(campaigns): add SyncGoogleAdsJob recurring job"
```

---

## Task 5: Create GoogleAdsClientWrapper

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsClientWrapper.cs`

- [ ] **Step 5.1: Create the wrapper**

The SDK usage pattern comes from `SdkAccountBudgetFetcher.cs`:
- Build `GoogleAdsConfig` from `GoogleAdsSettings`
- `new GoogleAdsClient(config).GetService(Services.V18.GoogleAdsService)`
- `service.SearchStream(customerId, gaql)` → `stream.GetResponseStream()` → `MoveNextAsync` loop

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsClientWrapper.cs
using Anela.Heblo.Domain.Features.Campaigns;
using Google.Ads.GoogleAds;
using Google.Ads.GoogleAds.Config;
using Google.Ads.GoogleAds.Lib;
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

    private (GoogleAdsService service, string customerId) CreateService()
    {
        var s = _settings.CurrentValue;
        var customerId = s.CustomerId.Replace("-", "");

        var config = new GoogleAdsConfig
        {
            DeveloperToken = s.DeveloperToken,
            OAuth2ClientId = s.OAuth2ClientId,
            OAuth2ClientSecret = s.OAuth2ClientSecret,
            OAuth2RefreshToken = s.OAuth2RefreshToken,
            LoginCustomerId = customerId,
        };

        var client = new GoogleAdsClient(config);
        var service = client.GetService(Services.V18.GoogleAdsService);

        return (service, customerId);
    }

    public async Task<IReadOnlyList<GoogleCampaignDto>> GetCampaignsAsync(CancellationToken ct = default)
    {
        var (service, customerId) = CreateService();

        const string query = """
            SELECT
                campaign.id,
                campaign.name,
                campaign.status,
                campaign.advertising_channel_type,
                campaign_budget.amount_micros,
                campaign.start_date,
                campaign.end_date
            FROM campaign
            WHERE campaign.status != 'REMOVED'
            """;

        _logger.LogDebug("GoogleAds: fetching campaigns for customer {CustomerId}", customerId);

        var results = new List<GoogleCampaignDto>();

        using var stream = service.SearchStream(customerId, query);
        var responseStream = stream.GetResponseStream();

        while (await responseStream.MoveNextAsync(ct))
        {
            foreach (var row in responseStream.Current.Results)
            {
                var c = row.Campaign;
                if (c is null)
                {
                    continue;
                }

                results.Add(new GoogleCampaignDto
                {
                    Id = c.Id.ToString(),
                    Name = c.Name ?? string.Empty,
                    Status = c.Status.ToString(),
                    Objective = c.AdvertisingChannelType.ToString(),
                    DailyBudget = row.CampaignBudget?.AmountMicros is long micros
                        ? micros / 1_000_000m
                        : null,
                    StartDate = ParseDate(c.StartDate),
                    EndDate = ParseDate(c.EndDate),
                });
            }
        }

        _logger.LogInformation("GoogleAds: fetched {Count} campaigns", results.Count);
        return results;
    }

    public async Task<IReadOnlyList<GoogleAdGroupDto>> GetAdGroupsAsync(string campaignId, CancellationToken ct = default)
    {
        var (service, customerId) = CreateService();

        var query = $"""
            SELECT
                ad_group.id,
                ad_group.name,
                ad_group.status,
                ad_group.cpc_bid_micros
            FROM ad_group
            WHERE campaign.id = {campaignId}
              AND ad_group.status != 'REMOVED'
            """;

        var results = new List<GoogleAdGroupDto>();

        using var stream = service.SearchStream(customerId, query);
        var responseStream = stream.GetResponseStream();

        while (await responseStream.MoveNextAsync(ct))
        {
            foreach (var row in responseStream.Current.Results)
            {
                var ag = row.AdGroup;
                if (ag is null)
                {
                    continue;
                }

                results.Add(new GoogleAdGroupDto
                {
                    Id = ag.Id.ToString(),
                    CampaignId = campaignId,
                    Name = ag.Name ?? string.Empty,
                    Status = ag.Status.ToString(),
                    CpcBidMicros = ag.CpcBidMicros,
                });
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<GoogleAdDto>> GetAdsAsync(string adGroupId, CancellationToken ct = default)
    {
        var (service, customerId) = CreateService();

        var query = $"""
            SELECT
                ad_group_ad.ad.id,
                ad_group_ad.ad.name,
                ad_group_ad.status
            FROM ad_group_ad
            WHERE ad_group.id = {adGroupId}
              AND ad_group_ad.status != 'REMOVED'
            """;

        var results = new List<GoogleAdDto>();

        using var stream = service.SearchStream(customerId, query);
        var responseStream = stream.GetResponseStream();

        while (await responseStream.MoveNextAsync(ct))
        {
            foreach (var row in responseStream.Current.Results)
            {
                var adGroupAd = row.AdGroupAd;
                if (adGroupAd?.Ad is null)
                {
                    continue;
                }

                results.Add(new GoogleAdDto
                {
                    Id = adGroupAd.Ad.Id.ToString(),
                    AdGroupId = adGroupId,
                    Name = adGroupAd.Ad.Name ?? string.Empty,
                    Status = adGroupAd.Status.ToString(),
                });
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<GoogleMetricDto>> GetMetricsAsync(
        string adId,
        DateTime since,
        DateTime until,
        CancellationToken ct = default)
    {
        var (service, customerId) = CreateService();

        var fromStr = since.ToString("yyyy-MM-dd");
        var toStr = until.ToString("yyyy-MM-dd");

        var query = $"""
            SELECT
                segments.date,
                metrics.impressions,
                metrics.clicks,
                metrics.cost_micros,
                metrics.conversions,
                metrics.conversions_value,
                ad_group_ad.ad.id
            FROM ad_group_ad
            WHERE ad_group_ad.ad.id = {adId}
              AND segments.date BETWEEN '{fromStr}' AND '{toStr}'
            """;

        var results = new List<GoogleMetricDto>();

        using var stream = service.SearchStream(customerId, query);
        var responseStream = stream.GetResponseStream();

        while (await responseStream.MoveNextAsync(ct))
        {
            foreach (var row in responseStream.Current.Results)
            {
                if (row.AdGroupAd?.Ad is null || row.Metrics is null || row.Segments is null)
                {
                    continue;
                }

                if (!DateOnly.TryParseExact(row.Segments.Date, "yyyy-MM-dd", out var dateOnly))
                {
                    continue;
                }

                results.Add(new GoogleMetricDto
                {
                    AdId = adId,
                    Date = dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    Impressions = row.Metrics.Impressions,
                    Clicks = row.Metrics.Clicks,
                    CostMicros = row.Metrics.CostMicros,
                    ConversionsValue = (decimal)row.Metrics.ConversionsValue,
                    Conversions = (long)row.Metrics.Conversions,
                });
            }
        }

        return results;
    }

    private static DateTime? ParseDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr))
        {
            return null;
        }

        return DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", out var d)
            ? d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : null;
    }
}
```

- [ ] **Step 5.2: Verify the build compiles**

```bash
cd backend
dotnet build src/Adapters/Anela.Heblo.Adapters.GoogleAds/Anela.Heblo.Adapters.GoogleAds.csproj 2>&1 | tail -15
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5.3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsClientWrapper.cs
git commit -m "feat(campaigns): add GoogleAdsClientWrapper implementing IGoogleAdsClient"
```

---

## Task 6: Wire up DI — register IGoogleAdsClient and SyncGoogleAdsJob

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs`

- [ ] **Step 6.1: Read the current file**

```bash
cat backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs
```

Current content (for reference):
```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.GoogleAds;

public static class GoogleAdsAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddGoogleAdsAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GoogleAdsSettings>(configuration.GetSection(GoogleAdsSettings.ConfigurationKey));
        services.AddScoped<IAccountBudgetFetcher, SdkAccountBudgetFetcher>();
        services.AddScoped<GoogleAdsTransactionSource>(sp =>
            new GoogleAdsTransactionSource(
                sp.GetRequiredService<IAccountBudgetFetcher>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GoogleAdsTransactionSource>>()));
        services.AddScoped<IRecurringJob, GoogleAdsInvoiceImportJob>();
        return services;
    }
}
```

- [ ] **Step 6.2: Add IGoogleAdsClient and SyncGoogleAdsJob registrations**

Replace the entire file content with:

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;
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
        services.Configure<GoogleAdsSettings>(configuration.GetSection(GoogleAdsSettings.ConfigurationKey));

        // Marketing invoice adapter (billing/account budgets)
        services.AddScoped<IAccountBudgetFetcher, SdkAccountBudgetFetcher>();
        services.AddScoped<GoogleAdsTransactionSource>(sp =>
            new GoogleAdsTransactionSource(
                sp.GetRequiredService<IAccountBudgetFetcher>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GoogleAdsTransactionSource>>()));
        services.AddScoped<IRecurringJob, GoogleAdsInvoiceImportJob>();

        // Campaign performance adapter
        services.AddScoped<IGoogleAdsClient, GoogleAdsClientWrapper>();
        services.AddScoped<IRecurringJob, Anela.Heblo.Application.Features.Campaigns.Infrastructure.Jobs.SyncGoogleAdsJob>();

        return services;
    }
}
```

- [ ] **Step 6.3: Verify the full solution builds**

```bash
cd backend
dotnet build 2>&1 | tail -20
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 6.4: Run all tests**

```bash
cd backend
dotnet test 2>&1 | tail -20
```

Expected: All tests pass. No failures.

- [ ] **Step 6.5: Run dotnet format**

```bash
cd backend
dotnet format 2>&1 | tail -10
```

Expected: No output (nothing to format) or a list of files formatted. Check `git diff` after to confirm no unintended changes.

- [ ] **Step 6.6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs
git commit -m "feat(campaigns): register IGoogleAdsClient and SyncGoogleAdsJob in DI"
```

---

## Task 7: Final push and PR

- [ ] **Step 7.1: Run full test suite one last time**

```bash
cd backend
dotnet test 2>&1 | grep -E "passed|failed|skipped|Error"
```

Expected: All tests pass, 0 failed.

- [ ] **Step 7.2: Push branch**

```bash
git push -u origin feat/631-google-ads-adapter
```

- [ ] **Step 7.3: Create PR targeting the integration branch**

```bash
gh pr create \
  --base feature/google_and_fb_data_integration \
  --head feat/631-google-ads-adapter \
  --title "feat(campaigns): Google Ads adapter + SyncGoogleAds handler (#631)" \
  --body "$(cat <<'EOF'
Part of epic #628. Depends on #629.

## Changes

- **`GoogleAdsClientWrapper`** — implements `IGoogleAdsClient` using the official `Google.Ads.GoogleAds` v21.1.0 SDK with four GAQL queries (campaigns, ad groups, ads, daily metrics)
- **`SyncGoogleAdsHandler`** — MediatR handler that walks campaign→adgroup→ad→metrics, upserts all entities via `ICampaignRepository`, marks sync log success/fail
- **`SyncGoogleAdsJob`** — `IRecurringJob` scheduled at `15 5 * * *` (5:15 AM daily, 15 min after Meta) with enable/disable support
- **DI registration** — `IGoogleAdsClient` and `SyncGoogleAdsJob` added to `AddGoogleAdsAdapter()`

## Test plan

- [x] `SyncGoogleAdsHandlerTests` — 5 unit tests covering happy path, empty campaigns, exception → sync log failed, platform = Google, micros → CZK conversion
- [x] `dotnet test` passes (all tests green)
- [x] `dotnet format` passes (no formatting violations)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 7.4: Remove the worktree**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git worktree remove ../Anela.Heblo-feat-631
```

---

## Self-Review

### Spec Coverage

| Requirement | Covered By |
|---|---|
| `GoogleAdsClientWrapper` implementing `IGoogleAdsClient` | Task 5 |
| GAQL campaigns query | Task 5 — `GetCampaignsAsync` |
| GAQL ad groups query | Task 5 — `GetAdGroupsAsync` |
| GAQL ads query | Task 5 — `GetAdsAsync` |
| GAQL metrics query (date range) | Task 5 — `GetMetricsAsync` |
| `cost_micros / 1_000_000` conversion | Task 3 handler (metric.CostMicros / 1_000_000m), Task 5 stores raw micros in DTO |
| `SyncGoogleAdsHandler` mirrors `SyncMetaAdsHandler` | Task 3 |
| Sync log Platform = Google | Task 3, tested in Task 1 |
| Sync log Fail on exception | Task 3, tested in Task 1 |
| Background task `15 5 * * *`, `DefaultIsEnabled = true` | Task 4 |
| DI registration | Task 6 |
| Tests: happy path + error path | Task 1 |

### No Placeholders ✓

All steps have complete code. No TBD/TODO.

### Type Consistency ✓

- `GoogleCampaignDto`, `GoogleAdGroupDto`, `GoogleAdDto`, `GoogleMetricDto` — defined in `IGoogleAdsClient.cs` (domain branch), used identically in Tasks 3 and 5
- `AdPlatform.Google` — from `AdPlatform.cs` (domain branch)
- `IRecurringJob`, `RecurringJobMetadata` — from `Anela.Heblo.Domain.Features.BackgroundJobs`
- `IGoogleAdsClient` — registered in Task 6, injected in Task 3
