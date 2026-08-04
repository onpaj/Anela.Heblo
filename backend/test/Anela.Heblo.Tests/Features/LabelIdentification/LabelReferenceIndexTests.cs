using Anela.Heblo.Application.Features.LabelIdentification.Services;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.LabelIdentification;

public class LabelReferenceIndexTests
{
    private readonly LabelReferenceIndex _index = new();

    [Fact]
    public void Loads_twenty_five_families_covering_thirty_seven_product_codes()
    {
        _index.Entries.Should().HaveCount(25);
        _index.Entries.Sum(e => e.Codes.Count).Should().Be(37);
    }

    [Fact]
    public void Splits_into_twelve_two_size_families_and_thirteen_single_size_families()
    {
        _index.Entries.Count(e => e.Codes.Count > 1).Should().Be(12);
        _index.Entries.Count(e => e.Codes.Count == 1).Should().Be(13);
    }

    [Fact]
    public void Every_code_maps_back_to_its_family_prefix()
    {
        foreach (var entry in _index.Entries)
        {
            entry.Codes.Should().OnlyContain(code => code.StartsWith(entry.Family));
            entry.Family.Should().HaveLength(6);
        }
    }

    [Fact]
    public void Every_entry_has_normalized_text_and_precomputed_tokens()
    {
        _index.Entries.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.Normalized));
        _index.Entries.Should().OnlyContain(e => e.Tokens.Count > 0);
    }

    [Fact]
    public void Index_excludes_the_artwork_job_name_line()
    {
        // "Anela_Malá čarodějka_15ml_k" and friends are never printed on the sticker.
        _index.Entries.Should().OnlyContain(e => !e.Normalized.Contains("anela"));
    }

    [Fact]
    public void Known_two_size_family_exposes_both_variants()
    {
        var kre005 = _index.Entries.Single(e => e.Family == "KRE005");

        kre005.Codes.Should().BeEquivalentTo(new[] { "KRE005015", "KRE005030" });
    }
}
