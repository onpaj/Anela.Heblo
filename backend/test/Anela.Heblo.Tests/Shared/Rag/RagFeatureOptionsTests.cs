using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.Leaflet;
using Anela.Heblo.Application.Shared.Rag;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace Anela.Heblo.Tests.Shared.Rag;

public class RagFeatureOptionsTests
{
    [Fact]
    public void LeafletOptions_ToExpansionConfig_ReturnsLeafletPrompt()
    {
        var options = new LeafletOptions();
        var config = options.ToExpansionConfig();
        config.Prompt.Should().Contain("Produkt:");
        config.Prompt.Should().Contain("Klíčové ingredience:");
        config.Enabled.Should().BeTrue();
    }

    [Fact]
    public void KnowledgeBaseOptions_ToExpansionConfig_ReturnsKbPrompt()
    {
        var options = new KnowledgeBaseOptions();
        var config = options.ToExpansionConfig();
        config.Prompt.Should().Contain("Problém:");
        config.Prompt.Should().Contain("Dotaz:");
        config.Enabled.Should().BeTrue();
    }

    [Fact]
    public void RagFeatureOptions_BaseDefault_HasEmptyPrompt()
    {
        // Verifies that the base class does NOT hold feature-specific prompts
        // (derived classes must supply their own via constructor)
        var options = new ConcreteRagOptions();
        options.ToExpansionConfig().Prompt.Should().BeEmpty();
    }

    [Fact]
    public void ToEmbeddingOptions_CarriesConfiguredModelAndDimensions()
    {
        var options = new LeafletOptions
        {
            EmbeddingModel = "text-embedding-3-small",
            EmbeddingDimensions = 3072,
        };

        var embeddingOptions = options.ToEmbeddingOptions();

        embeddingOptions.ModelId.Should().Be("text-embedding-3-small");
        embeddingOptions.Dimensions.Should().Be(3072);
    }

    [Fact]
    public void ToEmbeddingOptions_UnsetValues_FallBackToClassDefaults()
    {
        var options = new KnowledgeBaseOptions();

        var embeddingOptions = options.ToEmbeddingOptions();

        embeddingOptions.ModelId.Should().Be("text-embedding-3-large");
        embeddingOptions.Dimensions.Should().Be(1536);
    }

    // Minimal concrete subclass with no prompt override
    private sealed class ConcreteRagOptions : RagFeatureOptions { }
}
