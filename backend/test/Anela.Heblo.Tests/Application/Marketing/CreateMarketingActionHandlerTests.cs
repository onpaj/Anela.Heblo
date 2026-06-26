using System.Net;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Features.Marketing.UseCases.CreateMarketingAction;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.Marketing;

public class CreateMarketingActionHandlerTests
{
    private readonly Mock<IMarketingActionRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly Mock<IOutlookCalendarSync> _outlookSync = new();
    private readonly Mock<ILogger<CreateMarketingActionHandler>> _logger = new();

    private static readonly CurrentUser AuthenticatedUser =
        new("user-1", "Test User", "test@example.com", IsAuthenticated: true);

    public CreateMarketingActionHandlerTests()
    {
        _currentUserService.Setup(x => x.GetCurrentUser()).Returns(AuthenticatedUser);

        _repository
            .Setup(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarketingAction a, CancellationToken _) => a);

        _repository
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _outlookSync
            .Setup(x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("event-id-abc");
    }

    private CreateMarketingActionHandler BuildHandler(bool pushEnabled = true) =>
        new(
            _repository.Object,
            _currentUserService.Object,
            _logger.Object,
            _outlookSync.Object,
            new TestOptionsMonitor<MarketingCalendarOptions>(
                new MarketingCalendarOptions { GroupId = "grp", PushEnabled = pushEnabled }));

    private static CreateMarketingActionRequest BuildRequest() => new()
    {
        Title = "Test Action",
        ActionType = MarketingActionType.Blog,
        StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task Handle_CallsOutlookBeforeDb_WhenPushEnabled()
    {
        var callOrder = new List<string>();

        _outlookSync
            .Setup(x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .Callback<MarketingAction, CancellationToken>((_, _) => callOrder.Add("outlook"))
            .ReturnsAsync("event-id-abc");

        _repository
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => callOrder.Add("db"))
            .ReturnsAsync(0);

        await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        callOrder.Should().ContainInOrder("outlook", "db");
    }

    [Fact]
    public async Task Handle_ReturnsForbiddenError_WhenOutlookThrows403()
    {
        _outlookSync
            .Setup(x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutlookCalendarSyncException(HttpStatusCode.Forbidden, null, "403 Forbidden"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.MarketingCalendarAccessDenied);
        _repository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsSyncError_WhenOutlookThrowsNon403()
    {
        _outlookSync
            .Setup(x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutlookCalendarSyncException(HttpStatusCode.InternalServerError, null, "500"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.MarketingCalendarSyncFailed);
        _repository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CompensatesOutlookEvent_WhenDbSaveFails()
    {
        _outlookSync
            .Setup(x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("event-to-compensate");

        _repository
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB unavailable"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.DatabaseError);
        _outlookSync.Verify(
            x => x.DeleteEventAsync("event-to-compensate", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_SetsOutlookEventId_WhenBothSucceed()
    {
        MarketingAction? capturedAction = null;
        _repository
            .Setup(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .Callback<MarketingAction, CancellationToken>((a, _) => capturedAction = a)
            .ReturnsAsync((MarketingAction a, CancellationToken _) => a);

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedAction!.OutlookEventId.Should().Be("event-id-abc");
        capturedAction.OutlookSyncStatus.Should().Be(MarketingSyncStatus.Synced);
    }

    [Fact]
    public async Task Handle_SkipsOutlook_WhenPushDisabled()
    {
        var result = await BuildHandler(pushEnabled: false).Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        _outlookSync.Verify(
            x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsUnauthorized_WhenUserNotAuthenticated()
    {
        _currentUserService
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(null, null, null, IsAuthenticated: false));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnauthorizedMarketingAccess);
        _outlookSync.Verify(
            x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_PersistsFolderLinks_WhenProvided()
    {
        MarketingAction? capturedAction = null;
        _repository
            .Setup(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .Callback<MarketingAction, CancellationToken>((a, _) => capturedAction = a)
            .ReturnsAsync((MarketingAction a, CancellationToken _) => a);

        var request = BuildRequest();
        request.FolderLinks = new List<MarketingFolderLinkRequest>
        {
            new() { FolderKey = " key-1 ", FolderType = MarketingFolderType.General },
        };

        var result = await BuildHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedAction!.FolderLinks.Should().HaveCount(1);
        capturedAction.FolderLinks.Single().FolderKey.Should().Be("key-1");
        capturedAction.FolderLinks.Single().FolderType.Should().Be(MarketingFolderType.General);
    }

    [Fact]
    public async Task Handle_HonorsRuntimePushEnabledFlip_TrueToFalse()
    {
        var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(
            new MarketingCalendarOptions { GroupId = "grp", PushEnabled = true });
        var handler = new CreateMarketingActionHandler(
            _repository.Object,
            _currentUserService.Object,
            _logger.Object,
            _outlookSync.Object,
            monitor);

        await handler.Handle(BuildRequest(), CancellationToken.None);

        monitor.Set(new MarketingCalendarOptions { GroupId = "grp", PushEnabled = false });

        await handler.Handle(BuildRequest(), CancellationToken.None);

        _outlookSync.Verify(
            x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_HonorsRuntimePushEnabledFlip_FalseToTrue()
    {
        var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(
            new MarketingCalendarOptions { GroupId = "grp", PushEnabled = false });
        var handler = new CreateMarketingActionHandler(
            _repository.Object,
            _currentUserService.Object,
            _logger.Object,
            _outlookSync.Object,
            monitor);

        await handler.Handle(BuildRequest(), CancellationToken.None);

        monitor.Set(new MarketingCalendarOptions { GroupId = "grp", PushEnabled = true });

        await handler.Handle(BuildRequest(), CancellationToken.None);

        _outlookSync.Verify(
            x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
