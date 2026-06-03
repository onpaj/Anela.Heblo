using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Application.Features.Marketing.UseCases.CreateMarketingAction;
using Anela.Heblo.Application.Features.Marketing.UseCases.DeleteMarketingAction;
using Anela.Heblo.Application.Features.Marketing.UseCases.UpdateMarketingAction;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Marketing;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Tests.Domain.Marketing;
using Anela.Heblo.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Marketing
{
    public class MarketingActionHandlerSyncTests
    {
        private readonly Mock<IMarketingActionRepository> _repositoryMock;
        private readonly Mock<ICurrentUserService> _currentUserServiceMock;
        private readonly Mock<IOutlookCalendarSync> _outlookSyncMock;

        private static readonly CurrentUser AuthenticatedUser = new CurrentUser(
            Id: "user-1",
            Name: "Test User",
            Email: "test@example.com",
            IsAuthenticated: true);

        public MarketingActionHandlerSyncTests()
        {
            _repositoryMock = new Mock<IMarketingActionRepository>();
            _currentUserServiceMock = new Mock<ICurrentUserService>();
            _outlookSyncMock = new Mock<IOutlookCalendarSync>(MockBehavior.Strict);

            _currentUserServiceMock.Setup(x => x.GetCurrentUser()).Returns(AuthenticatedUser);

            _repositoryMock
                .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            _repositoryMock
                .Setup(x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        private CreateMarketingActionHandler BuildCreateHandler(bool pushEnabled)
        {
            var options = new MarketingCalendarOptions { PushEnabled = pushEnabled, GroupId = "cal@example.com" };
            var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(options);

            return new CreateMarketingActionHandler(
                _repositoryMock.Object,
                _currentUserServiceMock.Object,
                NullLogger<CreateMarketingActionHandler>.Instance,
                _outlookSyncMock.Object,
                monitor);
        }

        private UpdateMarketingActionHandler BuildUpdateHandler(bool pushEnabled)
        {
            var options = new MarketingCalendarOptions { PushEnabled = pushEnabled, GroupId = "cal@example.com" };
            var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(options);

            return new UpdateMarketingActionHandler(
                _repositoryMock.Object,
                _currentUserServiceMock.Object,
                NullLogger<UpdateMarketingActionHandler>.Instance,
                _outlookSyncMock.Object,
                monitor);
        }

        private DeleteMarketingActionHandler BuildDeleteHandler(bool pushEnabled)
        {
            var options = new MarketingCalendarOptions { PushEnabled = pushEnabled, GroupId = "cal@example.com" };
            var monitor = new TestOptionsMonitor<MarketingCalendarOptions>(options);

            return new DeleteMarketingActionHandler(
                _repositoryMock.Object,
                _currentUserServiceMock.Object,
                NullLogger<DeleteMarketingActionHandler>.Instance,
                _outlookSyncMock.Object,
                monitor);
        }

        private static MarketingAction BuildAction(string? outlookEventId = null) =>
            new MarketingActionTestBuilder()
                .WithId(42)
                .WithTitle("Test Action")
                .WithActionType(MarketingActionType.PR)
                .WithStartDate(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc))
                .WithEndDate(new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc))
                .WithCreatedAt(DateTime.UtcNow)
                .WithModifiedAt(DateTime.UtcNow)
                .WithCreatedBy("user-1")
                .WithOutlookEventId(outlookEventId)
                .Build();

        private static CreateMarketingActionRequest BuildCreateRequest()
        {
            return new CreateMarketingActionRequest
            {
                Title = "Test Action",
                ActionType = MarketingActionType.PR,
                StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            };
        }

        private static UpdateMarketingActionRequest BuildUpdateRequest(int id = 42)
        {
            return new UpdateMarketingActionRequest
            {
                Id = id,
                Title = "Updated Action",
                ActionType = MarketingActionType.PR,
                StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            };
        }

        // ─── Create handler ───────────────────────────────────────────────────────

        [Fact]
        public async Task CreateHandler_CallsOutlookSync_WhenPushEnabled()
        {
            // Arrange
            _repositoryMock
                .Setup(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((MarketingAction a, CancellationToken _) => a);

            _outlookSyncMock
                .Setup(s => s.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("evt-123");

            var handler = BuildCreateHandler(pushEnabled: true);

            // Act
            var result = await handler.Handle(BuildCreateRequest(), CancellationToken.None);

            // Assert — Outlook sync called before DB save; action is marked Synced
            result.Success.Should().BeTrue();
            _outlookSyncMock.Verify(
                s => s.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task CreateHandler_ReturnsError_WhenOutlookThrows()
        {
            // Arrange
            _outlookSyncMock
                .Setup(s => s.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OutlookCalendarSyncException(HttpStatusCode.InternalServerError, null, "Graph API error"));

            var handler = BuildCreateHandler(pushEnabled: true);

            // Act
            var result = await handler.Handle(BuildCreateRequest(), CancellationToken.None);

            // Assert — fail-fast: handler returns an error response without saving to DB
            result.Success.Should().BeFalse();
            _repositoryMock.Verify(
                x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task CreateHandler_SkipsOutlookSync_WhenPushDisabled()
        {
            // Arrange
            var createdAction = BuildAction();
            _repositoryMock
                .Setup(x => x.AddAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(createdAction);

            var handler = BuildCreateHandler(pushEnabled: false);

            // Act
            var result = await handler.Handle(BuildCreateRequest(), CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            _outlookSyncMock.VerifyNoOtherCalls();
        }

        // ─── Update handler ───────────────────────────────────────────────────────

        [Fact]
        public async Task UpdateHandler_CallsUpdateEvent_WhenOutlookEventIdExists()
        {
            // Arrange
            var existingAction = BuildAction(outlookEventId: "evt-existing");
            _repositoryMock
                .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingAction);

            _outlookSyncMock
                .Setup(s => s.UpdateEventAsync(existingAction, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var handler = BuildUpdateHandler(pushEnabled: true);

            // Act
            var result = await handler.Handle(BuildUpdateRequest(), CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            _outlookSyncMock.Verify(
                s => s.UpdateEventAsync(existingAction, It.IsAny<CancellationToken>()),
                Times.Once);
            _outlookSyncMock.VerifyNoOtherCalls();
            existingAction.OutlookSyncStatus.Should().Be(MarketingSyncStatus.Synced);
        }

        [Fact]
        public async Task UpdateHandler_CallsCreateEvent_WhenOutlookEventIdMissing()
        {
            // Arrange
            var existingAction = BuildAction(outlookEventId: null);
            _repositoryMock
                .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingAction);

            _outlookSyncMock
                .Setup(s => s.CreateEventAsync(existingAction, It.IsAny<CancellationToken>()))
                .ReturnsAsync("evt-new");

            var handler = BuildUpdateHandler(pushEnabled: true);

            // Act
            var result = await handler.Handle(BuildUpdateRequest(), CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            _outlookSyncMock.Verify(
                s => s.CreateEventAsync(existingAction, It.IsAny<CancellationToken>()),
                Times.Once);
            _outlookSyncMock.VerifyNoOtherCalls();
            existingAction.OutlookEventId.Should().Be("evt-new");
            existingAction.OutlookSyncStatus.Should().Be(MarketingSyncStatus.Synced);
        }

        [Fact]
        public async Task UpdateHandler_ReturnsError_WhenOutlookThrows()
        {
            // Arrange
            var existingAction = BuildAction(outlookEventId: "evt-existing");
            _repositoryMock
                .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingAction);

            _outlookSyncMock
                .Setup(s => s.UpdateEventAsync(existingAction, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OutlookCalendarSyncException(HttpStatusCode.InternalServerError, null, "update failed"));

            var handler = BuildUpdateHandler(pushEnabled: true);

            // Act
            var result = await handler.Handle(BuildUpdateRequest(), CancellationToken.None);

            // Assert — fail-fast: handler returns an error response without saving to DB
            result.Success.Should().BeFalse();
            _repositoryMock.Verify(
                x => x.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ─── Delete handler ───────────────────────────────────────────────────────

        [Fact]
        public async Task DeleteHandler_CallsDeleteEvent_WhenOutlookEventIdExists()
        {
            // Arrange
            var existingAction = BuildAction(outlookEventId: "evt-to-delete");
            _repositoryMock
                .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingAction);

            _repositoryMock
                .Setup(x => x.DeleteSoftAsync(42, AuthenticatedUser.Id!, AuthenticatedUser.Name!, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _outlookSyncMock
                .Setup(s => s.DeleteEventAsync("evt-to-delete", It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var handler = BuildDeleteHandler(pushEnabled: true);

            // Act
            var result = await handler.Handle(new DeleteMarketingActionRequest { Id = 42 }, CancellationToken.None);

            // Assert — Outlook delete happens before soft-delete; DB soft-delete always follows on success
            result.Success.Should().BeTrue();
            _outlookSyncMock.Verify(
                s => s.DeleteEventAsync("evt-to-delete", It.IsAny<CancellationToken>()),
                Times.Once);
            _outlookSyncMock.VerifyNoOtherCalls();
            _repositoryMock.Verify(
                x => x.DeleteSoftAsync(42, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task DeleteHandler_ReturnsError_WhenOutlookThrowsNonNotFound()
        {
            // Arrange
            var existingAction = BuildAction(outlookEventId: "evt-failing");
            _repositoryMock
                .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingAction);

            _outlookSyncMock
                .Setup(s => s.DeleteEventAsync("evt-failing", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OutlookCalendarSyncException(HttpStatusCode.InternalServerError, null, "Graph delete failed"));

            var handler = BuildDeleteHandler(pushEnabled: true);

            // Act
            var result = await handler.Handle(new DeleteMarketingActionRequest { Id = 42 }, CancellationToken.None);

            // Assert — fail-fast: Outlook error blocks soft-delete (non-404 errors are not swallowed)
            result.Success.Should().BeFalse();
            _repositoryMock.Verify(
                x => x.DeleteSoftAsync(42, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
