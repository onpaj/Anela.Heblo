using Anela.Heblo.Application.Features.ProductPricing.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.ProductPricing.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class ProductPriceSyncJobTests
{
    private readonly Mock<IProductPriceSyncService> _syncService = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();

    private ProductPriceSyncJob CreateJob() =>
        new(_syncService.Object, _statusChecker.Object, NullLogger<ProductPriceSyncJob>.Instance);

    [Fact]
    public async Task runs_the_sync_when_the_job_is_enabled()
    {
        // Arrange
        _statusChecker.Setup(c => c.IsJobEnabledAsync("product-price-sync", It.IsAny<CancellationToken>(), true)).ReturnsAsync(true);
        _syncService.Setup(s => s.SyncAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new PriceSyncRunResult());

        // Act
        await CreateJob().ExecuteAsync(CancellationToken.None);

        // Assert
        _syncService.Verify(s => s.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task skips_the_sync_when_the_job_is_disabled()
    {
        // Arrange
        _statusChecker.Setup(c => c.IsJobEnabledAsync("product-price-sync", It.IsAny<CancellationToken>(), true)).ReturnsAsync(false);

        // Act
        await CreateJob().ExecuteAsync(CancellationToken.None);

        // Assert
        _syncService.Verify(s => s.SyncAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task rethrows_so_hangfire_can_retry()
    {
        // Arrange
        _statusChecker.Setup(c => c.IsJobEnabledAsync("product-price-sync", It.IsAny<CancellationToken>(), true)).ReturnsAsync(true);
        _syncService.Setup(s => s.SyncAsync(It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var act = () => CreateJob().ExecuteAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void exposes_stable_job_metadata()
    {
        // Arrange & Act
        var metadata = CreateJob().Metadata;

        // Assert
        metadata.JobName.Should().Be("product-price-sync");
        metadata.CronExpression.Should().NotBeNullOrWhiteSpace();
    }
}
