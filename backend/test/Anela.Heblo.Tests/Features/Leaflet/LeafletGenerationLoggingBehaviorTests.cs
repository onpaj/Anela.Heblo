using System.Diagnostics;
using Anela.Heblo.Application.Features.Leaflet.Pipeline;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet;

public class LeafletGenerationLoggingBehaviorTests
{
    private readonly Mock<ILeafletRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<LeafletGenerationLoggingBehavior>> _loggerMock;
    private readonly LeafletGenerationLoggingBehavior _behavior;

    private const string UserId = "user-123";

    public LeafletGenerationLoggingBehaviorTests()
    {
        _repositoryMock = new Mock<ILeafletRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<LeafletGenerationLoggingBehavior>>();

        _behavior = new LeafletGenerationLoggingBehavior(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: UserId, Name: "Test User", Email: "test@example.com", IsAuthenticated: true));
    }

    [Fact]
    public async Task Handle_WhenResponseSuccessful_ShouldSaveGenerationAndSetId()
    {
        // Arrange
        LeafletGeneration? savedGeneration = null;
        _repositoryMock
            .Setup(x => x.SaveGenerationAsync(It.IsAny<LeafletGeneration>(), It.IsAny<CancellationToken>()))
            .Callback<LeafletGeneration, CancellationToken>((g, _) => savedGeneration = g)
            .Returns(Task.CompletedTask);

        var request = new GenerateLeafletRequest
        {
            Topic = "Rose cream",
            Audience = AudienceType.EndConsumer,
            Length = LeafletLength.Medium
        };

        var response = new GenerateLeafletResponse
        {
            Content = "# Rose Cream\n\nExcellent moisturizer.",
            KbSourceCount = 3,
            LeafletSourceCount = 2
        };
        // response.Success is true by default (BaseResponse)

        RequestHandlerDelegate<GenerateLeafletResponse> next = () => Task.FromResult(response);

        // Act
        var result = await _behavior.Handle(request, next, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(response);
        result.Id.Should().NotBeNull();
        _repositoryMock.Verify(x => x.SaveGenerationAsync(It.IsAny<LeafletGeneration>(), CancellationToken.None), Times.Once);
        savedGeneration.Should().NotBeNull();
        savedGeneration!.Topic.Should().Be("Rose cream");
        savedGeneration.KbSourceCount.Should().Be(3);
        savedGeneration.LeafletSourceCount.Should().Be(2);
        savedGeneration.UserId.Should().Be(UserId);
    }

    [Fact]
    public async Task Handle_WhenResponseFails_ShouldNotSaveGeneration()
    {
        // Arrange
        var request = new GenerateLeafletRequest
        {
            Topic = "Topic",
            Audience = AudienceType.B2B,
            Length = LeafletLength.Short
        };
        var failedResponse = new GenerateLeafletResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.InternalServerError
        };
        RequestHandlerDelegate<GenerateLeafletResponse> next = () => Task.FromResult(failedResponse);

        // Act
        var result = await _behavior.Handle(request, next, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(failedResponse);
        _repositoryMock.Verify(x => x.SaveGenerationAsync(It.IsAny<LeafletGeneration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSaveFails_ShouldSwallowExceptionAndReturnResponse()
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.SaveGenerationAsync(It.IsAny<LeafletGeneration>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        var request = new GenerateLeafletRequest
        {
            Topic = "Topic",
            Audience = AudienceType.EndConsumer,
            Length = LeafletLength.Long
        };
        var response = new GenerateLeafletResponse { Content = "Content" };
        RequestHandlerDelegate<GenerateLeafletResponse> next = () => Task.FromResult(response);

        // Act
        var act = async () => await _behavior.Handle(request, next, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        response.Id.Should().BeNull(); // Id is not set because save failed
    }

    [Fact]
    public async Task Handle_WhenResponseSuccessful_ShouldCaptureAudienceAndLengthAsStrings()
    {
        // Arrange
        LeafletGeneration? savedGeneration = null;
        _repositoryMock
            .Setup(x => x.SaveGenerationAsync(It.IsAny<LeafletGeneration>(), It.IsAny<CancellationToken>()))
            .Callback<LeafletGeneration, CancellationToken>((g, _) => savedGeneration = g)
            .Returns(Task.CompletedTask);

        var request = new GenerateLeafletRequest
        {
            Topic = "Topic",
            Audience = AudienceType.B2B,
            Length = LeafletLength.Short
        };
        var response = new GenerateLeafletResponse { Content = "Content" };
        RequestHandlerDelegate<GenerateLeafletResponse> next = () => Task.FromResult(response);

        // Act
        await _behavior.Handle(request, next, CancellationToken.None);

        // Assert
        savedGeneration!.Audience.Should().Be("B2B");
        savedGeneration.Length.Should().Be("Short");
    }
}
