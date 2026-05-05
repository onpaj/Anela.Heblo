using Anela.Heblo.Application.Features.Leaflet.UseCases.SubmitLeafletFeedback;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet;

public class SubmitLeafletFeedbackHandlerTests
{
    private readonly Mock<ILeafletRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly SubmitLeafletFeedbackHandler _handler;

    private const string UserId = "user-123";

    public SubmitLeafletFeedbackHandlerTests()
    {
        _repositoryMock = new Mock<ILeafletRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _handler = new SubmitLeafletFeedbackHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: UserId, Name: "Test User", Email: "test@example.com", IsAuthenticated: true));
    }

    [Fact]
    public async Task Handle_WhenGenerationNotFound_ShouldReturnNotFoundError()
    {
        var generationId = Guid.NewGuid();
        _repositoryMock
            .Setup(x => x.GetGenerationByIdAsync(generationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletGeneration?)null);

        var request = new SubmitLeafletFeedbackRequest { GenerationId = generationId, PrecisionScore = 4, StyleScore = 3 };
        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackNotFound);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserDoesNotOwnGeneration_ShouldReturnForbiddenError()
    {
        var generationId = Guid.NewGuid();
        var generation = new LeafletGeneration
        {
            Id = generationId,
            Topic = "test",
            UserId = "other-user"
        };

        _repositoryMock
            .Setup(x => x.GetGenerationByIdAsync(generationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(generation);

        var request = new SubmitLeafletFeedbackRequest { GenerationId = generationId, PrecisionScore = 4, StyleScore = 3 };
        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenFeedbackAlreadySubmittedViaPrecisionScore_ShouldReturnConflictError()
    {
        var generationId = Guid.NewGuid();
        var generation = new LeafletGeneration
        {
            Id = generationId,
            Topic = "test",
            UserId = UserId,
            PrecisionScore = 4
        };

        _repositoryMock
            .Setup(x => x.GetGenerationByIdAsync(generationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(generation);

        var request = new SubmitLeafletFeedbackRequest { GenerationId = generationId, PrecisionScore = 4, StyleScore = 3 };
        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackAlreadySubmitted);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenFeedbackAlreadySubmittedViaStyleScore_ShouldReturnConflictError()
    {
        var generationId = Guid.NewGuid();
        var generation = new LeafletGeneration
        {
            Id = generationId,
            Topic = "test",
            UserId = UserId,
            StyleScore = 5
        };

        _repositoryMock
            .Setup(x => x.GetGenerationByIdAsync(generationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(generation);

        var request = new SubmitLeafletFeedbackRequest { GenerationId = generationId, PrecisionScore = 4, StyleScore = 3 };
        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackAlreadySubmitted);
    }

    [Fact]
    public async Task Handle_WhenValidRequest_ShouldSaveFeedbackAndReturnSuccess()
    {
        var generationId = Guid.NewGuid();
        var generation = new LeafletGeneration
        {
            Id = generationId,
            Topic = "test",
            UserId = UserId
        };

        _repositoryMock
            .Setup(x => x.GetGenerationByIdAsync(generationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(generation);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new SubmitLeafletFeedbackRequest
        {
            GenerationId = generationId,
            PrecisionScore = 4,
            StyleScore = 3,
            Comment = "Good leaflet"
        };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        generation.PrecisionScore.Should().Be(4);
        generation.StyleScore.Should().Be(3);
        generation.FeedbackComment.Should().Be("Good leaflet");
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenValidRequestWithNoComment_ShouldSaveFeedbackWithNullComment()
    {
        var generationId = Guid.NewGuid();
        var generation = new LeafletGeneration
        {
            Id = generationId,
            Topic = "test",
            UserId = UserId
        };

        _repositoryMock
            .Setup(x => x.GetGenerationByIdAsync(generationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(generation);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new SubmitLeafletFeedbackRequest { GenerationId = generationId, PrecisionScore = 5, StyleScore = 4 };

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        generation.PrecisionScore.Should().Be(5);
        generation.StyleScore.Should().Be(4);
        generation.FeedbackComment.Should().BeNull();
    }
}
