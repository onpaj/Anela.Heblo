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

    private DoubleShotOrderSwapService CreateSut(int[]? eligible = null)
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
            DoubleShotSwapLookbackDays = 14,
        });
        return new DoubleShotOrderSwapService(_orders.Object, _stock.Object, settings,
            new FakeTimeProvider(Now), _logger.Object);
    }

    private void OrdersContaining(string variantCode, params string[] orderCodes) =>
        _orders.Setup(o => o.ListOrderCodesContainingProductAsync(variantCode, Now.AddDays(-14), It.IsAny<CancellationToken>()))
            .ReturnsAsync(orderCodes);

    private void Detail(string code, int status, params EshopOrderLine[] lines) =>
        _orders.Setup(o => o.GetOrderDetailAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EshopOrderDetail { Code = code, StatusId = status, Lines = lines });

    private static EshopOrderLine V(long id, decimal amount = 2m, string code = "SA015030V") =>
        new() { ItemId = id, ItemType = "product", Code = code, Amount = amount, UnitPriceWithVat = 1770m, VatRate = 21m, PriceRatio = 1m };

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
        _orders.Verify(o => o.ListOrderCodesContainingProductAsync("SA015030V", Now.AddDays(-14), It.IsAny<CancellationToken>()), Times.Once);
        _orders.Verify(o => o.ListOrderCodesContainingProductAsync("SA015030S", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
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
    public async Task Sweep_DoesNotWrite_WhenOrderNotEligible()
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
        _orders.Setup(o => o.ListOrderCodesContainingProductAsync("SA015030V", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom", null, HttpStatusCode.InternalServerError));
        OrdersContaining("SA016030V", "126000044");
        Detail("126000044", -2, V(5, code: "SA016030V"));
        AcceptWrites();
        var sut = new DoubleShotOrderSwapService(_orders.Object, _stock.Object,
            Options.Create(new ShoptetOrdersSettings()), new FakeTimeProvider(Now), _logger.Object);

        var summary = await sut.SweepAsync(applyChanges: true);

        summary.LinesSwapped.Should().Be(1);
    }
}
