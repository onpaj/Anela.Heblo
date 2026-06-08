using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
using Anela.Heblo.Adapters.Cups.Features.ExpeditionList;
using Anela.Heblo.API.Extensions;
using Anela.Heblo.API.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Shared.Printing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Tests.API;

public class CombinedPrintQueueSinkRegistrationTests
{
    // Azurite development storage connection string — never actually connects, just parses.
    private const string DevelopmentBlobConnectionString =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

    private static ServiceProvider BuildProvider(string printSink)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExpeditionList:PrintSink"] = printSink,
                ["ExpeditionList:BlobConnectionString"] = DevelopmentBlobConnectionString,
                ["ExpeditionList:BlobContainerName"] = "expedition-lists",
                ["ExpeditionList:PrintQueueFolder"] = "/tmp",
                ["Cups:ServerUrl"] = "http://localhost:631",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(TimeProvider.System);
        services.Configure<PrintPickingListOptions>(
            configuration.GetSection(PrintPickingListOptions.ConfigurationKey));

        services.AddPrintQueueSink(configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Combined_ResolvesCombinedPrintQueueSink()
    {
        // Arrange
        using var provider = BuildProvider("Combined");
        using var scope = provider.CreateScope();

        // Act
        var sink = scope.ServiceProvider.GetRequiredService<IPrintQueueSink>();

        // Assert
        Assert.IsType<CombinedPrintQueueSink>(sink);
    }

    [Fact]
    public void Combined_KeyedAzureSlot_ResolvesAzureBlobPrintQueueSink()
    {
        // Arrange
        using var provider = BuildProvider("Combined");
        using var scope = provider.CreateScope();

        // Act
        var azure = scope.ServiceProvider.GetRequiredKeyedService<IPrintQueueSink>("azure");

        // Assert
        Assert.IsType<AzureBlobPrintQueueSink>(azure);
    }

    [Fact]
    public void Combined_KeyedCupsSlot_ResolvesCupsPrintQueueSink()
    {
        // Arrange
        using var provider = BuildProvider("Combined");
        using var scope = provider.CreateScope();

        // Act
        var cups = scope.ServiceProvider.GetRequiredKeyedService<IPrintQueueSink>("cups");

        // Assert
        Assert.IsType<CupsPrintQueueSink>(cups);
    }

    [Fact]
    public void FileSystem_ResolvesFileSystemPrintQueueSink()
    {
        // Arrange — regression guard: relocating CombinedPrintQueueSink must not touch the FileSystem arm.
        using var provider = BuildProvider("FileSystem");
        using var scope = provider.CreateScope();

        // Act
        var sink = scope.ServiceProvider.GetRequiredService<IPrintQueueSink>();

        // Assert
        Assert.IsType<FileSystemPrintQueueSink>(sink);
    }
}
