using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.ShoptetOrders;

public class CreateMissingInvoicesJobTests
{
    private static readonly DateTimeOffset CreatedFrom = new(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(1));

    private readonly Mock<IEshopOrderClient> _orders = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();
    private readonly Mock<IFeatureFlagChecker> _featureFlags = new();

    private CreateMissingInvoicesJob MakeSut(bool jobEnabled = true, bool applyChanges = true, int maxPerRun = 100)
    {
        _statusChecker
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(jobEnabled);
        _featureFlags
            .Setup(f => f.IsEnabledAsync(FeatureFlagKeys.MissingInvoiceCreation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(applyChanges);
        _orders.Setup(o => o.GetOrderDetailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) => Detail(code, hasBillingMethod: true));

        var settings = Options.Create(new ShoptetOrdersSettings
        {
            CompletedStatusId = -3,
            MissingInvoiceOrdersCreatedFrom = CreatedFrom,
            MissingInvoiceCreationDelayMs = 0,
            MissingInvoiceMaxPerRun = maxPerRun,
        });

        return new CreateMissingInvoicesJob(
            _orders.Object, settings, _statusChecker.Object, _featureFlags.Object,
            NullLogger<CreateMissingInvoicesJob>.Instance);
    }

    private void GivenCompletedOrders(params EshopOrderSummary[] orders) =>
        _orders.Setup(o => o.ListOrdersByStatusAsync(-3, CreatedFrom, It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);

    private void GivenInvoicedOrderCodes(params string[] codes) =>
        _orders.Setup(o => o.ListInvoicedOrderCodesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes.ToHashSet(StringComparer.Ordinal));

    private static EshopOrderSummary Order(string code, decimal? priceWithVat) =>
        new() { Code = code, StatusId = -3, PriceWithVat = priceWithVat };

    private static EshopOrderDetail Detail(string code, bool hasBillingMethod) =>
        new() { Code = code, StatusId = -3, HasBillingMethod = hasBillingMethod };

    [Fact]
    public async Task ExecuteAsync_DoesNothing_WhenJobDisabled()
    {
        var sut = MakeSut(jobEnabled: false);

        await sut.ExecuteAsync();

        _orders.Verify(o => o.ListOrdersByStatusAsync(It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        _orders.Verify(o => o.CreateInvoiceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesInvoice_ForCompletedOrderWithPriceAndNoInvoice()
    {
        var sut = MakeSut();
        GivenCompletedOrders(Order("126000001", 350m));
        GivenInvoicedOrderCodes("125999999");

        await sut.ExecuteAsync();

        _orders.Verify(o => o.CreateInvoiceAsync("126000001", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(null)]
    public async Task ExecuteAsync_SkipsOrder_WhenTotalIsZeroOrMissing(int? price)
    {
        var sut = MakeSut();
        GivenCompletedOrders(Order("126000001", price));
        GivenInvoicedOrderCodes("125999999");

        await sut.ExecuteAsync();

        _orders.Verify(o => o.CreateInvoiceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsOrder_WhenItAlreadyHasAnInvoice()
    {
        var sut = MakeSut();
        GivenCompletedOrders(Order("126000001", 350m), Order("126000002", 99m));
        GivenInvoicedOrderCodes("126000001");

        await sut.ExecuteAsync();

        _orders.Verify(o => o.CreateInvoiceAsync("126000001", It.IsAny<CancellationToken>()), Times.Never);
        _orders.Verify(o => o.CreateInvoiceAsync("126000002", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ListsInvoicesFromBeforeTheOrderStartDate()
    {
        var sut = MakeSut();
        GivenCompletedOrders(Order("126000001", 350m));
        GivenInvoicedOrderCodes("125999999");

        await sut.ExecuteAsync();

        _orders.Verify(o => o.ListInvoicedOrderCodesAsync(
            It.Is<DateTimeOffset>(d => d < CreatedFrom), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesNothing_InDryRun()
    {
        var sut = MakeSut(applyChanges: false);
        GivenCompletedOrders(Order("126000001", 350m));
        GivenInvoicedOrderCodes("125999999");

        await sut.ExecuteAsync();

        _orders.Verify(o => o.CreateInvoiceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesWithNextOrder_WhenOneInvoiceFails()
    {
        var sut = MakeSut();
        GivenCompletedOrders(Order("126000001", 350m), Order("126000002", 99m));
        GivenInvoicedOrderCodes("125999999");
        _orders.Setup(o => o.CreateInvoiceAsync("126000001", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("422"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync());

        _orders.Verify(o => o.CreateInvoiceAsync("126000002", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesWithNextOrder_WhenOneRequestTimesOut()
    {
        // HttpClient reports a timeout as TaskCanceledException (an OperationCanceledException)
        // while the job's own token is not cancelled — it must not abort the run.
        var sut = MakeSut();
        GivenCompletedOrders(Order("126000001", 350m), Order("126000002", 99m));
        GivenInvoicedOrderCodes("125999999");
        _orders.Setup(o => o.CreateInvoiceAsync("126000001", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("timeout"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync());

        _orders.Verify(o => o.CreateInvoiceAsync("126000002", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotCreateInvoices_WhenInvoiceListingFails()
    {
        // Without the invoiced set every order looks uninvoiced — creating invoices then would duplicate them.
        var sut = MakeSut();
        GivenCompletedOrders(Order("126000001", 350m));
        _orders.Setup(o => o.ListInvoicedOrderCodesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("500"));

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.ExecuteAsync());

        _orders.Verify(o => o.CreateInvoiceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_StopsAndRethrows_WhenJobTokenIsCancelled()
    {
        var sut = MakeSut();
        GivenCompletedOrders(Order("126000001", 350m), Order("126000002", 99m));
        GivenInvoicedOrderCodes("125999999");
        using var cts = new CancellationTokenSource();
        _orders.Setup(o => o.CreateInvoiceAsync("126000001", It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.ExecuteAsync(cts.Token));

        _orders.Verify(o => o.CreateInvoiceAsync("126000002", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesNothing_WhenInvoiceListingIsEmptyButPricedOrdersExist()
    {
        // An empty invoiced set next to priced orders means the listing broke (200 with an empty or
        // malformed body) — invoicing everything would duplicate invoices that cannot be deleted.
        var sut = MakeSut();
        GivenCompletedOrders(Order("126000001", 350m), Order("126000002", 99m));
        GivenInvoicedOrderCodes();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync());

        _orders.Verify(o => o.CreateInvoiceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesAtMostTheConfiguredNumberOfInvoicesPerRun()
    {
        var sut = MakeSut(maxPerRun: 2);
        GivenCompletedOrders(
            Order("126000001", 1m), Order("126000002", 1m), Order("126000003", 1m), Order("126000009", 1m));
        GivenInvoicedOrderCodes("126000009");

        await sut.ExecuteAsync();

        _orders.Verify(o => o.CreateInvoiceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _orders.Verify(o => o.CreateInvoiceAsync("126000003", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsOrder_WhenItWasInvoicedAfterTheBulkListing()
    {
        var sut = MakeSut();
        GivenCompletedOrders(Order("126000001", 350m), Order("126000002", 99m));
        GivenInvoicedOrderCodes("125999999");
        _orders.Setup(o => o.HasInvoiceAsync("126000001", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await sut.ExecuteAsync();

        _orders.Verify(o => o.CreateInvoiceAsync("126000001", It.IsAny<CancellationToken>()), Times.Never);
        _orders.Verify(o => o.CreateInvoiceAsync("126000002", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Aborts_AfterFiveConsecutiveFailures()
    {
        var sut = MakeSut();
        var orders = Enumerable.Range(1, 8).Select(i => Order($"12600000{i}", 10m)).ToArray();
        GivenCompletedOrders(orders);
        GivenInvoicedOrderCodes("125999999");
        _orders.Setup(o => o.CreateInvoiceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("401"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync());

        _orders.Verify(o => o.CreateInvoiceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(5));
    }

    [Fact]
    public async Task ExecuteAsync_Throws_WhenMaxPerRunIsNotPositive()
    {
        var sut = MakeSut(maxPerRun: 0);
        GivenCompletedOrders(Order("126000001", 350m));
        GivenInvoicedOrderCodes("125999999");

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync());

        _orders.Verify(o => o.CreateInvoiceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsOrderWithoutBillingMethod_WithoutFailingTheRun()
    {
        // Shoptet rejects such an order with 422 "Unknown billingMethodId provided" — it needs a
        // payment method chosen by hand, so it is reported, not retried as a failure every night.
        var sut = MakeSut();
        GivenCompletedOrders(Order("126013625", 989m), Order("126000002", 99m));
        GivenInvoicedOrderCodes("125999999");
        _orders.Setup(o => o.GetOrderDetailAsync("126013625", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Detail("126013625", hasBillingMethod: false));

        await sut.ExecuteAsync();

        _orders.Verify(o => o.CreateInvoiceAsync("126013625", It.IsAny<CancellationToken>()), Times.Never);
        _orders.Verify(o => o.CreateInvoiceAsync("126000002", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_FailureMessage_NamesEachFailedOrderAndItsError()
    {
        var sut = MakeSut();
        GivenCompletedOrders(Order("126000001", 350m), Order("126000002", 99m));
        GivenInvoicedOrderCodes("125999999");
        _orders.Setup(o => o.CreateInvoiceAsync("126000001", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet POST /api/orders/126000001/invoice returned 409: conflict"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync());

        Assert.Contains("126000001", ex.Message);
        Assert.Contains("returned 409: conflict", ex.Message);
        Assert.DoesNotContain("126000002", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_CountsOrderAsFailed_WhenItNoLongerExists()
    {
        var sut = MakeSut();
        GivenCompletedOrders(Order("126000001", 350m));
        GivenInvoicedOrderCodes("125999999");
        _orders.Setup(o => o.GetOrderDetailAsync("126000001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EshopOrderDetail?)null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteAsync());

        Assert.Contains("126000001", ex.Message);
        _orders.Verify(o => o.CreateInvoiceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
