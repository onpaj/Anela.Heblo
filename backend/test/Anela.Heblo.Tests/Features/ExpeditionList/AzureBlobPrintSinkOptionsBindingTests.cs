using Anela.Heblo.Adapters.Azure;
using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

/// <summary>
/// Regression guard for the ExpeditionList/PrintPickingListOptions layering fix (issue #4335):
/// AzureBlobPrintSinkOptions must bind BlobConnectionString/BlobContainerName from the SAME
/// "ExpeditionList" configuration section that PrintPickingListOptions binds from, and
/// AddAzurePrintQueueSinkInfrastructure must construct BlobContainerClient from it.
/// </summary>
public class AzureBlobPrintSinkOptionsBindingTests
{
    // Azurite development storage connection string — never actually connects, just parses.
    private const string DevelopmentBlobConnectionString =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExpeditionList:BlobConnectionString"] = DevelopmentBlobConnectionString,
                ["ExpeditionList:BlobContainerName"] = "expedition-lists-test",
            })
            .Build();

    [Fact]
    public void AddAzurePrintQueueSinkInfrastructure_BindsAzureBlobPrintSinkOptions_FromExpeditionListSection()
    {
        // Arrange
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();

        // Act
        services.AddAzurePrintQueueSinkInfrastructure(configuration);
        var provider = services.BuildServiceProvider();

        // Assert — the new options type picks up both keys from the shared "ExpeditionList" section
        var options = provider.GetRequiredService<IOptions<AzureBlobPrintSinkOptions>>().Value;
        Assert.Equal(DevelopmentBlobConnectionString, options.BlobConnectionString);
        Assert.Equal("expedition-lists-test", options.BlobContainerName);
    }

    [Fact]
    public void AddAzurePrintQueueSinkInfrastructure_ConstructsBlobContainerClient_FromAzureBlobPrintSinkOptions()
    {
        // Arrange
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();

        // Act
        services.AddAzurePrintQueueSinkInfrastructure(configuration);
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<BlobContainerClient>();

        // Assert — connection-string account name and configured container name both flow through
        Assert.Equal("devstoreaccount1", client.AccountName);
        Assert.Equal("expedition-lists-test", client.Name);
    }

    [Fact]
    public void AzureBlobPrintSinkOptions_BlobContainerName_DefaultsToExpeditionLists()
    {
        // Assert — default must match the pre-refactor PrintPickingListOptions.BlobContainerName
        // default exactly, since Development leaves this key unset and relies on the class default.
        Assert.Equal("expedition-lists", new AzureBlobPrintSinkOptions().BlobContainerName);
    }
}
