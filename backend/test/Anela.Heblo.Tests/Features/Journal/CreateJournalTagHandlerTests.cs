using Anela.Heblo.Application.Features.Journal.Contracts;
using Anela.Heblo.Application.Features.Journal.UseCases.CreateJournalTag;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Journal;

public class CreateJournalTagHandlerTests
{
    private readonly Mock<IJournalTagRepository> _tagRepositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<CreateJournalTagHandler>> _loggerMock;
    private readonly CreateJournalTagHandler _handler;

    public CreateJournalTagHandlerTests()
    {
        _tagRepositoryMock = new Mock<IJournalTagRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<CreateJournalTagHandler>>();
        _handler = new CreateJournalTagHandler(
            _tagRepositoryMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ShouldReturnUnauthorizedError()
    {
        // Arrange
        var request = new CreateJournalTagRequest
        {
            Name = "Urgent",
            Color = "#FF0000"
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
        result.Params!["resource"].Should().Be("journal_tag");

        _tagRepositoryMock.Verify(
            x => x.AddAsync(It.IsAny<JournalEntryTag>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _tagRepositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsEmpty_ShouldReturnUnauthorizedError()
    {
        // Arrange
        var request = new CreateJournalTagRequest
        {
            Name = "Urgent",
            Color = "#FF0000"
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
        result.Params.Should().ContainKey("resource");
        result.Params!["resource"].Should().Be("journal_tag");

        _tagRepositoryMock.Verify(
            x => x.AddAsync(It.IsAny<JournalEntryTag>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _tagRepositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenValidRequest_ShouldCreateJournalTagSuccessfully()
    {
        // Arrange
        var request = new CreateJournalTagRequest
        {
            Name = "  Urgent  ",
            Color = "#FF0000"
        };

        var currentUser = new CurrentUser(
            Id: "user123",
            Name: "Test User",
            Email: "test@example.com",
            IsAuthenticated: true
        );

        var createdTag = new JournalEntryTag
        {
            Id = 42,
            Name = "Urgent",
            Color = request.Color,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUser.Id
        };

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(currentUser);

        _tagRepositoryMock
            .Setup(x => x.AddAsync(It.IsAny<JournalEntryTag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdTag);

        _tagRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Id.Should().Be(createdTag.Id);
        result.Name.Should().Be(createdTag.Name);
        result.Color.Should().Be(createdTag.Color);

        _tagRepositoryMock.Verify(x => x.AddAsync(
            It.Is<JournalEntryTag>(t =>
                t.Name == "Urgent" &&
                t.CreatedByUserId == currentUser.Id &&
                t.Color == request.Color),
            It.IsAny<CancellationToken>()), Times.Once);

        _tagRepositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
