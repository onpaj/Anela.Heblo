using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletGeneration;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.UseCases;

public class GetLeafletGenerationHandlerTests
{
    private readonly Mock<ILeafletGenerationRepository> _repoMock = new();

    private GetLeafletGenerationHandler CreateHandler() =>
        new(_repoMock.Object);

    [Fact]
    public async Task Handle_returns_not_found_error_code_and_default_fields_when_generation_missing()
    {
        // Arrange
        var requestedId = Guid.NewGuid();
        _repoMock
            .Setup(r => r.GetGenerationByIdAsync(requestedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletGeneration?)null);

        var handler = CreateHandler();
        var request = new GetLeafletGenerationRequest { Id = requestedId };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackNotFound);
        response.Id.Should().Be(Guid.Empty);
        response.Topic.Should().Be(string.Empty);
        response.Audience.Should().Be(string.Empty);
        response.Length.Should().Be(string.Empty);
        response.FinalMarkdown.Should().Be(string.Empty);
        response.KbSourceCount.Should().Be(0);
        response.LeafletSourceCount.Should().Be(0);
        response.DurationMs.Should().Be(0);
        response.CreatedAt.Should().Be(default(DateTimeOffset));
        response.UserId.Should().BeNull();
        response.PrecisionScore.Should().BeNull();
        response.StyleScore.Should().BeNull();
        response.FeedbackComment.Should().BeNull();
    }
}
