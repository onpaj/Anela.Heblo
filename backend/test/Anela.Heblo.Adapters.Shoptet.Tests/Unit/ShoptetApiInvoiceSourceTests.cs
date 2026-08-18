using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class ShoptetApiInvoiceSourceTests
{
    private static ShoptetInvoiceMapper BuildMapper() =>
        new(new BillingMethodMapper(), new ShippingMethodMapper(Options.Create(new ShoptetApiSettings())));

    private static ShoptetApiInvoiceSource BuildSource(Mock<IShoptetInvoiceClient> client) =>
        new(client.Object, BuildMapper(), Mock.Of<ILogger<ShoptetApiInvoiceSource>>());

    private static ShoptetInvoiceDto BuildDto(string code, string? orderCode = null, string currency = "CZK") =>
        new()
        {
            Code = code,
            OrderCode = orderCode ?? $"ORD-{code}",
            Items = new List<ShoptetInvoiceItemDto>(),
            Price = new ShoptetInvoicePriceDto { CurrencyCode = currency, WithVat = "0", WithoutVat = "0" },
        };

    [Fact]
    public async Task GetAllAsync_SingleInvoiceModeFound_ReturnsSingleBatchWithMappedInvoice()
    {
        // Arrange
        var dto = BuildDto("INV-1", orderCode: "ORD-1");
        var client = new Mock<IShoptetInvoiceClient>();
        client.Setup(x => x.GetInvoiceAsync("INV-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = "REQ-1",
            InvoiceId = "INV-1",
        };

        var source = BuildSource(client);

        // Act
        var result = await source.GetAllAsync(query);

        // Assert
        result.Should().HaveCount(1);
        var batch = result.Single();
        batch.BatchId.Should().Be("REQ-1");
        batch.Invoices.Should().HaveCount(1);
        // ShoptetInvoiceMapper.Map swaps Code/OrderCode: mapped.OrderCode = src.Code.
        // Asserting on OrderCode (not Code) proves the real mapper ran on this exact DTO.
        batch.Invoices[0].OrderCode.Should().Be("INV-1");

        client.Verify(
            x => x.ListInvoicesAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        client.Verify(
            x => x.GetInvoiceAsync("INV-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
