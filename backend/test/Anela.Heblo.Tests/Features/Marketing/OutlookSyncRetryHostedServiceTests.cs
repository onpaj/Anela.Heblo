using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Domain.Features.Marketing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Marketing
{
    public class OutlookSyncRetryHostedServiceTests
    {
        private readonly Mock<IMarketingActionRepository> _repositoryMock;
        private readonly Mock<IOutlookCalendarSync> _outlookSyncMock;

        public OutlookSyncRetryHostedServiceTests()
        {
            _repositoryMock = new Mock<IMarketingActionRepository>();
            _outlookSyncMock = new Mock<IOutlookCalendarSync>();

            _repositoryMock
                .Setup(r => r.UpdateAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _repositoryMock
                .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
        }

        private OutlookSyncRetryHostedService CreateService()
        {
            var scopeFactory = CreateScopeFactory(_repositoryMock.Object, _outlookSyncMock.Object);
            return new OutlookSyncRetryHostedService(scopeFactory, NullLogger<OutlookSyncRetryHostedService>.Instance);
        }

        private static IServiceScopeFactory CreateScopeFactory(IMarketingActionRepository repo, IOutlookCalendarSync sync)
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => repo);
            services.AddScoped(_ => sync);
            var provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IServiceScopeFactory>();
        }

        private static MarketingAction BuildAction(int id = 1, bool isDeleted = false, string? outlookEventId = null)
        {
            return new MarketingAction
            {
                Id = id,
                Title = $"Action {id}",
                StartDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                CreatedByUserId = "user-1",
                IsDeleted = isDeleted,
                OutlookEventId = outlookEventId,
                OutlookSyncStatus = MarketingSyncStatus.Failed
            };
        }

        [Fact]
        public async Task ProcessFailedSyncs_DoesNothing_WhenNoFailedActions()
        {
            // Arrange
            _repositoryMock
                .Setup(r => r.GetFailedOutlookSyncAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MarketingAction>());

            var service = CreateService();

            // Act
            await service.ProcessFailedSyncsAsync(CancellationToken.None);

            // Assert
            _outlookSyncMock.Verify(
                s => s.CreateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _outlookSyncMock.Verify(
                s => s.UpdateEventAsync(It.IsAny<MarketingAction>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _outlookSyncMock.Verify(
                s => s.DeleteEventAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ProcessFailedSyncs_CallsCreateEvent_ForNonDeletedActionWithNoEventId()
        {
            // Arrange
            var action = BuildAction(id: 1, isDeleted: false, outlookEventId: null);
            var newEventId = "new-event-id";

            _repositoryMock
                .Setup(r => r.GetFailedOutlookSyncAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MarketingAction> { action });

            _outlookSyncMock
                .Setup(s => s.CreateEventAsync(action, It.IsAny<CancellationToken>()))
                .ReturnsAsync(newEventId);

            var service = CreateService();

            // Act
            await service.ProcessFailedSyncsAsync(CancellationToken.None);

            // Assert
            _outlookSyncMock.Verify(
                s => s.CreateEventAsync(action, It.IsAny<CancellationToken>()),
                Times.Once);
            action.OutlookSyncStatus.Should().Be(MarketingSyncStatus.Synced);
            action.OutlookEventId.Should().Be(newEventId);
            _repositoryMock.Verify(
                r => r.UpdateAsync(action, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessFailedSyncs_CallsUpdateEvent_ForNonDeletedActionWithEventId()
        {
            // Arrange
            var action = BuildAction(id: 2, isDeleted: false, outlookEventId: "existing-event-id");

            _repositoryMock
                .Setup(r => r.GetFailedOutlookSyncAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MarketingAction> { action });

            _outlookSyncMock
                .Setup(s => s.UpdateEventAsync(action, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            // Act
            await service.ProcessFailedSyncsAsync(CancellationToken.None);

            // Assert
            _outlookSyncMock.Verify(
                s => s.UpdateEventAsync(action, It.IsAny<CancellationToken>()),
                Times.Once);
            action.OutlookSyncStatus.Should().Be(MarketingSyncStatus.Synced);
            action.OutlookEventId.Should().Be("existing-event-id");
            _repositoryMock.Verify(
                r => r.UpdateAsync(action, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessFailedSyncs_CallsDeleteEvent_ForSoftDeletedActionWithEventId()
        {
            // Arrange
            var action = BuildAction(id: 3, isDeleted: true, outlookEventId: "to-delete-event-id");

            _repositoryMock
                .Setup(r => r.GetFailedOutlookSyncAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MarketingAction> { action });

            _outlookSyncMock
                .Setup(s => s.DeleteEventAsync("to-delete-event-id", It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = CreateService();

            // Act
            await service.ProcessFailedSyncsAsync(CancellationToken.None);

            // Assert
            _outlookSyncMock.Verify(
                s => s.DeleteEventAsync("to-delete-event-id", It.IsAny<CancellationToken>()),
                Times.Once);
            action.OutlookSyncStatus.Should().Be(MarketingSyncStatus.NotSynced);
            action.OutlookEventId.Should().BeNull();
            _repositoryMock.Verify(
                r => r.UpdateAsync(action, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ProcessFailedSyncs_ContinuesWithNextAction_WhenOneActionFails()
        {
            // Arrange
            var failingAction = BuildAction(id: 10, isDeleted: false, outlookEventId: null);
            var succeedingAction = BuildAction(id: 11, isDeleted: false, outlookEventId: null);
            var successEventId = "success-event-id";

            _repositoryMock
                .Setup(r => r.GetFailedOutlookSyncAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MarketingAction> { failingAction, succeedingAction });

            _outlookSyncMock
                .Setup(s => s.CreateEventAsync(failingAction, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Graph error"));

            _outlookSyncMock
                .Setup(s => s.CreateEventAsync(succeedingAction, It.IsAny<CancellationToken>()))
                .ReturnsAsync(successEventId);

            var service = CreateService();

            // Act
            await service.ProcessFailedSyncsAsync(CancellationToken.None);

            // Assert: second action was still processed
            _outlookSyncMock.Verify(
                s => s.CreateEventAsync(succeedingAction, It.IsAny<CancellationToken>()),
                Times.Once);
            succeedingAction.OutlookSyncStatus.Should().Be(MarketingSyncStatus.Synced);

            // First action remains Failed (exception was swallowed, status not updated)
            failingAction.OutlookSyncStatus.Should().Be(MarketingSyncStatus.Failed);
        }
    }
}
