using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.ShoptetOrders;

public class CompleteDeliveredOrdersJobTests
{
    private static (
        CompleteDeliveredOrdersJob Sut,
        Mock<IEshopOrderClient> Orders,
        Mock<IShipmentDeliveryChecker> Shipments,
        Mock<IRecurringJobStatusChecker> StatusChecker)
        MakeSut(bool jobEnabled = true, bool applyChanges = true, bool useTestSource = false)
    {
        var orders = new Mock<IEshopOrderClient>();
        var shipments = new Mock<IShipmentDeliveryChecker>();
        var statusChecker = new Mock<IRecurringJobStatusChecker>();
        statusChecker
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(jobEnabled);

        var featureFlags = new Mock<IFeatureFlagChecker>();
        featureFlags
            .Setup(f => f.IsEnabledAsync(FeatureFlagKeys.DeliveredOrderCompletion, It.IsAny<CancellationToken>()))
            .ReturnsAsync(applyChanges);
        featureFlags
            .Setup(f => f.IsEnabledAsync(FeatureFlagKeys.DeliveredOrderCompletionTestSource, It.IsAny<CancellationToken>()))
            .ReturnsAsync(useTestSource);

        var settings = Options.Create(new ShoptetOrdersSettings
        {
            DeliveredCompletionSourceStateIds = [70, 82],
            DeliveredCompletionTestSourceStateIds = [73],
            CompletedStatusId = -3,
        });

        var sut = new CompleteDeliveredOrdersJob(
            orders.Object, shipments.Object, settings,
            statusChecker.Object, featureFlags.Object, NullLogger<CompleteDeliveredOrdersJob>.Instance);
        return (sut, orders, shipments, statusChecker);
    }

    private static EshopOrderSummary Order(string code, int statusId) =>
        new() { Code = code, StatusId = statusId };

    [Fact]
    public async Task ExecuteAsync_SkipsWork_WhenJobDisabled()
    {
        var (sut, orders, shipments, _) = MakeSut(jobEnabled: false);

        await sut.ExecuteAsync();

        orders.Verify(o => o.ListOrdersByStatusAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        shipments.Verify(s => s.HasDeliveredShipmentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_CompletesOrder_WhenShipmentDelivered()
    {
        var (sut, orders, shipments, _) = MakeSut();
        orders.Setup(o => o.ListOrdersByStatusAsync(70, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Order("ORD-1", 70)]);
        orders.Setup(o => o.ListOrdersByStatusAsync(82, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        shipments.Setup(s => s.HasDeliveredShipmentAsync("ORD-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await sut.ExecuteAsync();

        orders.Verify(o => o.UpdateStatusAsync("ORD-1", -3, It.IsAny<CancellationToken>()), Times.Once);
        orders.Verify(o => o.AppendEshopRemarkAsync(
            "ORD-1", "Automaticky vyřízeno – zásilka doručena", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotComplete_WhenNoShipmentDelivered()
    {
        var (sut, orders, shipments, _) = MakeSut();
        orders.Setup(o => o.ListOrdersByStatusAsync(70, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Order("ORD-1", 70)]);
        orders.Setup(o => o.ListOrdersByStatusAsync(82, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        shipments.Setup(s => s.HasDeliveredShipmentAsync("ORD-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await sut.ExecuteAsync();

        orders.Verify(o => o.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesBothSourceStates()
    {
        var (sut, orders, shipments, _) = MakeSut();
        orders.Setup(o => o.ListOrdersByStatusAsync(70, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Order("ORD-70", 70)]);
        orders.Setup(o => o.ListOrdersByStatusAsync(82, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Order("ORD-82", 82)]);
        shipments.Setup(s => s.HasDeliveredShipmentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await sut.ExecuteAsync();

        orders.Verify(o => o.UpdateStatusAsync("ORD-70", -3, It.IsAny<CancellationToken>()), Times.Once);
        orders.Verify(o => o.UpdateStatusAsync("ORD-82", -3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_DryRun_DetectsButDoesNotMutate_WhenFeatureFlagDisabled()
    {
        var (sut, orders, shipments, _) = MakeSut(applyChanges: false);
        orders.Setup(o => o.ListOrdersByStatusAsync(70, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Order("ORD-1", 70)]);
        orders.Setup(o => o.ListOrdersByStatusAsync(82, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        shipments.Setup(s => s.HasDeliveredShipmentAsync("ORD-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await sut.ExecuteAsync();

        // Delivered orders are still detected (so the dry-run log reflects reality)...
        shipments.Verify(s => s.HasDeliveredShipmentAsync("ORD-1", It.IsAny<CancellationToken>()), Times.Once);
        // ...but no state change or remark is written while the flag is off.
        orders.Verify(o => o.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        orders.Verify(o => o.AppendEshopRemarkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_UsesTestSourceState_WhenTestSourceFlagEnabled()
    {
        var (sut, orders, shipments, _) = MakeSut(useTestSource: true);
        orders.Setup(o => o.ListOrdersByStatusAsync(73, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Order("ORD-TEST", 73)]);
        shipments.Setup(s => s.HasDeliveredShipmentAsync("ORD-TEST", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await sut.ExecuteAsync();

        // Test state 73 replaces the production source states entirely.
        orders.Verify(o => o.ListOrdersByStatusAsync(73, It.IsAny<CancellationToken>()), Times.Once);
        orders.Verify(o => o.ListOrdersByStatusAsync(70, It.IsAny<CancellationToken>()), Times.Never);
        orders.Verify(o => o.ListOrdersByStatusAsync(82, It.IsAny<CancellationToken>()), Times.Never);
        orders.Verify(o => o.UpdateStatusAsync("ORD-TEST", -3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_TestSourceRespectsDryRun_WhenCompletionFlagDisabled()
    {
        var (sut, orders, shipments, _) = MakeSut(applyChanges: false, useTestSource: true);
        orders.Setup(o => o.ListOrdersByStatusAsync(73, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Order("ORD-TEST", 73)]);
        shipments.Setup(s => s.HasDeliveredShipmentAsync("ORD-TEST", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await sut.ExecuteAsync();

        orders.Verify(o => o.ListOrdersByStatusAsync(73, It.IsAny<CancellationToken>()), Times.Once);
        orders.Verify(o => o.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        orders.Verify(o => o.AppendEshopRemarkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesProcessing_WhenOneOrderThrows()
    {
        var (sut, orders, shipments, _) = MakeSut();
        orders.Setup(o => o.ListOrdersByStatusAsync(70, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Order("ORD-FAIL", 70), Order("ORD-OK", 70)]);
        orders.Setup(o => o.ListOrdersByStatusAsync(82, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        shipments.Setup(s => s.HasDeliveredShipmentAsync("ORD-FAIL", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet 500"));
        shipments.Setup(s => s.HasDeliveredShipmentAsync("ORD-OK", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await sut.ExecuteAsync();

        orders.Verify(o => o.UpdateStatusAsync("ORD-OK", -3, It.IsAny<CancellationToken>()), Times.Once);
        orders.Verify(o => o.UpdateStatusAsync("ORD-FAIL", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
