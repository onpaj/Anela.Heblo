using System;
using System.Collections.Generic;
using System.Threading;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Features.Marketing.UseCases.ImportFromOutlook;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Marketing;

public class ImportFromOutlookHandlerTests
{
    private readonly Mock<IMarketingActionRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<IOutlookCalendarSync> _outlookSyncMock;
    private readonly ImportFromOutlookHandler _handler;

    private static readonly CurrentUser AuthenticatedUser = new CurrentUser(
        Id: "user-import",
        Name: "Import User",
        Email: "import@example.com",
        IsAuthenticated: true);

    public ImportFromOutlookHandlerTests()
    {
        _repositoryMock = new Mock<IMarketingActionRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _outlookSyncMock = new Mock<IOutlookCalendarSync>();

        _currentUserServiceMock.Setup(x => x.GetCurrentUser()).Returns(AuthenticatedUser);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarketingAction a, CancellationToken _) =>
            {
                a.Id = 100;
                return a;
            });

        _repositoryMock
            .Setup(x => x.GetByOutlookEventIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction>());

        _handler = new ImportFromOutlookHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _outlookSyncMock.Object,
            NullLogger<ImportFromOutlookHandler>.Instance);
    }

    private static ImportFromOutlookRequest BuildRequest(bool dryRun = false)
    {
        return new ImportFromOutlookRequest
        {
            FromUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            DryRun = dryRun,
        };
    }

    private static OutlookEventDto BuildEvent(
        string id = "evt-1",
        string subject = "Test Event",
        string? category = null,
        string? bodyContent = null)
    {
        var startUtc = new DateTime(2026, 6, 10, 9, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc);

        return new OutlookEventDto
        {
            Id = id,
            Subject = subject,
            Body = bodyContent is not null
                ? new GraphEventBody { Content = bodyContent, ContentType = "html" }
                : null,
            Start = new GraphEventDateTime { DateTimeString = startUtc.ToString("O"), TimeZone = "UTC" },
            End = new GraphEventDateTime { DateTimeString = endUtc.ToString("O"), TimeZone = "UTC" },
            Categories = category is not null ? new[] { category } : Array.Empty<string>(),
        };
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ReturnsUnauthorizedError()
    {
        // Arrange
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(Id: null, Name: null, Email: null, IsAuthenticated: false));

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnauthorizedMarketingAccess);
        result.Params.Should().ContainKey("resource");
    }

    [Fact]
    public async Task Handle_WhenEventAlreadyImported_SkipsIt()
    {
        // Arrange
        var existingAction = new MarketingAction
        {
            Id = 1,
            OutlookEventId = "evt-existing",
            Title = "Old",
            StartDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "user-1",
        };

        _outlookSyncMock
            .Setup(s => s.ListEventsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutlookEventDto> { BuildEvent(id: "evt-existing") });

        _repositoryMock
            .Setup(x => x.GetByOutlookEventIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { existingAction });

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Skipped.Should().Be(1);
        result.Created.Should().Be(0);
        result.Items.Should().ContainSingle(i => i.Status == "Skipped" && i.OutlookEventId == "evt-existing");

        _repositoryMock.Verify(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenNewEvent_CreatesActionWithCorrectFields()
    {
        // Arrange
        var evt = BuildEvent(id: "evt-new", subject: "Summer Launch", category: "Launch");

        _outlookSyncMock
            .Setup(s => s.ListEventsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutlookEventDto> { evt });

        MarketingAction? capturedAction = null;
        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .Callback<MarketingAction, CancellationToken>((a, _) => capturedAction = a)
            .ReturnsAsync((MarketingAction a, CancellationToken _) => { a.Id = 99; return a; });

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Created.Should().Be(1);
        result.Items.Should().ContainSingle(i => i.Status == "Created" && i.OutlookEventId == "evt-new");

        capturedAction.Should().NotBeNull();
        capturedAction!.Title.Should().Be("Summer Launch");
        capturedAction.ActionType.Should().Be(MarketingActionType.Launch);
        capturedAction.OutlookEventId.Should().Be("evt-new");
        capturedAction.OutlookSyncStatus.Should().Be(MarketingSyncStatus.Synced);
        capturedAction.CreatedByUserId.Should().Be(AuthenticatedUser.Id);
    }

    [Fact]
    public async Task Handle_WhenCategoryIsUnknown_FallsBackToGeneral()
    {
        // Arrange
        var evt = BuildEvent(category: "SomethingUnrecognized");

        _outlookSyncMock
            .Setup(s => s.ListEventsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutlookEventDto> { evt });

        MarketingAction? capturedAction = null;
        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .Callback<MarketingAction, CancellationToken>((a, _) => capturedAction = a)
            .ReturnsAsync((MarketingAction a, CancellationToken _) => { a.Id = 1; return a; });

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        capturedAction!.ActionType.Should().Be(MarketingActionType.General);
    }

    [Fact]
    public async Task Handle_WhenBodyContainsHtml_StripsTagsInDescription()
    {
        // Arrange
        var html = "<html><body><p>Hello <strong>World</strong></p><script>alert('x')</script></body></html>";
        var evt = BuildEvent(bodyContent: html);

        _outlookSyncMock
            .Setup(s => s.ListEventsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutlookEventDto> { evt });

        MarketingAction? capturedAction = null;
        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .Callback<MarketingAction, CancellationToken>((a, _) => capturedAction = a)
            .ReturnsAsync((MarketingAction a, CancellationToken _) => { a.Id = 1; return a; });

        // Act
        await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        capturedAction!.Description.Should().NotContain("<");
        capturedAction.Description.Should().NotContain("alert");
        capturedAction.Description.Should().Contain("Hello");
        capturedAction.Description.Should().Contain("World");
    }

    [Fact]
    public async Task Handle_WhenDryRun_CountsCreatedButDoesNotPersist()
    {
        // Arrange
        var evt = BuildEvent(id: "evt-dry");

        _outlookSyncMock
            .Setup(s => s.ListEventsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutlookEventDto> { evt });

        // Act
        var result = await _handler.Handle(BuildRequest(dryRun: true), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Created.Should().Be(1);
        result.Items.Should().ContainSingle(i => i.Status == "Created" && i.CreatedActionId == null);

        _repositoryMock.Verify(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_IdempotencyOnRerun_AllSkippedOnSecondCall()
    {
        // Arrange
        var evt = BuildEvent(id: "evt-idem");

        _outlookSyncMock
            .Setup(s => s.ListEventsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutlookEventDto> { evt });

        // First call: event is new
        var firstResult = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Second call: repo returns the imported action
        var importedAction = new MarketingAction
        {
            Id = 100,
            OutlookEventId = "evt-idem",
            Title = "Test Event",
            StartDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            CreatedByUserId = "user-1",
        };

        _repositoryMock
            .Setup(x => x.GetByOutlookEventIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingAction> { importedAction });

        var secondResult = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        firstResult.Created.Should().Be(1);
        secondResult.Created.Should().Be(0);
        secondResult.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenOneEventFails_OtherEventsSucceedAndHandlerReturnsSuccess()
    {
        // Arrange
        var goodEvent = BuildEvent(id: "evt-good", subject: "Good Event");
        var badEvent = BuildEvent(id: "evt-bad", subject: "Bad Event");

        _outlookSyncMock
            .Setup(s => s.ListEventsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OutlookEventDto> { goodEvent, badEvent });

        var callCount = 0;
        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarketingAction a, CancellationToken _) =>
            {
                callCount++;
                if (a.Title == "Bad Event")
                    throw new InvalidOperationException("Simulated DB failure");
                a.Id = callCount;
                return a;
            });

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Created.Should().Be(1);
        result.Failed.Should().Be(1);
        result.Items.Should().ContainSingle(i => i.Status == "Created" && i.OutlookEventId == "evt-good");
        result.Items.Should().ContainSingle(i => i.Status == "Failed" && i.OutlookEventId == "evt-bad");
        result.Items.First(i => i.OutlookEventId == "evt-bad").Error.Should().NotBeNullOrEmpty();
    }
}
