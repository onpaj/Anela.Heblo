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
    private readonly Mock<ILeafletRepository> _repo = new();
    private readonly Mock<ICurrentUserService> _userService = new();
    private const string UserId = "user-123";

    private SubmitLeafletFeedbackHandler CreateHandler() =>
        new(_repo.Object, _userService.Object);

    public SubmitLeafletFeedbackHandlerTests()
    {
        _userService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(UserId, "Test User", "t@test.com", true));
    }

    private LeafletGeneration MakeGeneration(Guid? id = null, string? userId = null,
        int? precisionScore = null, int? styleScore = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Topic = "Vitamin C",
            UserId = userId ?? UserId,
            PrecisionScore = precisionScore,
            StyleScore = styleScore,
        };

    [Fact]
    public async Task Handle_WhenGenerationNotFound_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetGenerationByIdAsync(id, default)).ReturnsAsync((LeafletGeneration?)null);

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest { GenerationId = id, PrecisionScore = 4, StyleScore = 3 }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackNotFound);
        _repo.Verify(r => r.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task ReturnsForbidden_WhenGenerationUserIdIsNull()
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
        _repo.Verify(r => r.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserDoesNotOwnGeneration_ReturnsForbidden()
    {
        var generation = MakeGeneration(userId: "other-user");
        _repo.Setup(r => r.GetGenerationByIdAsync(generation.Id, default)).ReturnsAsync(generation);

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest { GenerationId = generation.Id, PrecisionScore = 4, StyleScore = 3 }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _repo.Verify(r => r.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenFeedbackAlreadySubmitted_ReturnsConflict()
    {
        var generation = MakeGeneration(precisionScore: 5);
        _repo.Setup(r => r.GetGenerationByIdAsync(generation.Id, default)).ReturnsAsync(generation);

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest { GenerationId = generation.Id, PrecisionScore = 4, StyleScore = 3 }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackAlreadySubmitted);
        _repo.Verify(r => r.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenStyleScoreAlreadySet_ReturnsConflict()
    {
        var generation = MakeGeneration(styleScore: 2);
        _repo.Setup(r => r.GetGenerationByIdAsync(generation.Id, default)).ReturnsAsync(generation);

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest { GenerationId = generation.Id, PrecisionScore = 4, StyleScore = 3 }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackAlreadySubmitted);
    }

    [Fact]
    public async Task Handle_ValidRequest_SavesFeedbackAndReturnsSuccess()
    {
        var generation = MakeGeneration();
        _repo.Setup(r => r.GetGenerationByIdAsync(generation.Id, default)).ReturnsAsync(generation);
        _repo.Setup(r => r.SaveChangesAsync(default)).Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest
            {
                GenerationId = generation.Id,
                PrecisionScore = 4,
                StyleScore = 5,
                Comment = "Very helpful",
            }, default);

        result.Success.Should().BeTrue();
        generation.PrecisionScore.Should().Be(4);
        generation.StyleScore.Should().Be(5);
        generation.FeedbackComment.Should().Be("Very helpful");
        _repo.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRequestWithNoComment_SavesNullComment()
    {
        var generation = MakeGeneration();
        _repo.Setup(r => r.GetGenerationByIdAsync(generation.Id, default)).ReturnsAsync(generation);
        _repo.Setup(r => r.SaveChangesAsync(default)).Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest { GenerationId = generation.Id, PrecisionScore = 3, StyleScore = 3 }, default);

        result.Success.Should().BeTrue();
        generation.FeedbackComment.Should().BeNull();
    }
}
