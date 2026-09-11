using Anela.Heblo.Application.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.Campaigns;

public class SyncMetaAdsHandlerTests
{
    private readonly Mock<IMetaAdsClient> _metaAdsClient;
    private readonly Mock<ICampaignRepository> _repository;
    private readonly SyncMetaAdsHandler _handler;

    public SyncMetaAdsHandlerTests()
    {
        _metaAdsClient = new Mock<IMetaAdsClient>();
        _repository = new Mock<ICampaignRepository>();
        _handler = new SyncMetaAdsHandler(
            _metaAdsClient.Object,
            _repository.Object,
            NullLogger<SyncMetaAdsHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WithOneCampaignOneAdSetOneAdOneInsight_UpsertsAllEntities()
    {
        // Arrange
        var campaign = new MetaCampaignDto { Id = "camp1", Name = "Campaign 1", Status = "ACTIVE" };
        var adSet = new MetaAdSetDto { Id = "adset1", Name = "AdSet 1", Status = "ACTIVE" };
        var ad = new MetaAdDto { Id = "ad1", Name = "Ad 1", Status = "ACTIVE" };
        var insight = new MetaInsightDto
        {
            Date = DateTime.UtcNow.Date.AddDays(-1),
            Impressions = 1000,
            Clicks = 50,
            Spend = 25.50m,
            Revenue = 120m,
            Conversions = 5
        };

        _metaAdsClient.Setup(c => c.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MetaCampaignDto> { campaign });
        _metaAdsClient.Setup(c => c.GetAdSetsAsync("camp1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MetaAdSetDto> { adSet });
        _metaAdsClient.Setup(c => c.GetAdsAsync("adset1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MetaAdDto> { ad });
        _metaAdsClient.Setup(c => c.GetInsightsAsync("ad1", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MetaInsightDto> { insight });

        // Act
        await _handler.Handle(new SyncMetaAdsRequest(), CancellationToken.None);

        // Assert
        _repository.Verify(r => r.UpsertCampaignAsync(
            It.Is<AdCampaign>(c => c.PlatformCampaignId == "camp1" && c.Name == "Campaign 1"),
            It.IsAny<CancellationToken>()), Times.Once());

        _repository.Verify(r => r.UpsertAdSetAsync(
            It.Is<AdAdSet>(s => s.PlatformAdSetId == "adset1" && s.Name == "AdSet 1"),
            It.IsAny<CancellationToken>()), Times.Once());

        _repository.Verify(r => r.UpsertAdAsync(
            It.Is<Ad>(a => a.PlatformAdId == "ad1" && a.Name == "Ad 1"),
            It.IsAny<CancellationToken>()), Times.Once());

        _repository.Verify(r => r.UpsertDailyMetricAsync(
            It.Is<AdDailyMetric>(m => m.Impressions == 1000 && m.Clicks == 50 && m.Spend == 25.50m),
            It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_WithNoCampaigns_NoUpsertsCalled()
    {
        // Arrange
        _metaAdsClient.Setup(c => c.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MetaCampaignDto>());

        // Act
        await _handler.Handle(new SyncMetaAdsRequest(), CancellationToken.None);

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

        _metaAdsClient.Setup(c => c.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API unavailable"));

        // Act
        var act = async () => await _handler.Handle(new SyncMetaAdsRequest(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        capturedLog.Should().NotBeNull();
        capturedLog!.Status.Should().Be(AdSyncStatus.Failed);
        capturedLog.ErrorMessage.Should().Contain("API unavailable");
    }

    [Fact]
    public async Task Handle_SyncLogHasMetaPlatform()
    {
        // Arrange
        AdSyncLog? capturedLog = null;
        _repository
            .Setup(r => r.AddSyncLogAsync(It.IsAny<AdSyncLog>(), It.IsAny<CancellationToken>()))
            .Callback<AdSyncLog, CancellationToken>((log, _) => capturedLog = log)
            .Returns(Task.CompletedTask);

        _metaAdsClient.Setup(c => c.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MetaCampaignDto>());

        // Act
        await _handler.Handle(new SyncMetaAdsRequest(), CancellationToken.None);

        // Assert
        capturedLog.Should().NotBeNull();
        capturedLog!.Platform.Should().Be(AdPlatform.Meta);
    }

    [Fact]
    public async Task Handle_OnSuccess_SaveChangesCalledAtLeastTwice()
    {
        // Arrange
        _metaAdsClient.Setup(c => c.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MetaCampaignDto>());

        // Act
        await _handler.Handle(new SyncMetaAdsRequest(), CancellationToken.None);

        // Assert: initial save after adding sync log + final save after completion
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task Handle_WhenGetCampaignsThrows_SaveChangesCalledAfterFail()
    {
        // Arrange
        _metaAdsClient.Setup(c => c.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("oops"));

        // Act
        var act = async () => await _handler.Handle(new SyncMetaAdsRequest(), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Assert: SaveChanges called at least twice (initial after AddSyncLog + after Fail)
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }
}
