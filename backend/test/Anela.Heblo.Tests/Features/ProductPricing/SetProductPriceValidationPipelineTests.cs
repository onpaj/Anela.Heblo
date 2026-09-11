using Anela.Heblo.Application.Features.ProductPricing;
using Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

/// <summary>
/// Proves the 0.01 price floor is actually enforced end-to-end, not merely implemented.
///
/// This repo has no <c>AddValidatorsFromAssembly</c>: every validator and every
/// <c>ValidationBehavior</c> is registered by hand, per module. So the pipeline is wired
/// here by calling <see cref="ProductPricingModule.AddProductPricingModule"/> itself — a
/// future edit that drops either registration line has to fail this test rather than
/// silently letting a zero price through to two live systems.
/// </summary>
public class SetProductPriceValidationPipelineTests
{
    private readonly Mock<IEshopPriceListClient> _eshop = new();
    private readonly Mock<IErpPriceWriter> _erpWriter = new();
    private readonly Mock<IProductPriceErpClient> _erpReader = new();
    private readonly Mock<IProductVatRateProvider> _vatRates = new();
    private readonly Mock<IProductPriceChangeLogRepository> _changeLog = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    public SetProductPriceValidationPipelineTests()
    {
        _eshop.Setup(c => c.GetPriceWithVatAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(190.00m);
        _erpReader.Setup(c => c.GetAllAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>
            {
                new()
                {
                    ProductCode = "A",
                    ErpItemId = 11,
                    PriceWithVat = 190m,
                    PriceWithoutVat = 157.02m,
                    ErpPriceType = "bezDph",
                    VatRate = 21m,
                },
            });
        _vatRates.Setup(v => v.GetVatRatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { ["A"] = 21m });
        _currentUser.Setup(u => u.GetCurrentUser())
            .Returns(new CurrentUser("u1", "Ondra", "ondra@anela.cz", true));
    }

    private IMediator BuildMediator()
    {
        var services = new ServiceCollection();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SetProductPriceRequest>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // The subject of this test: the module's own registrations, not hand-rolled ones.
        services.AddProductPricingModule();

        // The handler's collaborators. Registered after the module so the change-log mock
        // wins over the real repository (which would need a DbContext).
        services.AddScoped(_ => _eshop.Object);
        services.AddScoped(_ => _erpWriter.Object);
        services.AddScoped(_ => _erpReader.Object);
        services.AddScoped(_ => _vatRates.Object);
        services.AddScoped(_ => _changeLog.Object);
        services.AddScoped(_ => _currentUser.Object);

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.004)]
    public async Task Send_WithAPriceBelowTheFloor_ThrowsBeforeAnythingIsWritten(decimal price)
    {
        // Arrange
        var mediator = BuildMediator();

        // Act
        var act = () => mediator.Send(
            new SetProductPriceRequest { ProductCode = "A", PriceWithVat = price }, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
        _eshop.Verify(c => c.SetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _erpWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Send_WithAnEmptyProductCode_ThrowsBeforeAnythingIsWritten()
    {
        // Arrange
        var mediator = BuildMediator();

        // Act
        var act = () => mediator.Send(
            new SetProductPriceRequest { ProductCode = "", PriceWithVat = 190.00m }, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
        _eshop.Verify(c => c.SetPriceWithVatAsync(It.IsAny<string>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Send_WithAValidRequest_ReachesTheHandler()
    {
        // Arrange
        var mediator = BuildMediator();

        // Act
        var response = await mediator.Send(
            new SetProductPriceRequest { ProductCode = "A", PriceWithVat = 210.00m }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _eshop.Verify(c => c.SetPriceWithVatAsync("A", 210.00m, It.IsAny<CancellationToken>()), Times.Once);
    }
}
