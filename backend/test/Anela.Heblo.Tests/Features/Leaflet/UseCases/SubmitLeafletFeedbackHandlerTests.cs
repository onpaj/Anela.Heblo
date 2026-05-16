using Anela.Heblo.Application.Features.Leaflet.UseCases.SubmitLeafletFeedback;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.UseCases;

public class SubmitLeafletFeedbackHandlerTests
{
    private readonly Mock<ILeafletGenerationRepository> _repo = new();
    private readonly Mock<ICurrentUserService> _userService = new();
    private const string UserId = "user-123";

    private SubmitLeafletFeedbackHandler CreateHandler() =>
        new(_repo.Object, _userService.Object);

    public SubmitLeafletFeedbackHandlerTests()
    {
        _userService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(UserId, "Test User", "t@test.com", true));
    }

    private static LeafletGeneration MakeGeneration(Guid? id = null, string? userId = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Topic = "Vitamin C",
            UserId = userId ?? UserId,
        };

    [Fact]
    public async Task Handle_WhenGenerationNotFound_ReturnsNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetGenerationByIdAsync(id, default)).ReturnsAsync((LeafletGeneration?)null);

        // Act
        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest { GenerationId = id, PrecisionScore = 4, StyleScore = 3 }, default);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackNotFound);
        _repo.Verify(r => r.UpdateFeedbackAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenGenerationUserIdIsNull_ReturnsForbidden()
    {
        // Arrange
        var generation = new LeafletGeneration { Id = Guid.NewGuid(), Topic = "Vitamin C", UserId = null };
        _repo.Setup(r => r.GetGenerationByIdAsync(generation.Id, default)).ReturnsAsync(generation);

        // Act
        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest { GenerationId = generation.Id, PrecisionScore = 4, StyleScore = 3 }, default);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _repo.Verify(r => r.UpdateFeedbackAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserDoesNotOwnGeneration_ReturnsForbidden()
    {
        // Arrange
        var generation = MakeGeneration(userId: "other-user");
        _repo.Setup(r => r.GetGenerationByIdAsync(generation.Id, default)).ReturnsAsync(generation);

        // Act
        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest { GenerationId = generation.Id, PrecisionScore = 4, StyleScore = 3 }, default);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _repo.Verify(r => r.UpdateFeedbackAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenRepoReturnsAlreadySubmitted_ReturnsConflict()
    {
        // Arrange
        var generation = MakeGeneration();
        _repo.Setup(r => r.GetGenerationByIdAsync(generation.Id, default)).ReturnsAsync(generation);
        _repo.Setup(r => r.UpdateFeedbackAsync(generation.Id, 4, 3, null, default))
            .ReturnsAsync(UpdateFeedbackResult.AlreadySubmitted);

        // Act
        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest { GenerationId = generation.Id, PrecisionScore = 4, StyleScore = 3 }, default);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackAlreadySubmitted);
    }

    [Fact]
    public async Task Handle_WhenRepoReturnsNotFound_ReturnsNotFound()
    {
        // Arrange
        var generation = MakeGeneration();
        _repo.Setup(r => r.GetGenerationByIdAsync(generation.Id, default)).ReturnsAsync(generation);
        _repo.Setup(r => r.UpdateFeedbackAsync(generation.Id, 4, 3, null, default))
            .ReturnsAsync(UpdateFeedbackResult.NotFound);

        // Act
        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest { GenerationId = generation.Id, PrecisionScore = 4, StyleScore = 3 }, default);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackNotFound);
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsUpdateFeedbackAndReturnsSuccess()
    {
        // Arrange
        var generation = MakeGeneration();
        _repo.Setup(r => r.GetGenerationByIdAsync(generation.Id, default)).ReturnsAsync(generation);
        _repo.Setup(r => r.UpdateFeedbackAsync(generation.Id, 4, 5, "Very helpful", default))
            .ReturnsAsync(UpdateFeedbackResult.Updated);

        // Act
        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest
            {
                GenerationId = generation.Id,
                PrecisionScore = 4,
                StyleScore = 5,
                Comment = "Very helpful",
            }, default);

        // Assert
        result.Success.Should().BeTrue();
        _repo.Verify(
            r => r.UpdateFeedbackAsync(generation.Id, 4, 5, "Very helpful", default),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRequestWithNoComment_CallsUpdateFeedbackWithNullComment()
    {
        // Arrange
        var generation = MakeGeneration();
        _repo.Setup(r => r.GetGenerationByIdAsync(generation.Id, default)).ReturnsAsync(generation);
        _repo.Setup(r => r.UpdateFeedbackAsync(generation.Id, 3, 3, null, default))
            .ReturnsAsync(UpdateFeedbackResult.Updated);

        // Act
        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest { GenerationId = generation.Id, PrecisionScore = 3, StyleScore = 3 }, default);

        // Assert
        result.Success.Should().BeTrue();
        _repo.Verify(
            r => r.UpdateFeedbackAsync(generation.Id, 3, 3, null, default),
            Times.Once);
    }
}
