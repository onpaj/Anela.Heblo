using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.OpenAI.Tests;

public class OpenAiAdapterServiceCollectionExtensionsTests
{
    // Resolves only IOptions<OpenAiEmbeddingOptions>; the IEmbeddingGenerator registration is left
    // unresolved on purpose so no ILoggerFactory/ILogger registration is needed here.
    private static OpenAiEmbeddingOptions BindOptions(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        var services = new ServiceCollection();
        services.AddOpenAiAdapter(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<OpenAiEmbeddingOptions>>().Value;
    }

    [Fact]
    public void AddOpenAiAdapter_NoEmbeddingKeys_UsesClassDefaults()
    {
        var options = BindOptions(("OpenAI:ApiKey", "test-key"));

        options.ApiKey.Should().Be("test-key");
        options.EmbeddingModel.Should().Be("text-embedding-3-large");
        options.EmbeddingDimensions.Should().Be(1536);
    }

    [Fact]
    public void AddOpenAiAdapter_OpenAiEmbeddingKeys_OverrideClassDefaults()
    {
        var options = BindOptions(
            ("OpenAI:ApiKey", "test-key"),
            ("OpenAI:EmbeddingModel", "text-embedding-3-small"),
            ("OpenAI:EmbeddingDimensions", "512"));

        options.EmbeddingModel.Should().Be("text-embedding-3-small");
        options.EmbeddingDimensions.Should().Be(512);
    }

    [Fact]
    public void AddOpenAiAdapter_KnowledgeBaseEmbeddingKeys_AreIgnored()
    {
        var options = BindOptions(
            ("OpenAI:ApiKey", "test-key"),
            ("KnowledgeBase:EmbeddingModel", "text-embedding-3-small"),
            ("KnowledgeBase:EmbeddingDimensions", "512"));

        options.EmbeddingModel.Should().Be(
            "text-embedding-3-large",
            "the adapter fallback must no longer be scoped to the KnowledgeBase feature's config");
        options.EmbeddingDimensions.Should().Be(1536);
    }
}
