using Anela.Heblo.Application.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Campaigns;

public class SyncGoogleAdsHandlerTests
{
    private readonly Mock<IGoogleAdsClient> _googleAdsClientMock;
    private readonly Mock<ICampaignRepository> _repositoryMock;
    private readonly SyncGoogleAdsHandler _handler;

    public SyncGoogleAdsHandlerTests()
    {
        _googleAdsClientMock = new Mock<IGoogleAdsClient>();
        _repositoryMock = new Mock<ICampaignRepository>();
        _handler = new SyncGoogleAdsHandler(
            _googleAdsClientMock.Object,
            _repositoryMock.Object,
            NullLogger<SyncGoogleAdsHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WithOneCampaignOneAdGroupOneAdOneMetric_UpsertsAllEntities()
    {
        // Arrange
        var campaignDto = new GoogleCampaignDto
        {
            Id = "cmp-1",
            Name = "Campaign 1",
            Status = "ENABLED",
            Objective = "AWARENESS",
            DailyBudget = 100m,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = null,
        };

        var adGroupDto = new GoogleAdGroupDto
        {
            Id = "ag-1",
            CampaignId = "cmp-1",
            Name = "Ad Group 1",
            Status = "ENABLED",
            CpcBidMicros = 2_000_000m,
        };

        var adDto = new GoogleAdDto
        {
            Id = "ad-1",
            AdGroupId = "ag-1",
            Name = "Ad 1",
            Status = "ENABLED",
        };

        var metricDto = new GoogleMetricDto
        {
            AdId = "ad-1",
            Date = new DateTime(2026, 4, 15),
            Impressions = 1000,
            Clicks = 50,
            CostMicros = 5_000_000m,
            ConversionsValue = 200m,
            Conversions = 10,
        };

        _googleAdsClientMock
            .Setup(c => c.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleCampaignDto> { campaignDto });

        _googleAdsClientMock
            .Setup(c => c.GetAdGroupsAsync("cmp-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleAdGroupDto> { adGroupDto });

        _googleAdsClientMock
            .Setup(c => c.GetAdsAsync("ag-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleAdDto> { adDto });

        _googleAdsClientMock
            .Setup(c => c.GetMetricsAsync("ad-1", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleMetricDto> { metricDto });

        // Act
        await _handler.Handle(new SyncGoogleAdsRequest(), CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.UpsertCampaignAsync(
                It.Is<AdCampaign>(c => c.PlatformCampaignId == "cmp-1" && c.Name == "Campaign 1" && c.Platform == AdPlatform.Google),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _repositoryMock.Verify(
            r => r.UpsertAdSetAsync(
                It.Is<AdAdSet>(a => a.PlatformAdSetId == "ag-1" && a.Name == "Ad Group 1"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _repositoryMock.Verify(
            r => r.UpsertAdAsync(
                It.Is<Ad>(a => a.PlatformAdId == "ad-1" && a.Name == "Ad 1"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _repositoryMock.Verify(
            r => r.UpsertDailyMetricAsync(
                It.Is<AdDailyMetric>(m => m.Date == metricDto.Date && m.Impressions == 1000 && m.Clicks == 50),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithNoCampaigns_NoUpsertsCalled()
    {
        // Arrange
        _googleAdsClientMock
            .Setup(c => c.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleCampaignDto>());

        // Act
        await _handler.Handle(new SyncGoogleAdsRequest(), CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.UpsertCampaignAsync(It.IsAny<AdCampaign>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _repositoryMock.Verify(
            r => r.UpsertAdSetAsync(It.IsAny<AdAdSet>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _repositoryMock.Verify(
            r => r.UpsertAdAsync(It.IsAny<Ad>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _repositoryMock.Verify(
            r => r.UpsertDailyMetricAsync(It.IsAny<AdDailyMetric>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenGetCampaignsThrows_SyncLogMarkedAsFailed()
    {
        // Arrange
        var exceptionMessage = "API unavailable";

        _googleAdsClientMock
            .Setup(c => c.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(exceptionMessage));

        AdSyncLog? capturedLog = null;
        _repositoryMock
            .Setup(r => r.AddSyncLogAsync(It.IsAny<AdSyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<AdSyncLog, CancellationToken>((log, _) => capturedLog = log);

        // Act
        var act = () => _handler.Handle(new SyncGoogleAdsRequest(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        capturedLog.Should().NotBeNull();
        capturedLog!.Status.Should().Be(AdSyncStatus.Failed);
        capturedLog.ErrorMessage.Should().Contain(exceptionMessage);
    }

    [Fact]
    public async Task Handle_SyncLogHasGooglePlatform()
    {
        // Arrange
        _googleAdsClientMock
            .Setup(c => c.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleCampaignDto>());

        AdSyncLog? capturedLog = null;
        _repositoryMock
            .Setup(r => r.AddSyncLogAsync(It.IsAny<AdSyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<AdSyncLog, CancellationToken>((log, _) => capturedLog = log);

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
        // Arrange
        var campaignDto = new GoogleCampaignDto { Id = "cmp-1", Name = "Campaign 1" };
        var adGroupDto = new GoogleAdGroupDto { Id = "ag-1", CampaignId = "cmp-1", Name = "Ad Group 1" };
        var adDto = new GoogleAdDto { Id = "ad-1", AdGroupId = "ag-1", Name = "Ad 1" };
        var metricDto = new GoogleMetricDto
        {
            AdId = "ad-1",
            Date = new DateTime(2026, 4, 15),
            CostMicros = 12_345_678m,
        };

        _googleAdsClientMock
            .Setup(c => c.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleCampaignDto> { campaignDto });

        _googleAdsClientMock
            .Setup(c => c.GetAdGroupsAsync("cmp-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleAdGroupDto> { adGroupDto });

        _googleAdsClientMock
            .Setup(c => c.GetAdsAsync("ag-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleAdDto> { adDto });

        _googleAdsClientMock
            .Setup(c => c.GetMetricsAsync("ad-1", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoogleMetricDto> { metricDto });

        AdDailyMetric? capturedMetric = null;
        _repositoryMock
            .Setup(r => r.UpsertDailyMetricAsync(It.IsAny<AdDailyMetric>(), It.IsAny<CancellationToken>()))
            .Callback<AdDailyMetric, CancellationToken>((m, _) => capturedMetric = m);

        // Act
        await _handler.Handle(new SyncGoogleAdsRequest(), CancellationToken.None);

        // Assert
        capturedMetric.Should().NotBeNull();
        capturedMetric!.Spend.Should().Be(12.345678m);
    }
}
