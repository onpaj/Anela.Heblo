using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.UseCases.CreateJournalEntry;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Journal;

public class CreateJournalEntryHandlerTests
{
    private readonly Mock<IJournalRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<CreateJournalEntryHandler>> _loggerMock;
    private readonly CreateJournalEntryHandler _handler;

    public CreateJournalEntryHandlerTests()
    {
        _repositoryMock = new Mock<IJournalRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<CreateJournalEntryHandler>>();
        _handler = new CreateJournalEntryHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ShouldReturnUnauthorizedError()
    {
        // Arrange
        var request = new CreateJournalEntryRequest
        {
            Content = "Test content",
            EntryDate = DateTime.Today
        };

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
    }

    [Fact]
    public async Task Handle_WhenUserIdIsEmpty_ShouldReturnUnauthorizedError()
    {
        // Arrange
        var request = new CreateJournalEntryRequest
        {
            Content = "Test content",
            EntryDate = DateTime.Today
        };

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
    public async Task Handle_WhenValidRequest_ShouldCreateJournalEntrySuccessfully()
    {
        // Arrange
        var request = new CreateJournalEntryRequest
        {
            Title = "Test Title",
            Content = "Test content",
            EntryDate = DateTime.Today,
            AssociatedProducts = new List<string> { "TON001", "TON002" },
            TagIds = new List<int> { 1, 2 }
        };

        var currentUser = new CurrentUser(
            Id: "user123",
            Name: "Test User",
            Email: "test@example.com",
            IsAuthenticated: true
        );

        var createdEntry = new JournalEntry
        {
            Id = 1,
            Title = request.Title,
            Content = request.Content,
            EntryDate = request.EntryDate,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUser.Id
        };

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(currentUser);

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdEntry);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Id.Should().Be(1);
        result.CreatedAt.Should().Be(createdEntry.CreatedAt);
        result.ErrorCode.Should().BeNull();

        // Verify repository calls
        _repositoryMock.Verify(x => x.AddAsync(
            It.Is<JournalEntry>(e =>
                e.Title == request.Title &&
                e.Content == request.Content &&
                e.CreatedByUserId == currentUser.Id),
            It.IsAny<CancellationToken>()), Times.Once);

        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenValidRequestWithoutOptionalFields_ShouldCreateJournalEntrySuccessfully()
    {
        // Arrange
        var request = new CreateJournalEntryRequest
        {
            Content = "Test content",
            EntryDate = DateTime.Today
        };

        var currentUser = new CurrentUser(
            Id: "user123",
            Name: "Test User",
            Email: "test@example.com",
            IsAuthenticated: true
        );

        var createdEntry = new JournalEntry
        {
            Id = 1,
            Title = null,
            Content = request.Content,
            EntryDate = request.EntryDate,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUser.Id
        };

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(currentUser);

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdEntry);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Id.Should().Be(1);
        result.ErrorCode.Should().BeNull();
    }
}