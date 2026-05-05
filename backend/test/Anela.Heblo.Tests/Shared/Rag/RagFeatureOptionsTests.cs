using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.Leaflet;
using Anela.Heblo.Application.Shared.Rag;
using FluentAssertions;
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

    // Minimal concrete subclass with no prompt override
    private sealed class ConcreteRagOptions : RagFeatureOptions { }
}
