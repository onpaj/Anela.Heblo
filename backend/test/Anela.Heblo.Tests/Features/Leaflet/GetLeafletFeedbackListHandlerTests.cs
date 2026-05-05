using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletFeedbackList;
using Anela.Heblo.Domain.Features.Leaflet;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet;

public class GetLeafletFeedbackListHandlerTests
{
    private readonly Mock<ILeafletRepository> _repositoryMock;
    private readonly GetLeafletFeedbackListHandler _handler;

    public GetLeafletFeedbackListHandlerTests()
    {
        _repositoryMock = new Mock<ILeafletRepository>();
        _handler = new GetLeafletFeedbackListHandler(_repositoryMock.Object);

        _repositoryMock
            .Setup(x => x.GetGenerationStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeafletFeedbackStats
            {
                TotalGenerations = 10,
                TotalWithFeedback = 5,
                AvgPrecisionScore = 4.2,
                AvgStyleScore = 3.8
            });
    }

    private void SetupEmptyPagedResult()
    {
        _repositoryMock
            .Setup(x => x.GetGenerationsPagedAsync(
                It.IsAny<bool?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<LeafletGeneration>(), 0));
    }

    [Fact]
    public async Task Handle_WithInvalidPageSize_ShouldDefaultTo20()
    {
        SetupEmptyPagedResult();
        var request = new GetLeafletFeedbackListRequest { PageSize = 999 };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.PageSize.Should().Be(20);
        _repositoryMock.Verify(x => x.GetGenerationsPagedAsync(
            It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<int>(), 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithInvalidSortBy_ShouldDefaultToCreatedAt()
    {
        SetupEmptyPagedResult();
        var request = new GetLeafletFeedbackListRequest { SortBy = "InvalidColumn" };

        var result = await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(x => x.GetGenerationsPagedAsync(
            It.IsAny<bool?>(), It.IsAny<string?>(), "CreatedAt", It.IsAny<bool>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithZeroPageNumber_ShouldClampTo1()
    {
        SetupEmptyPagedResult();
        var request = new GetLeafletFeedbackListRequest { PageNumber = 0 };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.PageNumber.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithValidParams_ShouldReturnMappedLogsAndStats()
    {
        var generationId = Guid.NewGuid();
        var generations = new List<LeafletGeneration>
        {
            new()
            {
                Id = generationId,
                Topic = "Rose cream",
                Audience = "EndConsumer",
                Length = "Medium",
                KbSourceCount = 3,
                LeafletSourceCount = 2,
                DurationMs = 1234,
                CreatedAt = DateTimeOffset.UtcNow,
                UserId = "user-1",
                PrecisionScore = 4,
                StyleScore = 5,
                FeedbackComment = "Good"
            }
        };

        _repositoryMock
            .Setup(x => x.GetGenerationsPagedAsync(
                null, null, "CreatedAt", true, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((generations, 1));

        var request = new GetLeafletFeedbackListRequest();
        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.TotalCount.Should().Be(1);
        result.Logs.Should().HaveCount(1);
        result.Logs[0].Id.Should().Be(generationId);
        result.Logs[0].Topic.Should().Be("Rose cream");
        result.Logs[0].HasFeedback.Should().BeTrue();
        result.Stats.TotalGenerations.Should().Be(10);
        result.Stats.TotalWithFeedback.Should().Be(5);
        result.Stats.AvgPrecisionScore.Should().Be(4.2);
    }
}
