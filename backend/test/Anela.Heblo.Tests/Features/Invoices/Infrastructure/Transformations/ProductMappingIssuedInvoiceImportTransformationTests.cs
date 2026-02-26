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
}
