using System.Net;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Features.Marketing.UseCases.DeleteMarketingAction;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Tests.Domain.Marketing;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.Marketing;

public class DeleteMarketingActionHandlerTests
{
    private readonly Mock<IMarketingActionRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly Mock<IOutlookCalendarSync> _outlookSync = new();
    private readonly Mock<ILogger<DeleteMarketingActionHandler>> _logger = new();

    private static readonly CurrentUser AuthenticatedUser =
        new("user-1", "Test User", "test@example.com", IsAuthenticated: true);

    private static MarketingAction BuildExistingAction(string? outlookEventId = "event-abc")
    {
        var action = new MarketingActionTestBuilder()
            .WithId(7)
            .WithTitle("To Delete")
            .WithActionType(MarketingActionType.Blog)
            .WithStartDate(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc))
            .WithCreatedAt(DateTime.UtcNow.AddDays(-1))
            .WithModifiedAt(DateTime.UtcNow.AddDays(-1))
            .WithCreatedBy("user-1")
            .WithOutlookEventId(outlookEventId)
            .Build();
        return action;
    }

    private static DeleteMarketingActionRequest BuildRequest(int id = 7) => new() { Id = id };

    public DeleteMarketingActionHandlerTests()
    {
        _currentUserService.Setup(x => x.GetCurrentUser()).Returns(AuthenticatedUser);
        _repository.Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildExistingAction());
        _repository.Setup(x => x.DeleteSoftAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _outlookSync.Setup(x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private DeleteMarketingActionHandler BuildHandler(bool pushEnabled = true) =>
        new(
            _repository.Object,
            _currentUserService.Object,
            _logger.Object,
            _outlookSync.Object,
            new TestOptionsMonitor<MarketingCalendarOptions>(
                new MarketingCalendarOptions { GroupId = "grp", PushEnabled = pushEnabled }));

    [Fact]
    public async Task Handle_CallsOutlookDeleteBeforeSoftDelete_WhenPushEnabled()
    {
        var callOrder = new List<string>();

        _outlookSync
            .Setup(x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((_, _) => callOrder.Add("outlook"))
            .Returns(Task.CompletedTask);

        _repository
            .Setup(x => x.DeleteSoftAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<int, string, string, CancellationToken>((_, _, _, _) => callOrder.Add("db"))
            .Returns(Task.CompletedTask);

        await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        callOrder.Should().ContainInOrder("outlook", "db");
    }

    [Fact]
    public async Task Handle_TreatsOutlook404AsSuccess_AndProceedsWithSoftDelete()
    {
        _outlookSync
            .Setup(x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutlookCalendarSyncException(HttpStatusCode.NotFound, null, "404"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        _repository.Verify(x => x.DeleteSoftAsync(
            7, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsForbiddenError_WhenOutlookThrows403()
    {
        _outlookSync
            .Setup(x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutlookCalendarSyncException(HttpStatusCode.Forbidden, null, "403"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.MarketingCalendarAccessDenied);
        _repository.Verify(x => x.DeleteSoftAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsSyncError_WhenOutlookThrowsNon403Non404()
    {
        _outlookSync
            .Setup(x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OutlookCalendarSyncException(HttpStatusCode.ServiceUnavailable, null, "503"));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.MarketingCalendarSyncFailed);
        _repository.Verify(x => x.DeleteSoftAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SkipsOutlook_WhenActionHasNoEventId()
    {
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildExistingAction(outlookEventId: null));

        var result = await BuildHandler().Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        _outlookSync.Verify(
            x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repository.Verify(x => x.DeleteSoftAsync(
            7, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SkipsOutlook_WhenPushDisabled()
    {
        var result = await BuildHandler(pushEnabled: false).Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        _outlookSync.Verify(
            x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repository.Verify(x => x.DeleteSoftAsync(
            7, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task Handle_HonorsRuntimePushEnabledFlip_TrueToFalse()
    {
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => BuildExistingAction());

        var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(
            new MarketingCalendarOptions { GroupId = "grp", PushEnabled = true });
        var handler = new DeleteMarketingActionHandler(
            _repository.Object,
            _currentUserService.Object,
            _logger.Object,
            _outlookSync.Object,
            monitor);

        await handler.Handle(BuildRequest(), CancellationToken.None);

        monitor.Set(new MarketingCalendarOptions { GroupId = "grp", PushEnabled = false });

        await handler.Handle(BuildRequest(), CancellationToken.None);

        _outlookSync.Verify(
            x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_HonorsRuntimePushEnabledFlip_FalseToTrue()
    {
        _repository
            .Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => BuildExistingAction());

        var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(
            new MarketingCalendarOptions { GroupId = "grp", PushEnabled = false });
        var handler = new DeleteMarketingActionHandler(
            _repository.Object,
            _currentUserService.Object,
            _logger.Object,
            _outlookSync.Object,
            monitor);

        await handler.Handle(BuildRequest(), CancellationToken.None);

        monitor.Set(new MarketingCalendarOptions { GroupId = "grp", PushEnabled = true });

        await handler.Handle(BuildRequest(), CancellationToken.None);

        _outlookSync.Verify(
            x => x.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
