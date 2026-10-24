using Anela.Heblo.Adapters.Flexi.Invoices;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Rem.FlexiBeeSDK.Model.Invoices;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Invoices;

public class FlexiInvoiceMappingProfileCountryTests
{
    private static IMapper CreateMapper()
    {
        var config = new MapperConfiguration(
            c => c.AddProfile<FlexiInvoiceMappingProfile>(),
            NullLoggerFactory.Instance);
        return config.CreateMapper();
    }

    private static IssuedInvoiceDetail BuildInvoice(string? billingCountryCode) => new()
    {
        Code = "INV1",
        OrderCode = "ORD1",
        Customer = new InvoiceCustomer { Name = "Test" },
        BillingAddress = new InvoiceAddress { CountryCode = billingCountryCode! },
        DeliveryAddress = new InvoiceAddress { Street = "S", City = "C", CountryCode = "CZ" },
        Price = new InvoicePrice { CurrencyCode = "CZK", WithoutVat = 78m, WithVat = 94.38m, Vat = 16.38m },
        Items = new List<IssuedInvoiceDetailItem>(),
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankBillingCountry_DefaultsToCzechRepublic(string? countryCode)
    {
        var flexi = CreateMapper().Map<IssuedInvoiceDetailFlexiDto>(BuildInvoice(countryCode));

        flexi.CompanyState.Should().Be("code:CZ");
    }

    [Theory]
    [InlineData("SK")]
    [InlineData("DE")]
    public void FilledBillingCountry_IsPassedThrough(string countryCode)
    {
        var flexi = CreateMapper().Map<IssuedInvoiceDetailFlexiDto>(BuildInvoice(countryCode));

        flexi.CompanyState.Should().Be($"code:{countryCode}");
    }
}
