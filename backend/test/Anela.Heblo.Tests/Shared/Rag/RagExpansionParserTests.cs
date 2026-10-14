using Anela.Heblo.Application.Shared.Rag;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Shared.Rag;

public class RagExpansionParserTests
{
    [Fact]
    public void Parse_ValidJson_ReturnsExpandedAndNormalisedTags()
    {
        const string raw = """{"expanded": "Problém: akné na čele", "problemTags": ["Akné"], "productTags": ["Hořčíkové máslo"]}""";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeFalse();
        result.Expansion.QueryToEmbed.Should().Be("Problém: akné na čele");
        result.Expansion.ProblemTags.Should().Equal("akné");
        result.Expansion.ProductTags.Should().Equal("hořčíkové máslo");
    }

    [Fact]
    public void Parse_FencedJson_StripsFenceAndParses()
    {
        const string raw = "```json\n{\"expanded\": \"Problém: růžovka\", \"problemTags\": [\"růžovka\"], \"productTags\": []}\n```";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeFalse();
        result.Expansion.QueryToEmbed.Should().Be("Problém: růžovka");
        result.Expansion.ProblemTags.Should().Equal("růžovka");
        result.Expansion.ProductTags.Should().BeEmpty();
    }

    [Fact]
    public void Parse_PlainText_ReturnsRawUntaggedWithoutMalformedFlag()
    {
        const string raw = "Problém: popraskané ruce\nKontext: suchá kůže";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeFalse();
        result.Expansion.QueryToEmbed.Should().Be(raw);
        result.Expansion.ProblemTags.Should().BeEmpty();
        result.Expansion.ProductTags.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ProseBeforeJson_FallsBackToRawUntagged()
    {
        const string raw = "Tady je výstup: {\"expanded\": \"x\", \"problemTags\": [], \"productTags\": []}";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeFalse();
        result.Expansion.QueryToEmbed.Should().Be(raw);
        result.Expansion.ProblemTags.Should().BeEmpty();
    }

    [Fact]
    public void Parse_TruncatedJson_FlagsMalformedAndReturnsRaw()
    {
        const string raw = "{\"expanded\": \"Problém: akné\", \"problemTags\": [\"akn";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeTrue();
        result.Expansion.QueryToEmbed.Should().Be(raw);
        result.Expansion.ProblemTags.Should().BeEmpty();
    }

    [Fact]
    public void Parse_BlankExpanded_FlagsMalformedAndReturnsRaw()
    {
        const string raw = """{"expanded": "  ", "problemTags": ["akné"], "productTags": []}""";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeTrue();
        result.Expansion.QueryToEmbed.Should().Be(raw);
        result.Expansion.ProblemTags.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NonStringTags_FlagsMalformedAndReturnsRaw()
    {
        const string raw = """{"expanded": "Problém: akné", "problemTags": [{"name": "akné"}], "productTags": []}""";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeTrue();
        result.Expansion.QueryToEmbed.Should().Be(raw);
    }

    [Fact]
    public void Parse_MissingTagArrays_ReturnsEmptyTags()
    {
        const string raw = """{"expanded": "Problém: akné"}""";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeFalse();
        result.Expansion.QueryToEmbed.Should().Be("Problém: akné");
        result.Expansion.ProblemTags.Should().BeEmpty();
        result.Expansion.ProductTags.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeTags_TrimsLowercasesCollapsesWhitespaceDedupesAndCaps()
    {
        var tags = new string?[] { " Akné ", "akné", "suchá   pleť", "", null, "a", "b", "c", "d", "e" };

        var result = RagExpansionParser.NormalizeTags(tags);

        result.Should().Equal("akné", "suchá pleť", "a", "b", "c");
    }

    [Fact]
    public void NormalizeTags_Null_ReturnsEmpty()
    {
        RagExpansionParser.NormalizeTags(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData(" AKNÉ ", "akné")]
    [InlineData("otoky   nohou", "otoky nohou")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void NormalizeTag_NormalisesSingleValue(string? input, string? expected)
    {
        RagExpansionParser.NormalizeTag(input).Should().Be(expected);
    }

    [Fact]
    public void Parse_LiteralNewlineAndTabInsideExpanded_ParsesAndKeepsControlChars()
    {
        var raw = "{\"expanded\": \"Problém: akné\nKontext:\tmastná pleť\", \"problemTags\": [\"Akné\"], \"productTags\": []}";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeFalse();
        result.Expansion.QueryToEmbed.Should().Be("Problém: akné\nKontext:\tmastná pleť");
        result.Expansion.ProblemTags.Should().Equal("akné");
    }

    [Fact]
    public void Parse_PrettyPrintedJsonWithNewlinesBetweenTokens_Parses()
    {
        var raw = "{\n  \"expanded\": \"Problém: akné\",\n  \"problemTags\": [\"akné\"],\n  \"productTags\": []\n}";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeFalse();
        result.Expansion.QueryToEmbed.Should().Be("Problém: akné");
        result.Expansion.ProblemTags.Should().Equal("akné");
    }

    [Fact]
    public void Parse_EscapedQuoteFollowedByLiteralNewline_Parses()
    {
        var raw = "{\"expanded\": \"Problém: \\\"suchá\\\" pleť\nKontext: zima\", \"problemTags\": [\"suchá pleť\"], \"productTags\": []}";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeFalse();
        result.Expansion.QueryToEmbed.Should().Be("Problém: \"suchá\" pleť\nKontext: zima");
        result.Expansion.ProblemTags.Should().Equal("suchá pleť");
    }

    [Fact]
    public void Parse_TruncatedJsonWithLiteralNewline_IsStillMalformedAfterRetry()
    {
        var raw = "{\"expanded\": \"Problém: akné\nKontext: mastná";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeTrue();
        result.Expansion.QueryToEmbed.Should().Be(raw);
        result.Expansion.ProblemTags.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeTag_TagOfMaxLength_IsKept()
    {
        var tag = new string('a', RagExpansionParser.MaxTagLength);

        RagExpansionParser.NormalizeTag(tag).Should().Be(tag);
    }

    [Fact]
    public void NormalizeTag_TagLongerThanMaxLength_IsDropped()
    {
        var tag = new string('a', RagExpansionParser.MaxTagLength + 1);

        RagExpansionParser.NormalizeTag(tag).Should().BeNull();
    }
}
