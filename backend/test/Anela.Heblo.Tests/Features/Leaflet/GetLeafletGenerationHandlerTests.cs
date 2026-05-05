using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletGeneration;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet;

public class GetLeafletGenerationHandlerTests
{
    private readonly Mock<ILeafletRepository> _repositoryMock;
    private readonly GetLeafletGenerationHandler _handler;

    public GetLeafletGenerationHandlerTests()
    {
        _repositoryMock = new Mock<ILeafletRepository>();
        _handler = new GetLeafletGenerationHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_WhenGenerationNotFound_ShouldReturnNotFoundError()
    {
        var id = Guid.NewGuid();
        _repositoryMock
            .Setup(x => x.GetGenerationByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletGeneration?)null);

        var request = new GetLeafletGenerationRequest { Id = id };
        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackNotFound);
    }

    [Fact]
    public async Task Handle_WhenGenerationFound_ShouldReturnFullPayload()
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var generation = new LeafletGeneration
        {
            Id = id,
            Topic = "Vitamin C serum",
            Audience = "EndConsumer",
            Length = "Long",
            FinalMarkdown = "# Serum\n\nContent.",
            KbSourceCount = 5,
            LeafletSourceCount = 3,
            DurationMs = 4567,
            CreatedAt = createdAt,
            UserId = "user-abc",
            PrecisionScore = 4,
            StyleScore = 5,
            FeedbackComment = "Excellent"
        };

        _repositoryMock
            .Setup(x => x.GetGenerationByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(generation);

        var request = new GetLeafletGenerationRequest { Id = id };
        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Id.Should().Be(id);
        result.Topic.Should().Be("Vitamin C serum");
        result.Audience.Should().Be("EndConsumer");
        result.FinalMarkdown.Should().Be("# Serum\n\nContent.");
        result.KbSourceCount.Should().Be(5);
        result.LeafletSourceCount.Should().Be(3);
        result.DurationMs.Should().Be(4567);
        result.CreatedAt.Should().Be(createdAt);
        result.UserId.Should().Be("user-abc");
        result.PrecisionScore.Should().Be(4);
        result.StyleScore.Should().Be(5);
        result.FeedbackComment.Should().Be("Excellent");
    }
}
