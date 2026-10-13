using Anela.Heblo.Adapters.ShoptetApi.Analytics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Analytics;

public class ShoptetOrdersSyncJobTests
{
    private static ShoptetOrdersSyncJob CreateJob(
        ShoptetOrdersSyncOptions options, Mock<IShoptetOrdersSyncService>? sync = null) =>
        new((sync ?? new Mock<IShoptetOrdersSyncService>()).Object,
            Options.Create(options),
            NullLogger<ShoptetOrdersSyncJob>.Instance);

    [Fact]
    public void Metadata_uses_a_cron_slot_outside_the_crowded_nightly_import_band()
    {
        // Arrange & Act — 02:00–09:00 is taken by the daily imports and flexi-analytics-sync
        // holds 03:00, so this job runs at 01:30.
        var job = CreateJob(new ShoptetOrdersSyncOptions());

        // Assert
        job.Metadata.JobName.Should().Be("shoptet-orders-sync");
        job.Metadata.CronExpression.Should().Be("30 1 * * *");
        job.Metadata.TimeZoneId.Should().Be("Europe/Prague");
    }

    [Fact]
    public async Task ExecuteAsync_skips_the_sync_when_the_job_is_disabled()
    {
        // Arrange
        var sync = new Mock<IShoptetOrdersSyncService>();
        var job = CreateJob(new ShoptetOrdersSyncOptions { Enabled = false }, sync);

        // Act
        await job.ExecuteAsync();

        // Assert
        sync.Verify(s => s.SyncAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_runs_the_sync_when_enabled()
    {
        // Arrange
        var sync = new Mock<IShoptetOrdersSyncService>();
        sync.Setup(s => s.SyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShoptetOrdersSyncReport(10, 10, true, true));
        var job = CreateJob(new ShoptetOrdersSyncOptions(), sync);

        // Act
        await job.ExecuteAsync();

        // Assert
        sync.Verify(s => s.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_rethrows_so_Hangfire_records_the_failure()
    {
        // Arrange
        var sync = new Mock<IShoptetOrdersSyncService>();
        sync.Setup(s => s.SyncAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var job = CreateJob(new ShoptetOrdersSyncOptions(), sync);

        // Act
        var act = () => job.ExecuteAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
