using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.UseCases.DeleteJournalEntry;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Journal;

public class DeleteJournalEntryHandlerTests
{
    private readonly Mock<IJournalRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<DeleteJournalEntryHandler>> _loggerMock;
    private readonly DeleteJournalEntryHandler _handler;

    public DeleteJournalEntryHandlerTests()
    {
        _repositoryMock = new Mock<IJournalRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<DeleteJournalEntryHandler>>();
        _handler = new DeleteJournalEntryHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ShouldReturnUnauthorizedError()
    {
        // Arrange
        var request = new DeleteJournalEntryRequest { Id = 1 };

        var currentUser = new CurrentUser(
            Id: null,
            Name: null,
            Email: null,
            IsAuthenticated: false
        );

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(currentUser);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnauthorizedJournalAccess);
        result.Params.Should().ContainKey("resource");
        result.Params["resource"].Should().Be("journal_entry");

        // Verify no repository calls were made
        _repositoryMock.Verify(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(x => x.DeleteSoftAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsEmpty_ShouldReturnUnauthorizedError()
    {
        // Arrange
        var request = new DeleteJournalEntryRequest { Id = 1 };

        var currentUser = new CurrentUser(
            Id: string.Empty,
            Name: null,
            Email: null,
            IsAuthenticated: true
        );

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(currentUser);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnauthorizedJournalAccess);
    }

    [Fact]
    public async Task Handle_WhenEntryDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var entryId = 999;
        var request = new DeleteJournalEntryRequest { Id = entryId };

        var currentUser = new CurrentUser
        {
            IsAuthenticated = true,
            Id = "user123"
        };

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(currentUser);

        _repositoryMock
            .Setup(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JournalEntry)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.JournalEntryNotFound);
        result.Params.Should().ContainKey("entryId");
        result.Params["entryId"].Should().Be(entryId.ToString());

        // Verify repository calls
        _repositoryMock.Verify(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.DeleteSoftAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenValidRequest_ShouldDeleteEntrySuccessfully()
    {
        // Arrange
        var entryId = 1;
        var userId = "user123";
        var request = new DeleteJournalEntryRequest { Id = entryId };

        var currentUser = new CurrentUser(
            Id: userId,
            Name: "Test User",
            Email: "test@example.com",
            IsAuthenticated: true
        );

        var existingEntry = new JournalEntry
        {
            Id = entryId,
            Title = "Test Entry",
            Content = "Test content",
            CreatedByUserId = userId
        };

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(currentUser);

        _repositoryMock
            .Setup(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        _repositoryMock
            .Setup(x => x.DeleteSoftAsync(entryId, userId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Id.Should().Be(entryId);
        result.Message.Should().Be("Journal entry deleted successfully");

        // Verify repository calls
        _repositoryMock.Verify(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.DeleteSoftAsync(entryId, userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenDeletionSuccessful_ShouldLogInformation()
    {
        // Arrange
        var entryId = 1;
        var userId = "user123";
        var request = new DeleteJournalEntryRequest { Id = entryId };

        var currentUser = new CurrentUser(
            Id: userId,
            Name: "Test User",
            Email: "test@example.com",
            IsAuthenticated: true
        );

        var existingEntry = new JournalEntry
        {
            Id = entryId,
            Title = "Test Entry",
            Content = "Test content",
            CreatedByUserId = userId
        };

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(currentUser);

        _repositoryMock
            .Setup(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEntry);

        _repositoryMock
            .Setup(x => x.DeleteSoftAsync(entryId, userId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        // Verify logging occurred
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Journal entry {entryId} deleted by user {userId}")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}