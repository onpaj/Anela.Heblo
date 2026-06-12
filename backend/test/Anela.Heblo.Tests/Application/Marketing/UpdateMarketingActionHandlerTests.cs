using System.Net;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Features.Marketing.UseCases.UpdateMarketingAction;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Tests.Domain.Marketing;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.Marketing;

public class UpdateMarketingActionHandlerTests
{
    private readonly Mock<IMarketingActionRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly Mock<IOutlookCalendarSync> _outlookSync = new();
    private readonly Mock<ILogger<UpdateMarketingActionHandler>> _logger = new();

    private static readonly CurrentUser AuthenticatedUser =
        new("user-1", "Test User", "test@example.com", IsAuthenticated: true);

    private static MarketingAction BuildExistingAction(string? outlookEventId = "existing-event-id")
    {
        var action = new MarketingActionTestBuilder()
            .WithId(42)
            .WithTitle("Old Title")
            .WithActionType(MarketingActionType.Blog)
            .WithStartDate(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc))
            .WithCreatedAt(DateTime.UtcNow.AddDays(-1))
            .WithModifiedAt(DateTime.UtcNow.AddDays(-1))
            .WithCreatedBy("user-1")
            .WithOutlookEventId(outlookEventId)
            .WithOutlookSyncStatus(outlookEventId != null ? MarketingSyncStatus.Synced : MarketingSyncStatus.NotSynced)
            .Build();
        return action;
    }

    private static MarketingAction BuildExistingActionWithCollections()
    {
        var action = BuildExistingAction();
        action.AssociateWithProduct("OLD-PROD", DateTime.UtcNow);
        action.LinkToFolder("old-key", MarketingFolderType.General, DateTime.UtcNow);
        return action;
    }

    private static UpdateMarketingActionRequest BuildRequest(int id = 42) => new()
    {
        Id = id,
        Title = "New Title",
        ActionType = MarketingActionType.Newsletter,
        StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    public UpdateMarketingActionHandlerTests()
    {
        _currentUserService.Setup(x => x.GetCurrentUser()).Returns(AuthenticatedUser);
        _repository.Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildExistingAction());
        _repository.Setup(x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _outlookSync.Setup(x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _outlookSync.Setup(x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-event-id");
    }

    private UpdateMarketingActionHandler BuildHandler(bool pushEnabled = true) =>
        new(
            _repository.Object,
            _currentUserService.Object,
            _logger.Object,
            _outlookSync.Object,
            new TestOptionsMonitor<MarketingCalendarOptions>(
                new MarketingCalendarOptions { GroupId = "grp", PushEnabled = pushEnabled }));

    [Fact]
    public async Task Handle_CallsOutlookBeforeDb_WhenPushEnabled()
    {
        var callOrder = new List<string>();

        _outlookSync
            .Setup(x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .Callback<MarketingAction, CancellationToken>((_, _) => callOrder.Add("outlook"))
            .Returns(Task.CompletedTask);

        _repository
            .Setup(x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .Callback<MarketingAction, CancellationToken>((_, _) => callOrder.Add("db:update"))
            .Returns(Task.CompletedTask);

        _repository
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => callOrder.Add("db:save"))
            .ReturnsAsync(0);

        await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        callOrder.Should().ContainInOrder("outlook", "db:update", "db:save");
    }

    [Fact]
    public async Task Handle_ReturnsForbiddenError_WhenOutlookUpdateThrows403()
    {
        _outlookSync
            .Setup(x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutlookCalendarSyncException(HttpStatusCode.Forbidden, null, "403"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.MarketingCalendarAccessDenied);
        _repository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsSyncError_WhenOutlookUpdateThrowsNon403()
    {
        _outlookSync
            .Setup(x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutlookCalendarSyncException(HttpStatusCode.BadGateway, null, "502"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.MarketingCalendarSyncFailed);
        _repository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CreatesOutlookEvent_WhenActionHasNoEventId()
    {
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildExistingAction(outlookEventId: null));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        _outlookSync.Verify(
            x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _outlookSync.Verify(
            x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SkipsOutlook_WhenPushDisabled()
    {
        var result = await BuildHandler(pushEnabled: false).Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        _outlookSync.Verify(
            x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _outlookSync.Verify(
            x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenActionDoesNotExist()
    {
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarketingAction?)null);

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.MarketingActionNotFound);
    }

    [Fact]
    public async Task Handle_ReturnsUnauthorized_WhenUserIsNotAuthenticated()
    {
        _currentUserService
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(null, null, null, IsAuthenticated: false));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnauthorizedMarketingAccess);
        _repository.Verify(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UpdatesProductsAndFolderLinks_WhenProvided()
    {
        var request = BuildRequest();
        request.AssociatedProducts = new List<string> { "prod-1", "prod-2", "prod-1" };
        request.FolderLinks = new List<MarketingFolderLinkRequest>
        {
            new() { FolderKey = " key-1 ", FolderType = MarketingFolderType.General },
        };

        var result = await BuildHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repository.Verify(x => x.UpdateAsync(
            It.Is<MarketingAction>(a =>
                a.ProductAssociations.Count == 2 &&
                a.FolderLinks.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsDatabaseError_WhenDbSaveFails()
    {
        _repository
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB unavailable"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.DatabaseError);
        _outlookSync.Verify(
            x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _outlookSync.Verify(
            x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("may now be out of sync")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_HonorsRuntimePushEnabledFlip_TrueToFalse()
    {
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => BuildExistingAction());

        var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(
            new MarketingCalendarOptions { GroupId = "grp", PushEnabled = true });
        var handler = new UpdateMarketingActionHandler(
            _repository.Object,
            _currentUserService.Object,
            _logger.Object,
            _outlookSync.Object,
            monitor);

        await handler.Handle(BuildRequest(), CancellationToken.None);

        monitor.Set(new MarketingCalendarOptions { GroupId = "grp", PushEnabled = false });

        await handler.Handle(BuildRequest(), CancellationToken.None);

        _outlookSync.Verify(
            x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _outlookSync.Verify(
            x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_HonorsRuntimePushEnabledFlip_FalseToTrue()
    {
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => BuildExistingAction());

        var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(
            new MarketingCalendarOptions { GroupId = "grp", PushEnabled = false });
        var handler = new UpdateMarketingActionHandler(
            _repository.Object,
            _currentUserService.Object,
            _logger.Object,
            _outlookSync.Object,
            monitor);

        await handler.Handle(BuildRequest(), CancellationToken.None);

        monitor.Set(new MarketingCalendarOptions { GroupId = "grp", PushEnabled = true });

        await handler.Handle(BuildRequest(), CancellationToken.None);

        _outlookSync.Verify(
            x => x.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ClearsCollections_WhenRequestListsAreNull()
    {
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildExistingActionWithCollections());

        var request = BuildRequest();
        request.AssociatedProducts = null;
        request.FolderLinks = null;

        var result = await BuildHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repository.Verify(x => x.UpdateAsync(
            It.Is<MarketingAction>(a =>
                a.ProductAssociations.Count == 0 &&
                a.FolderLinks.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReplacesCollections_OnDeltaInput()
    {
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildExistingActionWithCollections());

        var request = BuildRequest();
        request.AssociatedProducts = new List<string> { "OLD-PROD", "NEW-PROD" };
        request.FolderLinks = new List<MarketingFolderLinkRequest>
        {
            new() { FolderKey = "old-key", FolderType = MarketingFolderType.General },
            new() { FolderKey = "new-key", FolderType = MarketingFolderType.Seasonal },
        };

        var result = await BuildHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repository.Verify(x => x.UpdateAsync(
            It.Is<MarketingAction>(a =>
                a.ProductAssociations.Count == 2 &&
                a.ProductAssociations.Any(p => p.ProductCodePrefix == "OLD-PROD") &&
                a.ProductAssociations.Any(p => p.ProductCodePrefix == "NEW-PROD") &&
                a.FolderLinks.Count == 2 &&
                a.FolderLinks.Any(f => f.FolderKey == "old-key" && f.FolderType == MarketingFolderType.General) &&
                a.FolderLinks.Any(f => f.FolderKey == "new-key" && f.FolderType == MarketingFolderType.Seasonal)),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
