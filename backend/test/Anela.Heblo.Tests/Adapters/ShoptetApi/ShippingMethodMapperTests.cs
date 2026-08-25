using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ShippingMethodMapperTests
{
    private static ShippingMethodMapper CreateMapper(
        Dictionary<string, ShippingMethod>? guidMap = null,
        Mock<ILogger<ShippingMethodMapper>>? loggerMock = null)
    {
        var settings = new ShoptetApiSettings
        {
            InvoiceShippingGuidMap = guidMap ?? new Dictionary<string, ShippingMethod>()
        };
        var options = Options.Create(settings);
        return loggerMock is null
            ? new ShippingMethodMapper(options)
            : new ShippingMethodMapper(options, loggerMock.Object);
    }

    [Fact]
    public void Map_ReturnsPickUp_WhenShippingIsNull()
    {
        var mapper = CreateMapper();

        var result = mapper.Map(null);

        result.Should().Be(ShippingMethod.PickUp);
    }

    [Fact]
    public void Map_ReturnsPickUp_WhenGuidIsEmpty()
    {
        var mapper = CreateMapper();
        var shipping = new ShoptetInvoiceShippingDto { Guid = "", Name = "Osobní odběr" };

        var result = mapper.Map(shipping);

        result.Should().Be(ShippingMethod.PickUp);
    }

    [Fact]
    public void Map_ReturnsConfiguredMethod_WhenGuidIsKnown()
    {
        const string guid = "known-guid";
        var mapper = CreateMapper(new Dictionary<string, ShippingMethod>
        {
            [guid] = ShippingMethod.PPL
        });
        var shipping = new ShoptetInvoiceShippingDto { Guid = guid, Name = "PPL" };

        var result = mapper.Map(shipping);

        result.Should().Be(ShippingMethod.PPL);
    }

    [Fact]
    public void Map_ReturnsPickUpAndLogsWarning_WhenGuidIsUnknown()
    {
        const string guid = "unknown-guid";
        var loggerMock = new Mock<ILogger<ShippingMethodMapper>>();
        var mapper = CreateMapper(loggerMock: loggerMock);
        var shipping = new ShoptetInvoiceShippingDto { Guid = guid, Name = "Mystery method" };

        var result = mapper.Map(shipping);

        result.Should().Be(ShippingMethod.PickUp);
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(guid)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
