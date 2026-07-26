using System.Net;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Features.Marketing.UseCases.MoveMarketingAction;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Tests.Domain.Marketing;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.Marketing;

public class MoveMarketingActionHandlerTests
{
    private readonly Mock<IMarketingActionRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly Mock<IOutlookCalendarSync> _outlookSync = new();
    private readonly Mock<ILogger<MoveMarketingActionHandler>> _logger = new();

    private static readonly CurrentUser AuthenticatedUser =
        new("user-1", "Test User", "test@example.com", IsAuthenticated: true);

    private static MarketingAction BuildExistingAction(string? outlookEventId = "existing-event-id")
    {
        var action = new MarketingActionTestBuilder()
            .WithId(42)
            .WithTitle("Old Title")
            .WithDescription("Old Description")
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

    private static MarketingAction BuildExistingActionWithCollections(string? outlookEventId = "existing-event-id")
    {
        var action = BuildExistingAction(outlookEventId);
        action.AssociateWithProduct("OLD-PROD", DateTime.UtcNow);
        action.AssociateWithProduct("OTHER-PROD", DateTime.UtcNow);
        action.LinkToFolder("old-key", MarketingFolderType.General, DateTime.UtcNow);
        action.LinkToFolder("other-key", MarketingFolderType.Seasonal, DateTime.UtcNow);
        return action;
    }

    private static MoveMarketingActionRequest BuildRequest(int id = 42) => new()
    {
        Id = id,
        StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        EndDate = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc),
    };

    public MoveMarketingActionHandlerTests()
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

    private MoveMarketingActionHandler BuildHandler(bool pushEnabled = true) =>
        new(
            _repository.Object,
            _currentUserService.Object,
            _logger.Object,
            _outlookSync.Object,
            new TestOptionsMonitor<MarketingCalendarOptions>(
                new MarketingCalendarOptions { GroupId = "grp", PushEnabled = pushEnabled }));

    [Fact]
    public async Task Handle_LeavesFolderLinksAndProductAssociationsUnchanged()
    {
        var existing = BuildExistingActionWithCollections();
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        _repository.Verify(x => x.UpdateAsync(
            It.Is<MarketingAction>(a =>
                a.ProductAssociations.Count == 2 &&
                a.ProductAssociations.Any(p => p.ProductCodePrefix == "OLD-PROD") &&
                a.ProductAssociations.Any(p => p.ProductCodePrefix == "OTHER-PROD") &&
                a.FolderLinks.Count == 2 &&
                a.FolderLinks.Any(f => f.FolderKey == "old-key" && f.FolderType == MarketingFolderType.General) &&
                a.FolderLinks.Any(f => f.FolderKey == "other-key" && f.FolderType == MarketingFolderType.Seasonal)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_LeavesTitleDescriptionActionTypeUnchanged()
    {
        var existing = BuildExistingActionWithCollections();
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        existing.Title.Should().Be("Old Title");
        existing.Description.Should().Be("Old Description");
        existing.ActionType.Should().Be(MarketingActionType.Blog);
    }

    [Fact]
    public async Task Handle_UpdatesStartAndEndDate()
    {
        var request = BuildRequest();

        var result = await BuildHandler().Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repository.Verify(x => x.UpdateAsync(
            It.Is<MarketingAction>(a =>
                a.StartDate == request.StartDate &&
                a.EndDate == request.EndDate),
            It.IsAny<CancellationToken>()), Times.Once);
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
        _repository.Verify(x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()), Times.Never);
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
    public async Task Handle_UpdatesOutlookEvent_WhenActionHasEventIdAndPushEnabled()
    {
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildExistingAction(outlookEventId: "existing-event-id"));

        var request = BuildRequest();
        var result = await BuildHandler(pushEnabled: true).Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        _outlookSync.Verify(
            x => x.UpdateEventAsync(
                It.Is<MarketingAction>(a => a.StartDate == request.StartDate),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _outlookSync.Verify(
            x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SkipsOutlookSync_WhenActionHasNoEventId()
    {
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildExistingAction(outlookEventId: null));

        var result = await BuildHandler(pushEnabled: true).Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        _outlookSync.Verify(
            x => x.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
    public async Task Handle_ReturnsDatabaseError_WhenDbSaveFails()
    {
        _repository
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB unavailable"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.DatabaseError);
    }
}
