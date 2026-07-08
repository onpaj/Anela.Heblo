using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Pipeline;

public class ContextSanitizerTests
{
    [Fact]
    public void StripMarkdownLinks_ReplacesLinkWithItsText()
    {
        var result = ContextSanitizer.StripMarkdownLinks(
            "Doporučili jsme [Klidné nožky](https://anela.cz/klidne-nozky) na večer.");

        Assert.Equal("Doporučili jsme Klidné nožky na večer.", result);
    }

    [Fact]
    public void StripMarkdownLinks_StripsMultipleLinks()
    {
        var result = ContextSanitizer.StripMarkdownLinks(
            "[A](https://x.cz/a) a také [B](https://x.cz/b).");

        Assert.Equal("A a také B.", result);
    }

    [Fact]
    public void StripMarkdownLinks_TextWithoutLinks_ReturnedUnchanged()
    {
        const string text = "Problém: suchá pleť. Doporučení: hydratace.";

        Assert.Equal(text, ContextSanitizer.StripMarkdownLinks(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void StripMarkdownLinks_NullOrEmpty_ReturnedAsIs(string? input)
    {
        Assert.Equal(input, ContextSanitizer.StripMarkdownLinks(input!));
    }
}
