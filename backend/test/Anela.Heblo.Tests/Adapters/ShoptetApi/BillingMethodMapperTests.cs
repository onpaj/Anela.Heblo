using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Mapping;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class BillingMethodMapperTests
{
    [Theory]
    [InlineData(1, BillingMethod.CoD)]
    [InlineData(2, BillingMethod.BankTransfer)]
    [InlineData(3, BillingMethod.Cash)]
    [InlineData(4, BillingMethod.CreditCard)]
    public void Map_ResolvesByDocumentedNumericId(int id, BillingMethod expected)
    {
        // Arrange
        var dto = new ShoptetBillingMethodDto { Id = id, Name = "anything" };

        // Act
        var result = new BillingMethodMapper().Map(dto);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Map_ReturnsBankTransfer_WhenBillingMethodIsNull()
    {
        var result = new BillingMethodMapper().Map((ShoptetBillingMethodDto?)null);

        result.Should().Be(BillingMethod.BankTransfer);
    }

    [Fact]
    public void Map_FallsBackToName_WhenIdIsUnknownButNameIsKnown()
    {
        // Arrange — id 0 (missing/unexpected), name still resolvable
        var dto = new ShoptetBillingMethodDto { Id = 0, Name = "Kartou" };

        // Act
        var result = new BillingMethodMapper().Map(dto);

        // Assert
        result.Should().Be(BillingMethod.CreditCard);
    }

    [Fact]
    public void Map_ReturnsBankTransfer_WhenIdAndNameAreUnknown()
    {
        var dto = new ShoptetBillingMethodDto { Id = 99, Name = "Mystery method" };

        var result = new BillingMethodMapper().Map(dto);

        result.Should().Be(BillingMethod.BankTransfer);
    }
}
