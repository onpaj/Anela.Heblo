using Anela.Heblo.Adapters.Flexi.Invoices;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Rem.FlexiBeeSDK.Model.Invoices;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Invoices;

public class FlexiInvoiceMappingProfileStoreTests
{
    private static IMapper CreateMapper()
    {
        var config = new MapperConfiguration(
            c => c.AddProfile<FlexiInvoiceMappingProfile>(),
            NullLoggerFactory.Instance);
        return config.CreateMapper();
    }

    private static IssuedInvoiceDetail BuildInvoice(IssuedInvoiceDetailItem item) => new()
    {
        Code = "INV1",
        OrderCode = "ORD1",
        Customer = new InvoiceCustomer { Name = "Test" },
        BillingAddress = new InvoiceAddress { Street = "S", City = "C", CountryCode = "CZ" },
        DeliveryAddress = new InvoiceAddress { Street = "S", City = "C", CountryCode = "CZ" },
        Price = new InvoicePrice { CurrencyCode = "CZK", WithoutVat = 78m, WithVat = 94.38m, Vat = 16.38m },
        Items = new List<IssuedInvoiceDetailItem> { item },
    };

    private static IssuedInvoiceDetailItem BuildItem(string code, bool isNonStock) => new()
    {
        Code = code,
        Name = "Item",
        Amount = 1m,
        IsNonStock = isNonStock,
        ItemPrice = new InvoicePrice
        {
            CurrencyCode = "CZK",
            WithoutVat = 78m,
            WithVat = 94.38m,
            Vat = 16.38m,
            TotalWithoutVat = 78m,
            TotalWithVat = 94.38m,
            VatRate = "high",
        },
    };

    [Fact]
    public void NonStockItem_OmitsWarehouse()
    {
        var mapper = CreateMapper();

        var flexi = mapper.Map<IssuedInvoiceDetailFlexiDto>(
            BuildInvoice(BuildItem("GOODYDO0001", isNonStock: true)));

        flexi.Items.Should().HaveCount(1);
        flexi.Items[0].Store.Should().BeNull();
    }

    [Fact]
    public void StockProductItem_AssignsWarehouse()
    {
        var mapper = CreateMapper();

        var flexi = mapper.Map<IssuedInvoiceDetailFlexiDto>(
            BuildInvoice(BuildItem("P1", isNonStock: false)));

        flexi.Items.Should().HaveCount(1);
        flexi.Items[0].Store.Should().Be("code:ZBOZI");
    }
}
