using Anela.Heblo.Application.Features.ProcessDocs;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProcessDocs;

public class ProcessDocParserTests
{
    private const string Valid = """
        ---
        process: calc-margins
        kind: calculation
        summary: Computes M0-M3 margins.
        owns:
          - backend/src/**/Margins/**
        verified_at: "1d75813bb"
        related:
          - sync-flexi-analytics
        ---

        # Margins

        ## Purpose
        Text.
        """;

    [Fact]
    public void Parse_ValidDoc_MapsFrontmatterAndKeepsFullMarkdown()
    {
        var doc = ProcessDocParser.Parse("calc-margins", Valid);

        Assert.Equal("calc-margins", doc.Name);
        Assert.Equal("calculation", doc.Kind);
        Assert.Equal("Computes M0-M3 margins.", doc.Summary);
        Assert.Equal(["backend/src/**/Margins/**"], doc.Owns);
        Assert.Equal("1d75813bb", doc.VerifiedAt);
        Assert.Equal(["sync-flexi-analytics"], doc.Related);
        Assert.Contains("## Purpose", doc.Markdown);
        Assert.DoesNotContain("verified_at", doc.Markdown);
    }

    [Fact]
    public void Parse_NoFrontmatter_Throws()
    {
        Assert.Throws<FormatException>(() => ProcessDocParser.Parse("calc-x", "# just markdown"));
    }

    [Fact]
    public void Parse_NameMismatch_Throws()
    {
        Assert.Throws<FormatException>(() => ProcessDocParser.Parse("calc-other", Valid));
    }

    [Fact]
    public void Parse_MissingSummary_Throws()
    {
        Assert.Throws<FormatException>(() =>
            ProcessDocParser.Parse("calc-margins", Valid.Replace("summary: Computes M0-M3 margins.\n", "")));
    }
}
