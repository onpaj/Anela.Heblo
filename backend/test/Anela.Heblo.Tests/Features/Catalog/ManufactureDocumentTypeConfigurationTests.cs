using Anela.Heblo.Application.Common;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

/// <summary>
/// The FlexiBee manufacture document type ids live in configuration precisely because the ERP
/// renumbered them once already (54/56 -> 65/67 on 2026-03-24) and the hardcoded reader dropped
/// six months of history unnoticed. These tests guard the binding itself, so a typo in
/// appsettings.json or a renamed property fails the build instead of quietly emptying the set.
/// </summary>
public class ManufactureDocumentTypeConfigurationTests
{
    [Fact]
    public void ShippedConfiguration_BindsAllFourManufactureDocumentTypes()
    {
        // Arrange
        var configuration = LoadApiConfiguration();
        var options = new DataSourceOptions();

        // Act
        configuration.GetSection(DataSourceOptions.ConfigKey).Bind(options);

        // Assert — legacy and current ids for both product and semi-product receipts
        options.ManufactureDocumentTypeIds.Should().BeEquivalentTo(new[] { 54, 56, 65, 67 });
    }

    [Fact]
    public void Binding_DoesNotDuplicateIds_BecauseCodeDefaultIsEmpty()
    {
        // ConfigurationBinder appends to an existing array instead of replacing it. A non-empty
        // code default would therefore bind to {54,56,65,67,54,56,65,67}, and since the history
        // group-by SUMS amounts per product+day, every manufactured quantity would double.
        var configuration = LoadApiConfiguration();
        var options = new DataSourceOptions();

        new DataSourceOptions().ManufactureDocumentTypeIds.Should().BeEmpty(
            "a non-empty code default is silently appended to by the configuration binder");

        configuration.GetSection(DataSourceOptions.ConfigKey).Bind(options);

        options.ManufactureDocumentTypeIds.Should().OnlyHaveUniqueItems();
    }

    private static IConfigurationRoot LoadApiConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(FindApiDirectory(AppContext.BaseDirectory))
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

    private static string FindApiDirectory(string startPath)
    {
        var dir = new DirectoryInfo(startPath);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "backend", "src", "Anela.Heblo.API");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not locate backend/src/Anela.Heblo.API from {startPath}");
    }
}
