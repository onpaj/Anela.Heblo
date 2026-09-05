using Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.ProductPricing;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class SetProductPriceHandlerTests
{
    private readonly Mock<IProductPriceRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();

    public SetProductPriceHandlerTests()
    {
        _currentUserService
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-1", "Test User", "test.user@anela.cz", true));
    }

    [Fact]
    public async Task stores_the_new_price_and_marks_both_targets_pending()
    {
        // Arrange
        _repository
            .Setup(r => r.GetAsync("OCH001030", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductPrice { ProductCode = "OCH001030", PriceWithVat = 190.00m, VatRate = 21m });
        var savedStates = new List<ProductPriceSyncState>();
        _repository
            .Setup(r => r.UpsertSyncStateAsync(It.IsAny<ProductPriceSyncState>(), It.IsAny<CancellationToken>()))
            .Callback<ProductPriceSyncState, CancellationToken>((s, _) => savedStates.Add(s))
            .Returns(Task.CompletedTask);
        _repository
            .Setup(r => r.GetSyncStateAsync(It.IsAny<string>(), It.IsAny<PriceSyncTarget>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductPriceSyncState { ProductCode = "OCH001030", Status = PriceSyncStatus.InSync });
        var handler = new SetProductPriceHandler(_repository.Object, _currentUserService.Object);

        // Act
        var response = await handler.Handle(
            new SetProductPriceRequest { ProductCode = "OCH001030", PriceWithVat = 210.00m },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _repository.Verify(
            r => r.UpsertAsync(It.Is<ProductPrice>(p => p.PriceWithVat == 210.00m), It.IsAny<CancellationToken>()),
            Times.Once);
        savedStates.Should().HaveCount(2);
        savedStates.Should().OnlyContain(s => s.Status == PriceSyncStatus.Pending);
    }

    [Fact]
    public async Task stores_the_editing_user_on_the_price()
    {
        // Arrange
        _currentUserService
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-2", "Editing User", "editing.user@anela.cz", true));
        _repository
            .Setup(r => r.GetAsync("OCH001030", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductPrice { ProductCode = "OCH001030", PriceWithVat = 190.00m, VatRate = 21m, ModifiedBy = "price-sync" });
        _repository
            .Setup(r => r.GetSyncStateAsync(It.IsAny<string>(), It.IsAny<PriceSyncTarget>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductPriceSyncState { ProductCode = "OCH001030", Status = PriceSyncStatus.InSync });
        var handler = new SetProductPriceHandler(_repository.Object, _currentUserService.Object);

        // Act
        await handler.Handle(
            new SetProductPriceRequest { ProductCode = "OCH001030", PriceWithVat = 210.00m },
            CancellationToken.None);

        // Assert
        _repository.Verify(
            r => r.UpsertAsync(It.Is<ProductPrice>(p => p.ModifiedBy == "editing.user@anela.cz"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task returns_not_found_when_the_product_has_no_price_record()
    {
        // Arrange
        _repository
            .Setup(r => r.GetAsync("NOPE", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductPrice?)null);
        var handler = new SetProductPriceHandler(_repository.Object, _currentUserService.Object);

        // Act
        var response = await handler.Handle(
            new SetProductPriceRequest { ProductCode = "NOPE", PriceWithVat = 210.00m },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ProductPriceNotFound);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void validator_rejects_non_positive_prices(decimal price)
    {
        // Arrange
        var validator = new SetProductPriceRequestValidator();

        // Act
        var result = validator.Validate(new SetProductPriceRequest { ProductCode = "A", PriceWithVat = price });

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void validator_rejects_a_blank_product_code()
    {
        // Arrange
        var validator = new SetProductPriceRequestValidator();

        // Act
        var result = validator.Validate(new SetProductPriceRequest { ProductCode = "  ", PriceWithVat = 210.00m });

        // Assert
        result.IsValid.Should().BeFalse();
    }
}
