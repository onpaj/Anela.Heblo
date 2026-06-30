using Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations;
using Anela.Heblo.Domain.Features.Invoices;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices.Infrastructure.Transformations;

public class GiftWithoutVATIssuedInvoiceImportTransformationTests
{
    private const string GiftCode = "GOODYDO0001";

    private readonly GiftWithoutVATIssuedInvoiceImportTransformation _transformation = new();

    [Fact]
    public async Task TransformAsync_WithGiftItem_FlagsItemAsNonStock()
    {
        // Arrange
        var invoiceDetail = new IssuedInvoiceDetail
        {
            Items = new List<IssuedInvoiceDetailItem>
            {
                new() { Code = GiftCode, Name = "Gift" }
            }
        };

        // Act
        var result = await _transformation.TransformAsync(invoiceDetail);

        // Assert
        Assert.Single(result.Items);
        Assert.True(result.Items[0].IsNonStock);
    }

    [Fact]
    public async Task TransformAsync_WithNonGiftItems_LeavesItemsAsStock()
    {
        // Arrange
        var invoiceDetail = new IssuedInvoiceDetail
        {
            Items = new List<IssuedInvoiceDetailItem>
            {
                new() { Code = "OTHER001", Name = "Product A" },
                new() { Code = "SHIPPING", Name = "Shipping" }
            }
        };

        // Act
        var result = await _transformation.TransformAsync(invoiceDetail);

        // Assert
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.False(item.IsNonStock));
    }

    [Fact]
    public async Task TransformAsync_WithMixedItems_FlagsOnlyGiftItem()
    {
        // Arrange
        var invoiceDetail = new IssuedInvoiceDetail
        {
            Items = new List<IssuedInvoiceDetailItem>
            {
                new() { Code = "OTHER001", Name = "Product A" },
                new() { Code = GiftCode, Name = "Gift" },
                new() { Code = "OTHER002", Name = "Product B" }
            }
        };

        // Act
        var result = await _transformation.TransformAsync(invoiceDetail);

        // Assert
        Assert.False(result.Items[0].IsNonStock);
        Assert.True(result.Items[1].IsNonStock);
        Assert.False(result.Items[2].IsNonStock);
    }

    [Fact]
    public async Task TransformAsync_WithEmptyItemsList_ReturnsInvoiceUnchanged()
    {
        // Arrange
        var invoiceDetail = new IssuedInvoiceDetail
        {
            Items = new List<IssuedInvoiceDetailItem>()
        };

        // Act
        var result = await _transformation.TransformAsync(invoiceDetail);

        // Assert
        Assert.Empty(result.Items);
    }
}
