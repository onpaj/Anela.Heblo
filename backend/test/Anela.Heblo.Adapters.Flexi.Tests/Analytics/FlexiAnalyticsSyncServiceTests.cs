using Anela.Heblo.Adapters.Flexi.Analytics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

public sealed class FlexiAnalyticsSyncServiceTests
{
    private static Mock<IEntitySyncService> MockService(SyncResult result)
    {
        var mock = new Mock<IEntitySyncService>();
        mock.Setup(s => s.SyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    private static FlexiAnalyticsSyncService CreateSut(IEnumerable<IEntitySyncService> services) =>
        new(services, Mock.Of<ILogger<FlexiAnalyticsSyncService>>());

    [Fact]
    public async Task SyncAllAsync_WhenAllServicesSucceed_ReturnsFullSuccessReport()
    {
        // Arrange
        var service1 = MockService(new SyncResult(10, 8, IsSuccess: true));
        var service2 = MockService(new SyncResult(5, 4, IsSuccess: true));
        var sut = CreateSut([service1.Object, service2.Object]);

        // Act
        var report = await sut.SyncAllAsync();

        // Assert
        report.TotalFetched.Should().Be(15);
        report.TotalUpserted.Should().Be(12);
        report.FailedServices.Should().Be(0);
        report.IsFullSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SyncAllAsync_WhenOneServiceFails_StillRunsOthersAndReportsFailure()
    {
        // Arrange
        var service1 = MockService(new SyncResult(10, 8, IsSuccess: true));
        var service2 = MockService(new SyncResult(3, 0, IsSuccess: false));
        var service3 = MockService(new SyncResult(7, 6, IsSuccess: true));
        var sut = CreateSut([service1.Object, service2.Object, service3.Object]);

        // Act
        var report = await sut.SyncAllAsync();

        // Assert — all three services must have been called
        service1.Verify(s => s.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
        service2.Verify(s => s.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
        service3.Verify(s => s.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);

        report.FailedServices.Should().Be(1);
        report.IsFullSuccess.Should().BeFalse();
        // Only successful services contribute to totals
        report.TotalFetched.Should().Be(17);
        report.TotalUpserted.Should().Be(14);
    }

    [Fact]
    public async Task SyncAllAsync_WhenServiceThrows_RecordsFailureAndContinues()
    {
        // Arrange
        var service1 = new Mock<IEntitySyncService>();
        service1.Setup(s => s.SyncAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Flexi connection refused"));

        var service2 = MockService(new SyncResult(6, 5, IsSuccess: true));

        var sut = CreateSut([service1.Object, service2.Object]);

        // Act
        var report = await sut.SyncAllAsync();

        // Assert — both services must have been attempted
        service1.Verify(s => s.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
        service2.Verify(s => s.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);

        report.FailedServices.Should().Be(1);
        report.IsFullSuccess.Should().BeFalse();
        report.TotalFetched.Should().Be(6);
        report.TotalUpserted.Should().Be(5);
    }
}
