using Anela.Heblo.Application.Features.Campaigns.GetCampaignDashboard;
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Application.Campaigns;

public class GetCampaignDashboardHandlerTests
{
    private readonly Mock<ICampaignRepository> _repositoryMock;
    private readonly GetCampaignDashboardHandler _handler;

    public GetCampaignDashboardHandlerTests()
    {
        _repositoryMock = new Mock<ICampaignRepository>();
        _handler = new GetCampaignDashboardHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsDashboardFromRepository()
    {
        // Arrange
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 1, 31);
        var request = new GetCampaignDashboardRequest(from, to, null);

        var expected = new CampaignDashboardDto
        {
            TotalSpend = 1000m,
            TotalConversions = 50,
            AvgRoas = 2.5m,
            AvgCpc = 1.2m,
            SpendOverTime =
            [
                new DailySpendDto { Date = new DateOnly(2026, 1, 1), MetaSpend = 600m, GoogleSpend = 400m }
            ]
        };

        _repositoryMock
            .Setup(r => r.GetDashboardAsync(from, to, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(expected);
        _repositoryMock.Verify(r => r.GetDashboardAsync(from, to, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ForwardsPlatformFilter_ToRepository()
    {
        // Arrange
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 1, 31);
        var request = new GetCampaignDashboardRequest(from, to, AdPlatform.Meta);

        var expected = new CampaignDashboardDto { TotalSpend = 500m };

        _repositoryMock
            .Setup(r => r.GetDashboardAsync(from, to, AdPlatform.Meta, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.TotalSpend.Should().Be(500m);
        _repositoryMock.Verify(r => r.GetDashboardAsync(from, to, AdPlatform.Meta, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsDashboardWithEmptySpendOverTime_WhenNoData()
    {
        // Arrange
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 1, 31);
        var request = new GetCampaignDashboardRequest(from, to, null);

        var expected = new CampaignDashboardDto
        {
            TotalSpend = 0m,
            TotalConversions = 0,
            AvgRoas = 0m,
            AvgCpc = 0m,
            SpendOverTime = []
        };

        _repositoryMock
            .Setup(r => r.GetDashboardAsync(from, to, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.SpendOverTime.Should().BeEmpty();
        result.TotalSpend.Should().Be(0m);
    }
}
