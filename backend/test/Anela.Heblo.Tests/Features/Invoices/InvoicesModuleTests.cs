using Anela.Heblo.Application.Features.Invoices;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Domain.Features.Invoices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

public class InvoicesModuleTests
{
    private const string TestShoptetCode = "TEST-SHOPTET";
    private const string TestErpCode = "TEST-ERP";

    [Fact]
    public async Task AddInvoicesModule_BindsProductMappingOptions_AndTransformationUsesThem()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProductMapping:ShoptetCode"] = TestShoptetCode,
                ["ProductMapping:ErpCode"] = TestErpCode,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInvoicesModule(configuration);
        var provider = services.BuildServiceProvider();

        // Act — find the product-mapping transformation and run it against an invoice
        // whose only item carries the configured Shoptet code.
        var transformations = provider.GetServices<IIssuedInvoiceImportTransformation>().ToList();
        var productMapping = transformations
            .OfType<Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations.ProductMappingIssuedInvoiceImportTransformation>()
            .Single();

        var invoice = new IssuedInvoiceDetail
        {
            Items = new List<IssuedInvoiceDetailItem>
            {
                new IssuedInvoiceDetailItem { Code = TestShoptetCode, Name = "Test Product" }
            }
        };

        var result = await productMapping.TransformAsync(invoice);

        // Assert
        Assert.Equal(TestErpCode, result.Items.Single().Code);
    }

    [Fact]
    public void AddInvoicesModule_RegistersOptions_BoundFromConfiguration()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProductMapping:ShoptetCode"] = TestShoptetCode,
                ["ProductMapping:ErpCode"] = TestErpCode,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInvoicesModule(configuration);
        var provider = services.BuildServiceProvider();

        // Act
        var opts = provider.GetRequiredService<IOptions<ProductMappingOptions>>().Value;

        // Assert
        Assert.Equal(TestShoptetCode, opts.ShoptetCode);
        Assert.Equal(TestErpCode, opts.ErpCode);
    }
}
