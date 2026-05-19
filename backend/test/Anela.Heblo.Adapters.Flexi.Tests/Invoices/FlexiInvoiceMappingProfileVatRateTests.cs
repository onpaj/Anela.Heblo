using Anela.Heblo.Adapters.Flexi.Invoices;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Rem.FlexiBeeSDK.Model.Invoices;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Invoices;

public class FlexiInvoiceMappingProfileVatRateTests
{
    [Theory]
    [InlineData("high", "typSzbDph.dphZakl")]    // 21% standard rate
    [InlineData("first", "typSzbDph.dphSniz")]   // 12% reduced rate
    [InlineData("second", "typSzbDph.dphSniz2")] // 10% second reduced rate
    [InlineData("zero", "typSzbDph.dphOsv")]     // 0% exempt
    public void Map_TranslatesNamedVatRate_ToFlexiVatRateType(
        string namedVatRate, string expectedFlexiVatRateType)
    {
        // Arrange
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
            Price = new InvoicePrice { CurrencyCode = "CZK", WithoutVat = 100m, WithVat = 112m, Vat = 12m },
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
                        WithoutVat = 100m,
                        WithVat = 112m,
                        Vat = 12m,
                        TotalWithoutVat = 100m,
                        TotalWithVat = 112m,
                        VatRate = namedVatRate,
                    },
                }
            },
        };

        // Act
        var flexi = mapper.Map<IssuedInvoiceDetailFlexiDto>(domain);

        // Assert
        flexi.Items.Should().HaveCount(1);
        flexi.Items[0].VatRateType.Should().Be(expectedFlexiVatRateType);
    }
}
