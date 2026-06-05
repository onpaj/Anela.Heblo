using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.GetPackingOrder;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.ShoptetOrders;

public class GetPackingOrderHandlerTests
{
    private readonly Mock<IPackingOrderClient> _clientMock = new();

    private GetPackingOrderHandler CreateHandler(int packingStateId = 26) =>
        new(
            _clientMock.Object,
            Options.Create(new ShoptetOrdersSettings { PackingStateId = packingStateId }),
            NullLogger<GetPackingOrderHandler>.Instance);

    [Fact]
    public async Task Handle_OrderFound_ReturnsMappedResponse()
    {
        _clientMock
            .Setup(c => c.GetPackingOrderAsync("250001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackingOrder
            {
                Code = "250001",
                CustomerName = "Jan Novák",
                ShippingMethodName = "PPL (do ruky)",
                Cooling = Cooling.L1,
                IsCooled = true,
                StatusId = 26,
                CustomerNote = "Zabalit jako dárek",
                EshopNote = "Stálý zákazník",
                ShippingStreet = "Hlavní 123/4",
                ShippingCity = "Praha",
                ShippingZip = "110 00",
                Items = new List<PackingOrderItem>
                {
                    new() { Name = "Krém", Quantity = 2, ImageUrl = "https://img/p.jpg" },
                },
            });

        var response = await CreateHandler().Handle(
            new GetPackingOrderRequest { Code = "250001" }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Code.Should().Be("250001");
        response.CustomerName.Should().Be("Jan Novák");
        response.ShippingMethodName.Should().Be("PPL (do ruky)");
        response.Cooling.Should().Be(Cooling.L1);
        response.IsCooled.Should().BeTrue();
        response.CustomerNote.Should().Be("Zabalit jako dárek");
        response.EshopNote.Should().Be("Stálý zákazník");
        response.ShippingStreet.Should().Be("Hlavní 123/4");
        response.ShippingCity.Should().Be("Praha");
        response.ShippingZip.Should().Be("110 00");
        response.Items.Should().ContainSingle().Which.Name.Should().Be("Krém");
    }

    [Fact]
    public async Task Handle_WhenOrderIsInPackingState_ReturnsEligibleWithNullWarning()
    {
        _clientMock
            .Setup(c => c.GetPackingOrderAsync("ORD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackingOrder
            {
                Code = "ORD001",
                CustomerName = "Jan Novák",
                ShippingMethodName = "PPL",
                StatusId = 26,
                Items = [],
            });

        var result = await CreateHandler().Handle(
            new GetPackingOrderRequest { Code = "ORD001" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Eligibility.IsEligible.Should().BeTrue();
        result.Eligibility.WarningTitle.Should().BeNull();
        result.Eligibility.WarningBody.Should().BeNull();
        result.ShippingStreet.Should().BeNull();
        result.ShippingCity.Should().BeNull();
        result.ShippingZip.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenOrderIsNotInPackingState_ReturnsIneligibleWithCzechWarning()
    {
        _clientMock
            .Setup(c => c.GetPackingOrderAsync("ORD002", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackingOrder
            {
                Code = "ORD002",
                CustomerName = "Jana Nováková",
                ShippingMethodName = "PPL",
                StatusId = 99,
                Items = [],
            });

        var result = await CreateHandler().Handle(
            new GetPackingOrderRequest { Code = "ORD002" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Eligibility.IsEligible.Should().BeFalse();
        result.Eligibility.WarningTitle.Should().Be("Objednávka není ve stavu „Balí se“");
        result.Eligibility.WarningBody.Should().Be("Tuto objednávku nezpracovávejte, dokud nebude ve správném stavu.");
    }

    [Fact]
    public async Task Handle_OrderNotFound_ReturnsShoptetOrderNotFound()
    {
        _clientMock
            .Setup(c => c.GetPackingOrderAsync("999999", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackingOrder?)null);

        var response = await CreateHandler().Handle(
            new GetPackingOrderRequest { Code = "999999" }, CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ShoptetOrderNotFound);
        response.Params.Should().ContainKey("orderCode").WhoseValue.Should().Be("999999");
    }

    [Fact]
    public async Task Handle_ClientThrows_ReturnsInternalServerError()
    {
        _clientMock
            .Setup(c => c.GetPackingOrderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet unavailable"));

        var response = await CreateHandler().Handle(
            new GetPackingOrderRequest { Code = "250001" }, CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
    }
}
