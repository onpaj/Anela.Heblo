using System.Net;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.ShoptetOrders;

public class DoubleShotOrderSwapServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly Mock<IEshopOrderClient> _orders = new(MockBehavior.Strict);
    private readonly Mock<IEshopStockClient> _stock = new();
    private readonly Mock<ILogger<DoubleShotOrderSwapService>> _logger = new();

    private const int HoldStatus = 23;

    private DoubleShotOrderSwapService CreateSut(int[]? eligible = null, int lookbackDays = 14, int? holdStatusId = null)
    {
        _stock.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<EshopStock>
        {
            new() { Code = "SA015030V" },
            new() { Code = "SA015030S" },
            new() { Code = "SER002030" },
        });
        var settings = Options.Create(new ShoptetOrdersSettings
        {
            DoubleShotSwapEligibleStatusIds = eligible ?? [],
            DoubleShotSwapLookbackDays = lookbackDays,
            DoubleShotSwapHoldStatusId = holdStatusId,
        });
        return new DoubleShotOrderSwapService(_orders.Object, _stock.Object, settings,
            new FakeTimeProvider(Now), _logger.Object);
    }

    private const int EligibleListStatus = -2;

    private void OrdersContaining(string variantCode, params string[] orderCodes) =>
        OrdersContainingWithStatus(variantCode, orderCodes.Select(c => (c, EligibleListStatus)).ToArray());

    private void OrdersContainingWithStatus(string variantCode, params (string Code, int StatusId)[] orders) =>
        _orders.Setup(o => o.ListOrdersContainingProductAsync(variantCode, Now.AddDays(-14), It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders.Select(o => new EshopOrderSummary { Code = o.Code, StatusId = o.StatusId }).ToList());

    private void HeldOrders(params (string Code, DateTimeOffset Created)[] orders) =>
        _orders.Setup(o => o.ListOrdersByStatusAsync(HoldStatus, It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders.Select(o => new EshopOrderSummary { Code = o.Code, StatusId = HoldStatus, CreationTime = o.Created }).ToList());

    private void StatusOnRecheck(string code, int status) =>
        _orders.Setup(o => o.GetOrderStatusIdAsync(code, It.IsAny<CancellationToken>())).ReturnsAsync(status);

    private void AcceptStatusUpdates() =>
        _orders.Setup(o => o.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

    private void Detail(string code, int status, params EshopOrderLine[] lines) =>
        _orders.Setup(o => o.GetOrderDetailAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EshopOrderDetail { Code = code, StatusId = status, Lines = lines });

    private static EshopOrderLine V(long id, decimal amount = 2m, string code = "SA015030V", decimal price = 1770m) =>
        new() { ItemId = id, ItemType = "product", Code = code, Amount = amount, UnitPriceWithVat = price, VatRate = 21m, PriceRatio = 1m };

    private void AcceptWrites()
    {
        _orders.Setup(o => o.AddOrderItemAsync(It.IsAny<string>(), It.IsAny<NewEshopOrderItem>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _orders.Setup(o => o.DeleteOrderItemAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _orders.Setup(o => o.AppendEshopRemarkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void VerifyLogged(LogLevel level, string fragment) =>
        _logger.Verify(l => l.Log(level, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(fragment)),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);

    [Fact]
    public async Task Sweep_QueriesOnlyVariantCodesFromStock()
    {
        OrdersContaining("SA015030V");
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: true);

        summary.OrdersScanned.Should().Be(0);
        _orders.Verify(o => o.ListOrdersContainingProductAsync("SA015030V", Now.AddDays(-14), It.IsAny<CancellationToken>()), Times.Once);
        _orders.Verify(o => o.ListOrdersContainingProductAsync("SA015030S", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Sweep_DryRun_MakesNoWrites()
    {
        OrdersContaining("SA015030V", "126000043");
        Detail("126000043", -2, V(1));
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: false);

        summary.LinesSwapped.Should().Be(0);
        _orders.Verify(o => o.AddOrderItemAsync(It.IsAny<string>(), It.IsAny<NewEshopOrderItem>(), It.IsAny<CancellationToken>()), Times.Never);
        _orders.Verify(o => o.DeleteOrderItemAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyLogged(LogLevel.Information, "DRY RUN");
    }

    [Fact]
    public async Task Sweep_AddsSetBeforeDeletingVariant_AndAppendsOneNote()
    {
        OrdersContaining("SA015030V", "126000043");
        Detail("126000043", -2, V(1, amount: 2m));
        var calls = new List<string>();
        _orders.Setup(o => o.AddOrderItemAsync("126000043", It.IsAny<NewEshopOrderItem>(), It.IsAny<CancellationToken>()))
            .Callback<string, NewEshopOrderItem, CancellationToken>((_, i, _) => calls.Add($"add {i.Code} {i.ItemType} {i.Remark}"))
            .Returns(Task.CompletedTask);
        _orders.Setup(o => o.DeleteOrderItemAsync("126000043", 1, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("delete 1")).Returns(Task.CompletedTask);
        _orders.Setup(o => o.AppendEshopRemarkAsync("126000043", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, t, _) => calls.Add($"note {t}")).Returns(Task.CompletedTask);
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: true);

        calls.Should().Equal(
            "add SA015030S product-set auto-swap SA015030V #1",
            "delete 1",
            "note Automaticky prohozeno: SA015030V → SA015030S (2 ks)");
        summary.Should().Be(new DoubleShotSwapSummary(1, 1, 0, 0, 0));
    }

    [Fact]
    public async Task Sweep_OnlyDeletesVariant_WhenHalfSwapped()
    {
        OrdersContaining("SA015030V", "126000043");
        Detail("126000043", -2, V(1),
            new EshopOrderLine { ItemId = 9, ItemType = "product-set", Code = "SA015030S", Amount = 2m, Remark = "auto-swap SA015030V #1" });
        AcceptWrites();
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: true);

        _orders.Verify(o => o.AddOrderItemAsync(It.IsAny<string>(), It.IsAny<NewEshopOrderItem>(), It.IsAny<CancellationToken>()), Times.Never);
        _orders.Verify(o => o.DeleteOrderItemAsync("126000043", 1, It.IsAny<CancellationToken>()), Times.Once);
        summary.LinesHealed.Should().Be(1);
        summary.LinesSwapped.Should().Be(0);
    }

    [Fact]
    public async Task Sweep_KeepsVariant_AndLogsError_WhenAddReturns422()
    {
        OrdersContaining("SA015030V", "126000043");
        OrdersContaining("SA016030V");
        _stock.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EshopStock> { new() { Code = "SA015030V" }, new() { Code = "SA016030V" } });
        Detail("126000043", -2, V(1), V(2, code: "SA016030V"));
        AcceptWrites();
        _orders.Setup(o => o.AddOrderItemAsync("126000043", It.Is<NewEshopOrderItem>(i => i.Code == "SA015030S"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet POST ... returned 422: invalid-request-data", null, HttpStatusCode.UnprocessableEntity));
        var sut = new DoubleShotOrderSwapService(_orders.Object, _stock.Object,
            Options.Create(new ShoptetOrdersSettings()), new FakeTimeProvider(Now), _logger.Object);

        var summary = await sut.SweepAsync(applyChanges: true);

        _orders.Verify(o => o.DeleteOrderItemAsync("126000043", 1, It.IsAny<CancellationToken>()), Times.Never);
        _orders.Verify(o => o.DeleteOrderItemAsync("126000043", 2, It.IsAny<CancellationToken>()), Times.Once);
        _orders.Verify(o => o.AppendEshopRemarkAsync("126000043",
            "Automaticky prohozeno: SA016030V → SA016030S (2 ks)", It.IsAny<CancellationToken>()), Times.Once);
        summary.LinesFailed.Should().Be(1);
        summary.LinesSwapped.Should().Be(1);
        VerifyLogged(LogLevel.Error, "SA015030S");
    }

    [Fact]
    public async Task Sweep_AbandonsOrderOn409_AndContinuesWithNextOrder()
    {
        OrdersContaining("SA015030V", "126000043", "126000044");
        Detail("126000043", -2, V(1), V(2));
        Detail("126000044", -2, V(5));
        AcceptWrites();
        _orders.Setup(o => o.AddOrderItemAsync("126000043", It.IsAny<NewEshopOrderItem>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("409", null, HttpStatusCode.Conflict));
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: true);

        _orders.Verify(o => o.AddOrderItemAsync("126000043", It.IsAny<NewEshopOrderItem>(), It.IsAny<CancellationToken>()), Times.Once,
            "the second V line of an abandoned order must wait for the next sweep");
        _orders.Verify(o => o.DeleteOrderItemAsync("126000044", 5, It.IsAny<CancellationToken>()), Times.Once);
        _orders.Verify(o => o.AppendEshopRemarkAsync("126000043", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        summary.LinesSwapped.Should().Be(1);
    }

    [Fact]
    public async Task Sweep_CountsOrderFailed_WhenDetailThrows_AndContinues()
    {
        OrdersContaining("SA015030V", "126000043", "126000044");
        _orders.Setup(o => o.GetOrderDetailAsync("126000043", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom", null, HttpStatusCode.InternalServerError));
        Detail("126000044", -2, V(5));
        AcceptWrites();
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: true);

        summary.OrdersFailed.Should().Be(1);
        summary.LinesSwapped.Should().Be(1);
    }

    [Fact]
    public async Task Sweep_NeverFetchesDetail_WhenListStatusNotEligible()
    {
        OrdersContainingWithStatus("SA015030V", ("126000043", 26), ("126000044", -2));
        Detail("126000044", -2, V(5));
        AcceptWrites();
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: true);

        _orders.Verify(o => o.GetOrderDetailAsync("126000043", It.IsAny<CancellationToken>()), Times.Never);
        summary.Should().Be(new DoubleShotSwapSummary(1, 1, 0, 0, 0, OrdersNotEligible: 1));
        VerifyLogged(LogLevel.Information, "skipped 1 not eligible");
    }

    [Fact]
    public async Task Sweep_ClampsLookbackToAtLeastOneDay()
    {
        _orders.Setup(o => o.ListOrdersContainingProductAsync("SA015030V", Now.AddDays(-1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EshopOrderSummary>());
        var sut = CreateSut(lookbackDays: 0);

        await sut.SweepAsync(applyChanges: true);

        _orders.Verify(o => o.ListOrdersContainingProductAsync("SA015030V", Now.AddDays(-1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Sweep_AbandonsOrder_AndReportsBothLines_WhenDeleteFailsAfterAdd(HttpStatusCode deleteStatus)
    {
        OrdersContaining("SA015030V", "126000043");
        Detail("126000043", -2, V(1), V(2));
        AcceptWrites();
        _orders.Setup(o => o.DeleteOrderItemAsync("126000043", 1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("delete failed", null, deleteStatus));
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: true);

        _orders.Verify(o => o.AddOrderItemAsync("126000043", It.IsAny<NewEshopOrderItem>(), It.IsAny<CancellationToken>()), Times.Once,
            "the next V line of the order must not be attempted after a failed delete");
        _orders.Verify(o => o.DeleteOrderItemAsync("126000043", 2, It.IsAny<CancellationToken>()), Times.Never);
        _orders.Verify(o => o.AppendEshopRemarkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        summary.LinesSwapped.Should().Be(0);
        summary.LinesFailed.Should().Be(1);
        VerifyLogged(LogLevel.Error, "now contains both SA015030V (line 1) and SA015030S");
    }

    [Theory]
    [InlineData(0, 1770)]
    [InlineData(2, 0)]
    public async Task Sweep_SkipsVariantLineWithZeroAmountOrPrice_AndWarns(int amount, int price)
    {
        OrdersContaining("SA015030V", "126000043");
        Detail("126000043", -2, V(1, amount: amount, price: price));
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: true);

        // Strict mock: any Add/Delete/Append call would have thrown and been counted as a failed order.
        summary.Should().Be(new DoubleShotSwapSummary(1, 0, 0, 0, 0));
        VerifyLogged(LogLevel.Warning, "not swapped");
    }

    [Fact]
    public async Task Sweep_DoesNotWrite_WhenDetailStatusNoLongerEligible()
    {
        OrdersContaining("SA015030V", "126000043");
        Detail("126000043", 26, V(1));
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: true);

        summary.Should().Be(new DoubleShotSwapSummary(1, 0, 0, 0, 0));
        // Strict mock: any Add/Delete/Append call would have thrown.
        _orders.Verify(o => o.AddOrderItemAsync(It.IsAny<string>(), It.IsAny<NewEshopOrderItem>(), It.IsAny<CancellationToken>()), Times.Never);
        _orders.Verify(o => o.DeleteOrderItemAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Sweep_UsesConfiguredEligibleStatuses_WhenSet()
    {
        OrdersContaining("SA015030V", "126000043");
        Detail("126000043", -1, V(1));
        var sut = CreateSut(eligible: [-2]);

        await sut.SweepAsync(applyChanges: true);
        // Strict mock: status -1 is not in [-2], so no write happens.
        _orders.Verify(o => o.AddOrderItemAsync(It.IsAny<string>(), It.IsAny<NewEshopOrderItem>(), It.IsAny<CancellationToken>()), Times.Never);
        _orders.Verify(o => o.DeleteOrderItemAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Sweep_SkipsOrderThatDisappeared()
    {
        OrdersContaining("SA015030V", "126000043");
        _orders.Setup(o => o.GetOrderDetailAsync("126000043", It.IsAny<CancellationToken>())).ReturnsAsync((EshopOrderDetail?)null);
        var sut = CreateSut();

        var summary = await sut.SweepAsync(applyChanges: true);

        summary.OrdersFailed.Should().Be(0);
    }

    [Fact]
    public async Task Sweep_ContinuesWithOtherVariants_WhenListingOneFails()
    {
        _stock.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EshopStock> { new() { Code = "SA015030V" }, new() { Code = "SA016030V" } });
        _orders.Setup(o => o.ListOrdersContainingProductAsync("SA015030V", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom", null, HttpStatusCode.InternalServerError));
        OrdersContaining("SA016030V", "126000044");
        Detail("126000044", -2, V(5, code: "SA016030V"));
        AcceptWrites();
        var sut = new DoubleShotOrderSwapService(_orders.Object, _stock.Object,
            Options.Create(new ShoptetOrdersSettings()), new FakeTimeProvider(Now), _logger.Object);

        var summary = await sut.SweepAsync(applyChanges: true);

        summary.LinesSwapped.Should().Be(1);
        summary.ListingsFailed.Should().Be(1);
    }
    [Fact]
    public async Task Sweep_DoesNotListHoldStatus_WhenNoHoldStatusConfigured()
    {
        OrdersContaining("SA015030V");
        var sut = CreateSut();

        await sut.SweepAsync(applyChanges: true);

        _orders.Verify(o => o.ListOrdersByStatusAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Sweep_SwapsHeldOrder_ThenReleasesItToNova()
    {
        OrdersContaining("SA015030V");
        HeldOrders(("126000050", Now.AddMinutes(-3)));
        Detail("126000050", HoldStatus, V(1));
        StatusOnRecheck("126000050", HoldStatus);
        var calls = new List<string>();
        _orders.Setup(o => o.AddOrderItemAsync("126000050", It.IsAny<NewEshopOrderItem>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("add")).Returns(Task.CompletedTask);
        _orders.Setup(o => o.DeleteOrderItemAsync("126000050", 1, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("delete")).Returns(Task.CompletedTask);
        _orders.Setup(o => o.AppendEshopRemarkAsync("126000050", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("note")).Returns(Task.CompletedTask);
        _orders.Setup(o => o.UpdateStatusAsync("126000050", -1, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("release -1")).Returns(Task.CompletedTask);
        var sut = CreateSut(holdStatusId: HoldStatus);

        var summary = await sut.SweepAsync(applyChanges: true);

        calls.Should().Equal("add", "delete", "note", "release -1");
        summary.LinesSwapped.Should().Be(1);
        summary.OrdersReleased.Should().Be(1);
        summary.OrdersStillHeld.Should().Be(0);
    }

    [Fact]
    public async Task Sweep_ReleasesHeldOrder_WithNothingToSwap()
    {
        OrdersContaining("SA015030V");
        HeldOrders(("126000050", Now.AddMinutes(-3)));
        Detail("126000050", HoldStatus,
            new EshopOrderLine { ItemId = 4, ItemType = "product", Code = "SER002030", Amount = 1m, UnitPriceWithVat = 300m });
        StatusOnRecheck("126000050", HoldStatus);
        AcceptStatusUpdates();
        var sut = CreateSut(holdStatusId: HoldStatus);

        var summary = await sut.SweepAsync(applyChanges: true);

        _orders.Verify(o => o.UpdateStatusAsync("126000050", -1, It.IsAny<CancellationToken>()), Times.Once);
        summary.OrdersReleased.Should().Be(1);
    }

    [Fact]
    public async Task Sweep_ReleasesToConfiguredStatus()
    {
        OrdersContaining("SA015030V");
        HeldOrders(("126000050", Now.AddMinutes(-3)));
        Detail("126000050", HoldStatus);
        StatusOnRecheck("126000050", HoldStatus);
        AcceptStatusUpdates();
        var settings = Options.Create(new ShoptetOrdersSettings
        {
            DoubleShotSwapHoldStatusId = HoldStatus,
            DoubleShotSwapReleaseStatusId = 32,
        });
        CreateSut();
        var sut = new DoubleShotOrderSwapService(_orders.Object, _stock.Object, settings, new FakeTimeProvider(Now), _logger.Object);

        await sut.SweepAsync(applyChanges: true);

        _orders.Verify(o => o.UpdateStatusAsync("126000050", 32, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Sweep_KeepsHeldOrderInHold_WhenSwapFails()
    {
        OrdersContaining("SA015030V");
        HeldOrders(("126000050", Now.AddMinutes(-3)));
        Detail("126000050", HoldStatus, V(1));
        AcceptWrites();
        _orders.Setup(o => o.AddOrderItemAsync("126000050", It.IsAny<NewEshopOrderItem>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("422", null, HttpStatusCode.UnprocessableEntity));
        var sut = CreateSut(holdStatusId: HoldStatus);

        var summary = await sut.SweepAsync(applyChanges: true);

        // Strict mock: UpdateStatusAsync has no setup, so a release attempt would fail the order.
        _orders.Verify(o => o.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        summary.OrdersFailed.Should().Be(0);
        summary.OrdersStillHeld.Should().Be(1);
        summary.OrdersReleased.Should().Be(0);
    }

    [Fact]
    public async Task Sweep_KeepsHeldOrderInHold_WhenVariantLineWasSkipped()
    {
        OrdersContaining("SA015030V");
        HeldOrders(("126000050", Now.AddMinutes(-3)));
        Detail("126000050", HoldStatus, V(1, price: 0m));
        var sut = CreateSut(holdStatusId: HoldStatus);

        var summary = await sut.SweepAsync(applyChanges: true);

        _orders.Verify(o => o.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        summary.OrdersFailed.Should().Be(0);
        summary.OrdersStillHeld.Should().Be(1);
    }

    [Fact]
    public async Task Sweep_DoesNotRelease_WhenStatusChangedSinceTheSwap()
    {
        OrdersContaining("SA015030V");
        HeldOrders(("126000050", Now.AddMinutes(-3)));
        Detail("126000050", HoldStatus, V(1));
        AcceptWrites();
        StatusOnRecheck("126000050", 32);
        var sut = CreateSut(holdStatusId: HoldStatus);

        var summary = await sut.SweepAsync(applyChanges: true);

        _orders.Verify(o => o.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        summary.LinesSwapped.Should().Be(1);
        summary.OrdersReleased.Should().Be(0);
        summary.OrdersStillHeld.Should().Be(0);
    }

    [Fact]
    public async Task Sweep_DryRun_LogsReleaseButChangesNothing()
    {
        OrdersContaining("SA015030V");
        HeldOrders(("126000050", Now.AddMinutes(-3)));
        Detail("126000050", HoldStatus, V(1));
        var sut = CreateSut(holdStatusId: HoldStatus);

        var summary = await sut.SweepAsync(applyChanges: false);

        // Strict mock: no write or status setups exist, so any call would fail the order.
        summary.OrdersFailed.Should().Be(0);
        summary.OrdersReleased.Should().Be(0);
        VerifyLogged(LogLevel.Information, "would release it to status -1");
    }

    [Fact]
    public async Task Sweep_WarnsAboutOrderHeldLongerThanFifteenMinutes()
    {
        OrdersContaining("SA015030V");
        HeldOrders(("126000050", Now.AddMinutes(-20)));
        Detail("126000050", HoldStatus, V(1, price: 0m));
        var sut = CreateSut(holdStatusId: HoldStatus);

        await sut.SweepAsync(applyChanges: true);

        VerifyLogged(LogLevel.Warning, "126000050 has been waiting in hold status 23");
    }

    [Fact]
    public async Task Sweep_DoesNotWarn_WhenHeldOrderIsRecent()
    {
        OrdersContaining("SA015030V");
        HeldOrders(("126000050", Now.AddMinutes(-5)));
        Detail("126000050", HoldStatus, V(1, price: 0m));
        var sut = CreateSut(holdStatusId: HoldStatus);

        await sut.SweepAsync(applyChanges: true);

        _logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("waiting in hold")),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }

    [Fact]
    public async Task Sweep_ProcessesHeldOrderOnce_WhenVariantListingAlsoFindsIt()
    {
        OrdersContainingWithStatus("SA015030V", ("126000050", HoldStatus));
        HeldOrders(("126000050", Now.AddMinutes(-3)));
        Detail("126000050", HoldStatus, V(1));
        AcceptWrites();
        StatusOnRecheck("126000050", HoldStatus);
        AcceptStatusUpdates();
        var sut = CreateSut(holdStatusId: HoldStatus);

        var summary = await sut.SweepAsync(applyChanges: true);

        _orders.Verify(o => o.GetOrderDetailAsync("126000050", It.IsAny<CancellationToken>()), Times.Once);
        summary.OrdersScanned.Should().Be(1);
        summary.OrdersNotEligible.Should().Be(0);
    }

    [Fact]
    public async Task Sweep_CountsFailedHoldListing_AndStillSweepsVariants()
    {
        OrdersContaining("SA015030V", "126000044");
        Detail("126000044", -2, V(5));
        AcceptWrites();
        _orders.Setup(o => o.ListOrdersByStatusAsync(HoldStatus, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom", null, HttpStatusCode.InternalServerError));
        var sut = CreateSut(holdStatusId: HoldStatus);

        var summary = await sut.SweepAsync(applyChanges: true);

        summary.ListingsFailed.Should().Be(1);
        summary.LinesSwapped.Should().Be(1);
    }
}
