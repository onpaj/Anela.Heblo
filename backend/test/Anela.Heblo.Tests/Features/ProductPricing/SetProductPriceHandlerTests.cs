using Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class SetProductPriceHandlerTests
{
    private readonly Mock<IEshopPriceListClient> _eshop = new();
    private readonly Mock<IErpPriceWriter> _erpWriter = new();
    private readonly Mock<IProductPriceErpClient> _erpReader = new();
    private readonly Mock<IProductVatRateProvider> _vatRates = new();
    private readonly Mock<IProductPriceChangeLogRepository> _changeLog = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    public SetProductPriceHandlerTests()
    {
        _eshop.Setup(c => c.GetPriceWithVatAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(190.00m);
        _erpReader.Setup(c => c.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>
            {
                new()
                {
                    ProductCode = "A", ErpItemId = 11, PriceWithVat = 190m, PriceWithoutVat = 157.02m,
                    ErpPriceType = "bezDph",
                },
            });
        _vatRates.Setup(v => v.GetVatRatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 21m });
        // CurrentUser is a positional record: (Id, Name, Email, IsAuthenticated).
        _currentUser.Setup(u => u.GetCurrentUser())
            .Returns(new CurrentUser("u1", "Ondra", "ondra@anela.cz", true));
    }

    private SetProductPriceHandler CreateSut() => new(
        _eshop.Object, _erpWriter.Object, _erpReader.Object,
        _vatRates.Object, _changeLog.Object, _currentUser.Object,
        NullLogger<SetProductPriceHandler>.Instance);

    private static SetProductPriceRequest Request(decimal price = 210.00m) =>
        new() { ProductCode = "A", PriceWithVat = price };

    [Fact]
    public async Task writes_shoptet_then_flexi_with_the_price_excluding_vat()
    {
        // Arrange: capture actual call order rather than merely asserting both calls happened.
        var callOrder = new List<string>();
        _eshop.Setup(c => c.SetPriceWithVatAsync("A", 210.00m, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("shoptet"))
            .Returns(Task.CompletedTask);
        _erpWriter.Setup(w => w.SetBasePriceAsync(11, 173.55m, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("flexi"))
            .Returns(Task.CompletedTask);

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _eshop.Verify(c => c.SetPriceWithVatAsync("A", 210.00m, It.IsAny<CancellationToken>()), Times.Once);
        _erpWriter.Verify(w => w.SetBasePriceAsync(11, 173.55m, It.IsAny<CancellationToken>()), Times.Once);
        callOrder.Should().Equal("shoptet", "flexi");
    }

    [Fact]
    public async Task aborts_before_touching_shoptet_when_flexi_has_no_cenik_id()
    {
        // Arrange
        _erpReader.Setup(c => c.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>
            {
                new() { ProductCode = "A", ErpItemId = 0 },
            });

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.ErrorCode.Should().Be(ErrorCodes.ProductPriceFlexiItemIdUnknown);
        _eshop.Verify(c => c.SetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _erpWriter.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("sDph")]
    [InlineData(null)]
    [InlineData("somethingUnrecognised")]
    public async Task writes_the_operators_with_vat_price_straight_to_cenazakl_unless_the_item_is_bez_dph(
        string? erpPriceType)
    {
        // Arrange: cenaZakl's VAT meaning is the item's own. Only "bezDph" stores an excl-VAT
        // number needing conversion; for everything else cenaZakl IS the with-VAT price, and
        // the operator always enters with VAT — so their number goes through untouched.
        _erpReader.Setup(c => c.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>
            {
                new() { ProductCode = "A", ErpItemId = 11, ErpPriceType = erpPriceType },
            });

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _erpWriter.Verify(w => w.SetBasePriceAsync(11, 210.00m, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("sDph")]
    [InlineData(null)]
    public async Task does_not_need_a_vat_rate_at_all_when_cenazakl_already_holds_the_with_vat_price(
        string? erpPriceType)
    {
        // Arrange: an unrecognised VAT band blocks only the bezDph conversion. These items
        // need no rate, so the band must not block them.
        _erpReader.Setup(c => c.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>
            {
                new() { ProductCode = "A", ErpItemId = 11, ErpPriceType = erpPriceType },
            });
        _vatRates.Setup(v => v.GetVatRatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal>());

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _erpWriter.Verify(w => w.SetBasePriceAsync(11, 210.00m, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task aborts_before_touching_shoptet_when_the_flexi_vat_rate_is_unknown()
    {
        // Arrange: a "bezDph" item (the fixture default) needs the rate to convert the
        // operator's with-VAT price into cenaZakl. The provider omits a product whose Flexi
        // VAT band it could not recognise, rather than handing back a fabricated 21 that
        // would be written into a live ERP.
        _vatRates.Setup(v => v.GetVatRatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal>());

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert: its own code — "no cen\u00edk item" would send the operator hunting for
        // something that exists.
        response.ErrorCode.Should().Be(ErrorCodes.ProductPriceFlexiVatRateUnknown);
        _eshop.Verify(c => c.SetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _erpWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task reports_an_erp_read_failure_separately_from_a_missing_cenik_item()
    {
        // Arrange: a Flexi outage, timeout or 500 — not a data-quality fact about the
        // product. Reporting it as "the product has no cen\u00edk item" sends every operator
        // hunting in Flexi for something that is actually there.
        _erpReader.Setup(c => c.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Flexi 500"));

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.ErrorCode.Should().Be(ErrorCodes.ProductPriceErpReadFailed);
        _eshop.Verify(c => c.SetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _erpWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task reports_a_vat_rate_read_failure_as_an_erp_read_failure()
    {
        // Arrange
        _vatRates.Setup(v => v.GetVatRatesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Flexi timeout"));

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.ErrorCode.Should().Be(ErrorCodes.ProductPriceErpReadFailed);
        _eshop.Verify(c => c.SetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _erpWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task never_writes_flexi_when_the_shoptet_write_fails()
    {
        // Arrange
        _eshop.Setup(c => c.SetPriceWithVatAsync("A", It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("422"));

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.ErrorCode.Should().Be(ErrorCodes.ProductPriceShoptetWriteFailed);
        _erpWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task reports_the_partial_failure_when_only_flexi_fails()
    {
        // Arrange
        _erpWriter.Setup(w => w.SetBasePriceAsync(11, It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Flexi timeout"));

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.ErrorCode.Should().Be(ErrorCodes.ProductPriceFlexiWriteFailed);
        _changeLog.Verify(l => l.AppendAsync(
            It.Is<ProductPriceChangeLog>(e => e.ShoptetSucceeded && !e.FlexiSucceeded),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task returns_not_found_when_the_product_has_no_shoptet_price()
    {
        // Arrange
        _eshop.Setup(c => c.GetPriceWithVatAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal?)null);

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.ErrorCode.Should().Be(ErrorCodes.ProductPriceNotFoundInShoptet);
        _eshop.Verify(c => c.SetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task appends_a_change_log_row_when_the_product_has_no_shoptet_price()
    {
        // Arrange
        _eshop.Setup(c => c.GetPriceWithVatAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal?)null);

        // Act
        await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert: a row must exist for this outcome too, not only for the Flexi-failure and
        // success paths.
        _changeLog.Verify(l => l.AppendAsync(
            It.Is<ProductPriceChangeLog>(e =>
                !e.ShoptetSucceeded && !e.FlexiSucceeded && e.OldPriceWithVat == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task appends_a_change_log_row_when_the_shoptet_write_fails()
    {
        // Arrange
        _eshop.Setup(c => c.SetPriceWithVatAsync("A", It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("422"));

        // Act
        await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        _changeLog.Verify(l => l.AppendAsync(
            It.Is<ProductPriceChangeLog>(e =>
                !e.ShoptetSucceeded && !e.FlexiSucceeded && e.OldPriceWithVat == 190.00m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task appends_a_change_log_row_when_flexi_has_no_cenik_id()
    {
        // Arrange: the spec promises a row for every outcome, including the pre-flight
        // refusals — this leg had none.
        _erpReader.Setup(c => c.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp> { new() { ProductCode = "A", ErpItemId = 0 } });

        // Act
        await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        _changeLog.Verify(l => l.AppendAsync(
            It.Is<ProductPriceChangeLog>(e =>
                !e.ShoptetSucceeded && !e.FlexiSucceeded && e.OldPriceWithVat == 190.00m &&
                e.ErrorMessage != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task appends_a_change_log_row_when_a_bez_dph_items_vat_band_is_unrecognised()
    {
        // Arrange: the fixture's product is "bezDph", so the conversion needs a rate.
        _vatRates.Setup(v => v.GetVatRatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal>());

        // Act
        await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        _changeLog.Verify(l => l.AppendAsync(
            It.Is<ProductPriceChangeLog>(e =>
                !e.ShoptetSucceeded && !e.FlexiSucceeded && e.OldPriceWithVat == 190.00m &&
                e.ErrorMessage != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task records_the_old_and_new_price_on_success()
    {
        // Act
        await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        _changeLog.Verify(l => l.AppendAsync(
            It.Is<ProductPriceChangeLog>(e =>
                e.OldPriceWithVat == 190.00m && e.NewPriceWithVat == 210.00m &&
                e.ChangedBy == "ondra@anela.cz" && e.ShoptetSucceeded && e.FlexiSucceeded),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task succeeds_even_when_appending_the_change_log_throws()
    {
        // Arrange: the log write must never be able to flip a completed price change into a
        // reported failure — both remote writes have already landed by the time it runs.
        _changeLog.Setup(l => l.AppendAsync(It.IsAny<ProductPriceChangeLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.PriceWithVat.Should().Be(210.00m);
    }

    [Fact]
    public async Task still_reports_the_real_error_code_when_appending_the_change_log_throws()
    {
        // Arrange: on a failure path, a log-write failure must not mask the real error code.
        _eshop.Setup(c => c.SetPriceWithVatAsync("A", It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("422"));
        _changeLog.Setup(l => l.AppendAsync(It.IsAny<ProductPriceChangeLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.ErrorCode.Should().Be(ErrorCodes.ProductPriceShoptetWriteFailed);
    }
}
