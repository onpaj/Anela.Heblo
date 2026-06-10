using Anela.Heblo.Application.Features.Packaging.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Packaging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging;

public class FillTrackingNumbersJobTests
{
    private static (
        FillTrackingNumbersJob Sut,
        Mock<IPackageRepository> Repo,
        Mock<IShipmentClient> Client,
        Mock<IRecurringJobStatusChecker> StatusChecker)
        MakeSut(bool jobEnabled = true)
    {
        var repo = new Mock<IPackageRepository>();
        var client = new Mock<IShipmentClient>();
        var statusChecker = new Mock<IRecurringJobStatusChecker>();
        statusChecker
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobEnabled);
        var logger = NullLogger<FillTrackingNumbersJob>.Instance;
        var sut = new FillTrackingNumbersJob(repo.Object, client.Object, statusChecker.Object, logger);
        return (sut, repo, client, statusChecker);
    }

    private static Package SamplePackage(int id = 1, string orderCode = "ORD-1", string packageNumber = "PKG-1") =>
        new()
        {
            Id = id,
            OrderCode = orderCode,
            CustomerName = "Alice",
            PackageNumber = packageNumber,
            TrackingNumber = null,
            ShippingProviderCode = "PPL",
            ShipmentGuid = Guid.NewGuid(),
            PackedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task ExecuteAsync_SkipsWork_WhenJobDisabled()
    {
        var (sut, repo, client, _) = MakeSut(jobEnabled: false);

        await sut.ExecuteAsync();

        repo.Verify(r => r.GetWithNullTrackingNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(c => c.GetLatestActiveTrackingNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNothing_WhenNoPackagesWithNullTracking()
    {
        var (sut, repo, client, _) = MakeSut();
        repo.Setup(r => r.GetWithNullTrackingNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await sut.ExecuteAsync();

        client.Verify(c => c.GetLatestActiveTrackingNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.SetTrackingNumberAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_UpdatesTrackingNumber_WhenLatestActiveShipmentHasTracking()
    {
        var (sut, repo, client, _) = MakeSut();
        var package = SamplePackage(id: 5, orderCode: "ORD-42");

        repo.Setup(r => r.GetWithNullTrackingNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([package]);

        client.Setup(c => c.GetLatestActiveTrackingNumberAsync("ORD-42", It.IsAny<CancellationToken>()))
            .ReturnsAsync("70603624124");

        await sut.ExecuteAsync();

        repo.Verify(r => r.SetTrackingNumberAsync(5, "70603624124", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsPackage_WhenNoActiveShipmentHasTrackingYet()
    {
        var (sut, repo, client, _) = MakeSut();
        var package = SamplePackage(id: 3, orderCode: "ORD-5");

        repo.Setup(r => r.GetWithNullTrackingNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([package]);

        client.Setup(c => c.GetLatestActiveTrackingNumberAsync("ORD-5", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        await sut.ExecuteAsync();

        repo.Verify(r => r.SetTrackingNumberAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_AppliesTrackingToAllOrderPackages_AndCallsShoptetOncePerOrder()
    {
        var (sut, repo, client, _) = MakeSut();
        var pkg1 = SamplePackage(id: 1, orderCode: "ORD-10", packageNumber: "Vlastní balení");
        var pkg2 = SamplePackage(id: 2, orderCode: "ORD-10", packageNumber: "Vlastní balení");

        repo.Setup(r => r.GetWithNullTrackingNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([pkg1, pkg2]);

        client.Setup(c => c.GetLatestActiveTrackingNumberAsync("ORD-10", It.IsAny<CancellationToken>()))
            .ReturnsAsync("TRK-LATEST");

        await sut.ExecuteAsync();

        client.Verify(c => c.GetLatestActiveTrackingNumberAsync("ORD-10", It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SetTrackingNumberAsync(1, "TRK-LATEST", It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SetTrackingNumberAsync(2, "TRK-LATEST", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesProcessing_WhenShoptetThrowsForOneOrder()
    {
        var (sut, repo, client, _) = MakeSut();
        var pkg1 = SamplePackage(id: 1, orderCode: "ORD-FAIL");
        var pkg2 = SamplePackage(id: 2, orderCode: "ORD-OK");

        repo.Setup(r => r.GetWithNullTrackingNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([pkg1, pkg2]);

        client.Setup(c => c.GetLatestActiveTrackingNumberAsync("ORD-FAIL", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet 500"));

        client.Setup(c => c.GetLatestActiveTrackingNumberAsync("ORD-OK", It.IsAny<CancellationToken>()))
            .ReturnsAsync("TRK-OK");

        await sut.ExecuteAsync();

        // ORD-OK still processed despite ORD-FAIL throwing
        repo.Verify(r => r.SetTrackingNumberAsync(2, "TRK-OK", It.IsAny<CancellationToken>()), Times.Once);
        // ORD-FAIL package not updated
        repo.Verify(r => r.SetTrackingNumberAsync(1, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
