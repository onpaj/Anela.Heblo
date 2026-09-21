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
    public void Binding_MergesIntoExistingArray_WhichIsWhyTheCodeDefaultIsEmpty()
    {
        // Pins the framework behaviour the empty default exists to dodge: ConfigurationBinder
        // MERGES into an existing array index-wise instead of replacing it. Binding the shipped
        // four ids onto an already-populated instance therefore yields eight entries, and since
        // the history group-by SUMS amounts per product+day, every quantity would double.
        // Asserted against a pre-seeded instance so a framework upgrade that changes this
        // breaks here loudly rather than in the manufacture numbers.
        var configuration = LoadApiConfiguration();
        var seeded = new DataSourceOptions
        {
            ManufactureDocumentTypeIds = new[] { 54, 56, 65, 67 }
        };

        configuration.GetSection(DataSourceOptions.ConfigKey).Bind(seeded);

        seeded.ManufactureDocumentTypeIds.Should().HaveCount(8,
            "the binder merges rather than replaces, so the code default must stay empty");
    }

    [Fact]
    public void EnvironmentOverride_MergesIndexWise_LeavingDuplicatesForTheReaderToDrop()
    {
        // The empty code default does NOT close the override route. An operator doing the
        // natural zero-deploy fix after the next renumbering — setting just the current ids
        // in Key Vault / App Settings, which layer after appsettings.json — gets an
        // index-wise merge, not a replacement. FlexiManufactureHistoryClient de-duplicates
        // for exactly this reason; see GetHistoryAsync_DeduplicatesConfiguredDocumentTypeIds.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(FindApiDirectory(AppContext.BaseDirectory))
            .AddJsonFile("appsettings.json", optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DataSourceOptions.ConfigKey}:{nameof(DataSourceOptions.ManufactureDocumentTypeIds)}:0"] = "65",
                [$"{DataSourceOptions.ConfigKey}:{nameof(DataSourceOptions.ManufactureDocumentTypeIds)}:1"] = "67",
            })
            .Build();
        var options = new DataSourceOptions();

        configuration.GetSection(DataSourceOptions.ConfigKey).Bind(options);

        options.ManufactureDocumentTypeIds.Should().Equal(65, 67, 65, 67);
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
