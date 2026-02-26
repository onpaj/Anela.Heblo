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
}
