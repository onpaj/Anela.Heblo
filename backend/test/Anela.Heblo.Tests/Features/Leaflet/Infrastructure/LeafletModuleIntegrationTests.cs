using Anela.Heblo.Application.Features.Leaflet;
using Anela.Heblo.Application.Features.Leaflet.Services;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Shared.Rag;
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
        services.AddSingleton(Mock.Of<IChatClient>());
        services.AddSingleton(Mock.Of<ILeafletDocumentRepository>());
        services.AddSingleton(Mock.Of<ILeafletGenerationRepository>());
        services.AddScoped<IWordWindowChunker, WordWindowChunker>();
        services.AddLeafletModule(configuration);
        return services;
    }

    [Fact]
    public void AddLeafletModule_resolves_indexing_service_and_options()
    {
        // Arrange
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Leaflet:OneDriveFolderMappings:0:DriveId"] = "test-drive",
            ["Leaflet:OneDriveFolderMappings:0:InboxPath"] = "/Leaflets/Inbox",
            ["Leaflet:OneDriveFolderMappings:0:ArchivedPath"] = "/Leaflets/Archived",
            ["Leaflet:OneDriveFolderMappings:0:DocumentType"] = nameof(DocumentType.Leaflet),
            ["Leaflet:ChatModel"] = "claude-sonnet-4-6",
            ["Leaflet:EmbeddingModel"] = "text-embedding-3-small",
        });

        var services = BuildBaseServices(config);
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();

        // Act & Assert
        var options = scope.ServiceProvider.GetRequiredService<IOptions<LeafletOptions>>();
        Assert.NotNull(options.Value);
        Assert.Single(options.Value.OneDriveFolderMappings);
        Assert.Equal("test-drive", options.Value.OneDriveFolderMappings[0].DriveId);

        var chunker = scope.ServiceProvider.GetRequiredService<IWordWindowChunker>();
        Assert.IsType<WordWindowChunker>(chunker);

        var indexingService = scope.ServiceProvider.GetRequiredService<ILeafletIndexingService>();
        Assert.IsType<LeafletIndexingService>(indexingService);

        var summarizer = scope.ServiceProvider.GetRequiredService<ILeafletChunkSummarizer>();
        Assert.IsType<LeafletChunkSummarizer>(summarizer);
    }
}
