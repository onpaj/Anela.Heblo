using Anela.Heblo.Application.Features.Leaflet;
using Anela.Heblo.Application.Features.Leaflet.Services;
using Anela.Heblo.Domain.Features.Leaflet;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.Leaflet.Infrastructure;

public class LeafletModuleIntegrationTests
{
    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static IServiceCollection BuildBaseServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(Mock.Of<IEmbeddingGenerator<string, Embedding<float>>>());
        services.AddSingleton(Mock.Of<ILeafletRepository>());
        services.AddLeafletModule(configuration);
        return services;
    }

    [Fact]
    public void AddLeafletModule_resolves_chunker_indexing_and_options()
    {
        // Arrange
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Leaflet:DriveId"] = "test-drive",
            ["Leaflet:InboxPath"] = "/Leaflets/Inbox",
            ["Leaflet:ArchivedPath"] = "/Leaflets/Archived",
            ["Leaflet:ChatModel"] = "claude-sonnet-4-6",
            ["Leaflet:EmbeddingModel"] = "text-embedding-3-small",
        });

        var services = BuildBaseServices(config);
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();

        // Act & Assert
        var options = scope.ServiceProvider.GetRequiredService<IOptions<LeafletOptions>>();
        Assert.NotNull(options.Value);
        Assert.Equal("test-drive", options.Value.DriveId);

        var chunker = scope.ServiceProvider.GetRequiredService<ILeafletChunker>();
        Assert.IsType<LeafletChunker>(chunker);

        var indexingService = scope.ServiceProvider.GetRequiredService<ILeafletIndexingService>();
        Assert.IsType<LeafletIndexingService>(indexingService);
    }

    [Fact]
    public void AddLeafletModule_throws_on_missing_DriveId()
    {
        // Arrange — no DriveId in config
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Leaflet:InboxPath"] = "/Leaflets/Inbox",
            ["Leaflet:ArchivedPath"] = "/Leaflets/Archived",
            ["Leaflet:ChatModel"] = "claude-sonnet-4-6",
            ["Leaflet:EmbeddingModel"] = "text-embedding-3-small",
        });

        var services = BuildBaseServices(config);
        var provider = services.BuildServiceProvider();

        // Act & Assert — accessing .Value should throw OptionsValidationException
        // because DriveId is [Required] and ValidateDataAnnotations() is registered
        var options = provider.GetRequiredService<IOptions<LeafletOptions>>();
        Assert.Throws<OptionsValidationException>(() => _ = options.Value);
    }
}
