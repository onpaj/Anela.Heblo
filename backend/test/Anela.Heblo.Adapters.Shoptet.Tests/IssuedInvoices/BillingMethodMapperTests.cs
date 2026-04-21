using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Shoptet.Tests.IssuedInvoices;

public class BillingMethodMapperTests
{
    private readonly BillingMethodMapper _sut = new();

    [Theory]
    [InlineData("bankTransfer", BillingMethod.BankTransfer)]
    [InlineData("cash", BillingMethod.Cash)]
    [InlineData("cashOnDelivery", BillingMethod.CoD)]
    [InlineData("creditCard", BillingMethod.CreditCard)]
    [InlineData("comgate", BillingMethod.Comgate)]
    public void Map_KnownCodes_ReturnCorrectEnum(string shoptetCode, BillingMethod expected)
    {
        _sut.Map(shoptetCode).Should().Be(expected);
    }

    [Theory]
    [InlineData("BANKTRANSFER", BillingMethod.BankTransfer)]
    [InlineData("CashOnDelivery", BillingMethod.CoD)]
    [InlineData("CREDITCARD", BillingMethod.CreditCard)]
    public void Map_KnownCodes_AreCaseInsensitive(string shoptetCode, BillingMethod expected)
    {
        _sut.Map(shoptetCode).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknownPaymentMethod")]
    public void Map_UnknownOrNullCode_ReturnsBankTransferDefault(string? shoptetCode)
    {
        _sut.Map(shoptetCode).Should().Be(BillingMethod.BankTransfer,
            "Default matches Playwright's PaymentMethodResolver fallback");
    }
}
