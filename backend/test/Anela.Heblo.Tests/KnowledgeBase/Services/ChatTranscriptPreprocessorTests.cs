using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class ChatTranscriptPreprocessorTests
{
    private static ChatTranscriptPreprocessor Create(KnowledgeBaseOptions? options = null)
        => new(Options.Create(options ?? new KnowledgeBaseOptions()));

    [Fact]
    public void Clean_RemovesGreeting()
    {
        var preprocessor = Create();
        var input =
            "Anela: Vítejte ve světě Anela 🌿🌿 Rádi Vám poradíme s péčí o pleť i s potížemi, které Vás trápí. Napište nám, jsme tu pro Vás!\n" +
            "Zákazník: mám problém s akné";

        var result = preprocessor.Clean(input);

        Assert.DoesNotContain("Vítejte ve světě Anela", result);
        Assert.Contains("mám problém s akné", result);
    }

    [Fact]
    public void Clean_RemovesMetadataHeader()
    {
        var preprocessor = Create();
        var input = "datum: 04.11.2025 zákazník: Zákazník-0364\nAnela: Jak Vám mohu pomoci?";

        var result = preprocessor.Clean(input);

        Assert.DoesNotContain("datum:", result);
        Assert.Contains("Jak Vám mohu pomoci?", result);
    }

    [Fact]
    public void Clean_RemovesAnonymizedCustomerId()
    {
        var preprocessor = Create();
        var input = "Zákazník-0042: Mám suchou pleť";

        var result = preprocessor.Clean(input);

        Assert.DoesNotContain("Zákazník-0042", result);
        Assert.Contains("Mám suchou pleť", result);
    }

    [Fact]
    public void Clean_RemovesAllPatternsCombined()
    {
        var preprocessor = Create();
        var input =
            "datum: 04.11.2025 zákazník: Zákazník-0364\n" +
            "Anela: Vítejte ve světě Anela 🌿🌿 Rádi Vám poradíme s péčí o pleť i s potížemi, které Vás trápí. Napište nám, jsme tu pro Vás!\n" +
            "Zákazník-0364: mám akné";

        var result = preprocessor.Clean(input);

        Assert.DoesNotContain("datum:", result);
        Assert.DoesNotContain("Vítejte ve světě Anela", result);
        Assert.DoesNotContain("Zákazník-0364", result);
        Assert.Contains("mám akné", result);
    }

    [Fact]
    public void Clean_NoMatchingPatterns_ReturnsTextUnchanged()
    {
        var preprocessor = Create();
        var input = "Anela: Bisabolol je vhodný pro citlivou pleť.";

        var result = preprocessor.Clean(input);

        Assert.Equal("Anela: Bisabolol je vhodný pro citlivou pleť.", result);
    }

    [Fact]
    public void Clean_CustomPattern_IsApplied()
    {
        var options = new KnowledgeBaseOptions
        {
            PreprocessorPatterns = [@"REMOVE_ME"]
        };
        var preprocessor = Create(options);
        var input = "Some text REMOVE_ME more text";

        var result = preprocessor.Clean(input);

        Assert.DoesNotContain("REMOVE_ME", result);
        Assert.Contains("Some text", result);
    }
}
