using Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations;
using Anela.Heblo.Domain.Features.Invoices;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices.Infrastructure.Transformations;

public class ProductMappingIssuedInvoiceImportTransformationTests
{
    private const string OriginalCode = "TEST001";
    private const string NewCode = "NEW001";

    private readonly ProductMappingIssuedInvoiceImportTransformation _transformation;

    public ProductMappingIssuedInvoiceImportTransformationTests()
    {
        _transformation = new ProductMappingIssuedInvoiceImportTransformation(OriginalCode, NewCode);
    }

    [Fact]
    public async Task TransformAsync_WithSingleMatchingItem_ReplacesProductCode()
    {
        // Arrange
        var invoiceDetail = new IssuedInvoiceDetail
        {
            Items = new List<IssuedInvoiceDetailItem>
            {
                new IssuedInvoiceDetailItem { Code = OriginalCode, Name = "Test Product" }
            }
        };

        // Act
        var result = await _transformation.TransformAsync(invoiceDetail);

        // Assert
        Assert.Single(result.Items);
        Assert.Equal(NewCode, result.Items[0].Code);
        Assert.Equal("Test Product", result.Items[0].Name); // Other properties unchanged
    }

    [Fact]
    public async Task TransformAsync_WithMultipleMatchingItems_ReplacesAllOccurrences()
    {
        // Arrange
        var invoiceDetail = new IssuedInvoiceDetail
        {
            Items = new List<IssuedInvoiceDetailItem>
            {
                new IssuedInvoiceDetailItem { Code = OriginalCode, Name = "Product A" },
                new IssuedInvoiceDetailItem { Code = OriginalCode, Name = "Product B" },
                new IssuedInvoiceDetailItem { Code = OriginalCode, Name = "Product C" }
            }
        };

        // Act
        var result = await _transformation.TransformAsync(invoiceDetail);

        // Assert
        Assert.Equal(3, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(NewCode, item.Code));
        Assert.Equal("Product A", result.Items[0].Name);
        Assert.Equal("Product B", result.Items[1].Name);
        Assert.Equal("Product C", result.Items[2].Name);
    }

    [Fact]
    public async Task TransformAsync_WithNoMatchingItems_LeavesInvoiceUnchanged()
    {
        // Arrange
        var invoiceDetail = new IssuedInvoiceDetail
        {
            Items = new List<IssuedInvoiceDetailItem>
            {
                new IssuedInvoiceDetailItem { Code = "OTHER001", Name = "Product A" },
                new IssuedInvoiceDetailItem { Code = "OTHER002", Name = "Product B" }
            }
        };

        // Act
        var result = await _transformation.TransformAsync(invoiceDetail);

        // Assert
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("OTHER001", result.Items[0].Code);
        Assert.Equal("OTHER002", result.Items[1].Code);
        Assert.Equal("Product A", result.Items[0].Name);
        Assert.Equal("Product B", result.Items[1].Name);
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
