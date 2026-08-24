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
    private const string KnownGuidPpl = "11111111-1111-1111-1111-111111111111";
    private const string KnownGuidZasilkovna = "22222222-2222-2222-2222-222222222222";
    private const string UnknownGuid = "99999999-9999-9999-9999-999999999999";

    private static ShippingMethodMapper CreateMapper(
        Dictionary<string, ShippingMethod>? guidMap,
        out Mock<ILogger<ShippingMethodMapper>> loggerMock)
    {
        loggerMock = new Mock<ILogger<ShippingMethodMapper>>();
        var settings = Options.Create(new ShoptetApiSettings
        {
            InvoiceShippingGuidMap = guidMap ?? new Dictionary<string, ShippingMethod>()
        });
        return new ShippingMethodMapper(settings, loggerMock.Object);
    }

    private static void VerifyNoWarningLogged(Mock<ILogger<ShippingMethodMapper>> loggerMock)
    {
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    private static void VerifyWarningLoggedOnceContaining(Mock<ILogger<ShippingMethodMapper>> loggerMock, string expectedGuid)
    {
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(expectedGuid)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // FR-1: no shipping GUID -> PickUp, no warning logged

    [Fact]
    public void Map_ReturnsPickUp_WhenShippingIsNull()
    {
        // Arrange
        var mapper = CreateMapper(null, out var loggerMock);

        // Act
        var result = mapper.Map(null);

        // Assert
        result.Should().Be(ShippingMethod.PickUp);
        VerifyNoWarningLogged(loggerMock);
    }

    [Fact]
    public void Map_ReturnsPickUp_WhenGuidIsNull()
    {
        // Arrange
        var mapper = CreateMapper(null, out var loggerMock);

        // Act
        var result = mapper.Map(new ShoptetInvoiceShippingDto { Guid = null });

        // Assert
        result.Should().Be(ShippingMethod.PickUp);
        VerifyNoWarningLogged(loggerMock);
    }

    [Fact]
    public void Map_ReturnsPickUp_WhenGuidIsEmpty()
    {
        // Arrange
        var mapper = CreateMapper(null, out var loggerMock);

        // Act
        var result = mapper.Map(new ShoptetInvoiceShippingDto { Guid = "" });

        // Assert
        result.Should().Be(ShippingMethod.PickUp);
        VerifyNoWarningLogged(loggerMock);
    }

    // FR-2: known GUID -> configured method, no warning logged

    [Theory]
    [InlineData(KnownGuidPpl, ShippingMethod.PPL)]
    [InlineData(KnownGuidZasilkovna, ShippingMethod.Zasilkovna)]
    public void Map_ReturnsConfiguredMethod_WhenGuidIsKnown(string guid, ShippingMethod expected)
    {
        // Arrange
        var guidMap = new Dictionary<string, ShippingMethod>
        {
            [KnownGuidPpl] = ShippingMethod.PPL,
            [KnownGuidZasilkovna] = ShippingMethod.Zasilkovna
        };
        var mapper = CreateMapper(guidMap, out var loggerMock);

        // Act
        var result = mapper.Map(new ShoptetInvoiceShippingDto { Guid = guid });

        // Assert
        result.Should().Be(expected);
        VerifyNoWarningLogged(loggerMock);
    }

    // FR-3: unknown GUID -> PickUp + exactly one warning log containing the GUID

    [Fact]
    public void Map_ReturnsPickUpAndLogsWarning_WhenGuidIsUnknown_WithNonEmptyMap()
    {
        // Arrange
        var guidMap = new Dictionary<string, ShippingMethod>
        {
            [KnownGuidPpl] = ShippingMethod.PPL,
            [KnownGuidZasilkovna] = ShippingMethod.Zasilkovna
        };
        var mapper = CreateMapper(guidMap, out var loggerMock);

        // Act
        var result = mapper.Map(new ShoptetInvoiceShippingDto { Guid = UnknownGuid });

        // Assert
        result.Should().Be(ShippingMethod.PickUp);
        VerifyWarningLoggedOnceContaining(loggerMock, UnknownGuid);
    }

    [Fact]
    public void Map_ReturnsPickUpAndLogsWarning_WhenGuidIsUnknown_WithEmptyMap()
    {
        // Arrange
        var mapper = CreateMapper(new Dictionary<string, ShippingMethod>(), out var loggerMock);

        // Act
        var result = mapper.Map(new ShoptetInvoiceShippingDto { Guid = UnknownGuid });

        // Assert
        result.Should().Be(ShippingMethod.PickUp);
        VerifyWarningLoggedOnceContaining(loggerMock, UnknownGuid);
    }

    // FR-4: single-argument constructor works end-to-end (delegates to NullLogger)

    [Fact]
    public void Map_ReturnsPickUp_WhenConstructedWithSingleArgumentConstructor()
    {
        // Arrange
        var settings = Options.Create(new ShoptetApiSettings
        {
            InvoiceShippingGuidMap = new Dictionary<string, ShippingMethod>()
        });
        var mapper = new ShippingMethodMapper(settings);

        // Act
        var result = mapper.Map(new ShoptetInvoiceShippingDto { Guid = null });

        // Assert
        result.Should().Be(ShippingMethod.PickUp);
    }
}
