using System;
using System.Collections.Generic;
using System.Threading;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.UseCases.CreateMarketingAction;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Marketing;

public class CreateMarketingActionHandlerTests
{
    private readonly Mock<IMarketingActionRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<CreateMarketingActionHandler>> _loggerMock;
    private readonly CreateMarketingActionHandler _handler;

    public CreateMarketingActionHandlerTests()
    {
        _repositoryMock = new Mock<IMarketingActionRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<CreateMarketingActionHandler>>();
        _handler = new CreateMarketingActionHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ShouldReturnUnauthorizedError()
    {
        // Arrange
        var request = new CreateMarketingActionRequest
        {
            Title = "Summer Campaign",
            ActionType = MarketingActionType.Campaign,
            StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
        };

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: null, Name: null, Email: null, IsAuthenticated: false));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnauthorizedMarketingAccess);
        result.Params.Should().ContainKey("resource");
    }

    [Fact]
    public async Task Handle_WhenValidRequest_ShouldCreateActionSuccessfully()
    {
        // Arrange
        var request = new CreateMarketingActionRequest
        {
            Title = "Summer Campaign",
            ActionType = MarketingActionType.Event,
            StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            AssociatedProducts = new List<string> { "TON001" },
        };

        var currentUser = new CurrentUser(
            Id: "user123",
            Name: "Test User",
            Email: "test@example.com",
            IsAuthenticated: true);

        var createdAction = new MarketingAction
        {
            Id = 42,
            Title = request.Title,
            ActionType = request.ActionType,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUser.Id!,
        };

        _currentUserServiceMock.Setup(x => x.GetCurrentUser()).Returns(currentUser);
        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdAction);
        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Id.Should().Be(42);
        result.ErrorCode.Should().BeNull();

        _repositoryMock.Verify(x => x.AddAsync(
            It.Is<MarketingAction>(a =>
                a.Title == "Summer Campaign" &&
                a.ActionType == MarketingActionType.Event &&
                a.CreatedByUserId == "user123"),
            It.IsAny<CancellationToken>()), Times.Once);

        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
