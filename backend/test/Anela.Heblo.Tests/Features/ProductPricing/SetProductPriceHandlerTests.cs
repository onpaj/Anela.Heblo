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
                new() { ProductCode = "A", ErpItemId = 11, PriceWithVat = 190m, PriceWithoutVat = 157.02m },
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
        // Act
        var response = await CreateSut().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _eshop.Verify(c => c.SetPriceWithVatAsync("A", 210.00m, It.IsAny<CancellationToken>()), Times.Once);
        _erpWriter.Verify(w => w.SetPriceWithoutVatAsync(11, 173.55m, It.IsAny<CancellationToken>()), Times.Once);
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
        _erpWriter.Setup(w => w.SetPriceWithoutVatAsync(11, It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
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
}
