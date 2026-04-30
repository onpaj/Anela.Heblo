using Anela.Heblo.Adapters.Flexi.Invoices;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Rem.FlexiBeeSDK.Model.Invoices;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Invoices;

public class FlexiInvoiceMappingProfileDiscountTests
{
    [Fact]
    public void DiscountedItem_FlowsAsPostDiscountPricePerUnit_ToFlexi()
    {
        var config = new MapperConfiguration(
            c => c.AddProfile<FlexiInvoiceMappingProfile>(),
            NullLoggerFactory.Instance);
        var mapper = config.CreateMapper();

        var domain = new IssuedInvoiceDetail
        {
            Code = "INV1",
            OrderCode = "ORD1",
            Customer = new InvoiceCustomer { Name = "Test" },
            BillingAddress = new InvoiceAddress { Street = "S", City = "C", CountryCode = "CZ" },
            DeliveryAddress = new InvoiceAddress { Street = "S", City = "C", CountryCode = "CZ" },
            Price = new InvoicePrice { CurrencyCode = "CZK", WithoutVat = 78m, WithVat = 94.38m, Vat = 16.38m },
            Items = new List<IssuedInvoiceDetailItem>
            {
                new()
                {
                    Code = "P1",
                    Name = "Product 1",
                    Amount = 1m,
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
                }
            },
        };

        var flexi = mapper.Map<IssuedInvoiceDetailFlexiDto>(domain);

        flexi.Items.Should().HaveCount(1);
        flexi.Items[0].PricePerUnit.Should().Be(78m);
        flexi.Items[0].SumBase.Should().Be(78m);
        flexi.Items[0].SumTotal.Should().Be(94.38m);
    }
}
