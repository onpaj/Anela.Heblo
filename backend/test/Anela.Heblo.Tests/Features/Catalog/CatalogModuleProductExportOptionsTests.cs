using System.Collections.Generic;
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

/// <summary>
/// Regression guard for ADR-004: ProductExportOptions must be bound by CatalogModule
/// (the owner of both the options type and its sole consumer, ProductExportDownloadJob)
/// — never by the API layer. See memory/decisions/product-export-options-ownership.md.
/// </summary>
public class CatalogModuleProductExportOptionsTests
{
    private const string ExpectedUrl = "https://example.invalid/export";
    private const string ExpectedContainerName = "product-exports";

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProductExportOptions:Url"] = ExpectedUrl,
                ["ProductExportOptions:ContainerName"] = ExpectedContainerName,
            })
            .Build();

    [Fact]
    public void AddCatalogModule_BindsProductExportOptions_FromConfigurationSection()
    {
        // Arrange — only CatalogModule wires DI; the API layer is intentionally NOT involved.
        // Pre-seed ILogger<> so any logger-injecting registration the module makes can resolve.
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddCatalogModule(BuildConfiguration());

        // Act
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ProductExportOptions>>().Value;

        // Assert — round-trip both fields. Fails closed if the Configure<T> call is deleted
        // from CatalogModule or repointed at the wrong configuration section name.
        Assert.Equal(ExpectedUrl, options.Url);
        Assert.Equal(ExpectedContainerName, options.ContainerName);
    }
}
