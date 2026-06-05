using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.UseCases.UpdateJournalEntry;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Journal;

public class UpdateJournalEntryHandlerTests
{
    private readonly Mock<IJournalRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<UpdateJournalEntryHandler>> _loggerMock;
    private readonly UpdateJournalEntryHandler _handler;

    public UpdateJournalEntryHandlerTests()
    {
        _repositoryMock = new Mock<IJournalRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<UpdateJournalEntryHandler>>();
        _handler = new UpdateJournalEntryHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);
    }

    private static UpdateJournalEntryRequest BuildRequest(int id = 1) => new()
    {
        Id = id,
        Title = "  New Title  ",
        Content = "  Updated body  ",
        EntryDate = new DateTime(2026, 6, 4, 14, 30, 0, DateTimeKind.Utc),
        AssociatedProducts = new List<string> { "AB-1" },
        TagIds = new List<int> { 7 }
    };

    private static JournalEntry BuildExistingEntry(int id = 1) => new()
    {
        Id = id,
        Title = "Old Title",
        Content = "Old body",
        EntryDate = new DateTime(2026, 6, 1),
        CreatedAt = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc),
        CreatedByUserId = "creator",
        CreatedByUsername = "Creator",
        ModifiedAt = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ReturnsUnauthorizedError()
    {
        // Arrange
        var request = BuildRequest();
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: null,
                Name: null,
                Email: null,
                IsAuthenticated: false));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnauthorizedJournalAccess);
        result.Params.Should().ContainKey("resource");
        result.Params["resource"].Should().Be("journal_entry");

        _repositoryMock.Verify(
            x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsEmpty_ReturnsUnauthorizedError()
    {
        // Arrange
        var request = BuildRequest();
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: string.Empty,
                Name: null,
                Email: null,
                IsAuthenticated: true));

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
    public async Task Handle_WhenEntryNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var entryId = 999;
        var request = BuildRequest(entryId);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: "user123",
                Name: "Test User",
                Email: "test@example.com",
                IsAuthenticated: true));

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

        _repositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenValidRequest_UpdatesEntryWithTrimmedFieldsAndAuditTrail()
    {
        // Arrange
        var entryId = 1;
        var userId = "user123";
        var request = BuildRequest(entryId);
        var existing = BuildExistingEntry(entryId);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: userId,
                Name: "Test User",
                Email: "test@example.com",
                IsAuthenticated: true));

        _repositoryMock
            .Setup(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var before = DateTime.UtcNow;

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert — response shape
        var after = DateTime.UtcNow;
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Id.Should().Be(entryId);
        result.ModifiedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);

        // Assert — entity mutations (the entity instance is the same one passed into UpdateAsync)
        existing.Title.Should().Be("New Title");
        existing.Content.Should().Be("Updated body");
        existing.EntryDate.Should().Be(new DateTime(2026, 6, 4));
        existing.ModifiedByUserId.Should().Be(userId);
        existing.ModifiedByUsername.Should().Be("Test User");
        existing.ModifiedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);

        // Creation audit untouched
        existing.CreatedByUserId.Should().Be("creator");
        existing.CreatedByUsername.Should().Be("Creator");

        // Collections were replaced
        existing.ProductAssociations.Select(p => p.ProductCodePrefix)
            .Should().BeEquivalentTo(new[] { "AB-1" });
        existing.TagAssignments.Select(t => t.TagId)
            .Should().BeEquivalentTo(new[] { 7 });

        _repositoryMock.Verify(
            x => x.UpdateAsync(existing, It.IsAny<CancellationToken>()),
            Times.Once);
        _repositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCurrentUserNameIsNull_FallsBackToUnknownUser()
    {
        // Arrange
        var entryId = 1;
        var request = BuildRequest(entryId);
        var existing = BuildExistingEntry(entryId);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: "user123",
                Name: null,
                Email: null,
                IsAuthenticated: true));

        _repositoryMock
            .Setup(x => x.GetByIdAsync(entryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        existing.ModifiedByUsername.Should().Be("Unknown User");
    }
}
