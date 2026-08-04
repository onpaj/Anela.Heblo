using Anela.Heblo.Application.Features.LabelIdentification.Services;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.LabelIdentification;

public class LabelTextNormalizerTests
{
    [Fact]
    public void Strips_everything_up_to_and_including_the_ingredients_marker()
    {
        // The artwork carries a Czech job-name line above the sticker die-cut that is
        // never printed on the physical label. Left in, it pollutes the index with
        // tokens OCR can never match — and carries the size, breaking family identity.
        var raw = "Anela_Něžná paní Ovesná_15\n\n   Ingredients:\n Avena Sativa Kernel Extract";

        var result = LabelTextNormalizer.Normalize(raw);

        result.Should().Be("avena sativa kernel extract");
    }

    [Fact]
    public void Strips_job_name_stamp_when_it_trails_the_ingredient_text()
    {
        // Real PdfPig extraction (content-stream order) puts the stamp AFTER the
        // ingredient list, once per page — not before it as pdftotext's visual-order
        // extraction suggested. This is the real shape seen across all 37 reference
        // PDFs (e.g. KRE002015.pdf), so the strip must not depend on the stamp being
        // a document-level prefix.
        var raw = "Ingredients: Helianthus Annuus Seed Oil, Linalyl Acetate" +
                  "Anela_Malá čarodějka_15ml_kelimek-dno_32mm_bila-snimatelna";

        var result = LabelTextNormalizer.Normalize(raw);

        result.Should().Be("helianthus annuus seed oil, linalyl acetate");
    }

    [Fact]
    public void Joins_hyphenation_across_line_breaks()
    {
        var result = LabelTextNormalizer.Normalize("Ingredients: Toco-\npherol");

        result.Should().Be("tocopherol");
    }

    [Fact]
    public void Collapses_whitespace_and_lowercases()
    {
        var result = LabelTextNormalizer.Normalize("Ingredients:   Rosa   CANINA\n\n Seed  Extract ");

        result.Should().Be("rosa canina seed extract");
    }

    [Fact]
    public void Converts_en_dash_and_slash_to_separators_preserving_ingredient_commas()
    {
        var result = LabelTextNormalizer.Normalize(
            "Ingredients: Cannabidiol – Derived From Extract, Caprylic/Capric Triglyceride");

        result.Should().Be("cannabidiol derived from extract, caprylic capric triglyceride");
    }

    [Fact]
    public void Strips_diacritics_and_form_feed_control_characters()
    {
        var result = LabelTextNormalizer.Normalize("Ingredients: Růže Oil");

        result.Should().Be("r e oil");
    }

    [Fact]
    public void Is_case_insensitive_about_the_ingredients_marker()
    {
        var result = LabelTextNormalizer.Normalize("INGREDIENTS: Tocopherol");

        result.Should().Be("tocopherol");
    }

    [Fact]
    public void Leaves_text_untouched_when_no_ingredients_marker_is_present()
    {
        var result = LabelTextNormalizer.Normalize("Tocopherol, Limonene");

        result.Should().Be("tocopherol, limonene");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Returns_empty_for_blank_input(string? raw)
    {
        LabelTextNormalizer.Normalize(raw).Should().BeEmpty();
    }
}
