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
        // CurrentUser is a positional record: (Id, Name, Email, IsAuthenticated).
        _currentUser.Setup(u => u.GetCurrentUser())
            .Returns(new CurrentUser("u1", "Ondra", "ondra@anela.cz", true));
    }

    private SetProductPriceHandler CreateSut() => new(
        _eshop.Object, _erpWriter.Object, _erpReader.Object,
        _changeLog.Object, _currentUser.Object,
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
        _erpWriter.Setup(w => w.SetPriceWithVatAsync(11, 210.00m, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("flexi"))
            .Returns(Task.CompletedTask);

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _eshop.Verify(c => c.SetPriceWithVatAsync("A", 210.00m, It.IsAny<CancellationToken>()), Times.Once);
        _erpWriter.Verify(w => w.SetPriceWithVatAsync(11, 210.00m, It.IsAny<CancellationToken>()), Times.Once);
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
    [InlineData("bezDph")]
    [InlineData("sDph")]
    [InlineData(null)]
    [InlineData("somethingUnrecognised")]
    public async Task writes_the_operators_with_vat_price_straight_to_cenazakl_for_every_price_type(
        string? erpPriceType)
    {
        // Arrange: the operator always enters a price INCLUDING VAT and that is the number
        // Flexi must end up holding, whatever the item's price type says. Confirmed against
        // the live ERP: converting to excl-VAT wrote the wrong figure.
        _erpReader.Setup(c => c.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>
            {
                new() { ProductCode = "A", ErpItemId = 11, ErpPriceType = erpPriceType },
            });

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _erpWriter.Verify(w => w.SetPriceWithVatAsync(11, 210.00m, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("bezDph")]
    [InlineData("sDph")]
    [InlineData(null)]
    public async Task never_needs_a_vat_rate_because_nothing_is_converted(string? erpPriceType)
    {
        // Arrange: with no conversion there is nothing a VAT rate could be needed for, so an
        // unrecognised VAT band must never block a write.
        _erpReader.Setup(c => c.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>
            {
                new() { ProductCode = "A", ErpItemId = 11, ErpPriceType = erpPriceType },
            });

        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _erpWriter.Verify(w => w.SetPriceWithVatAsync(11, 210.00m, It.IsAny<CancellationToken>()), Times.Once);
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
        _erpWriter.Setup(w => w.SetPriceWithVatAsync(11, It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
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
