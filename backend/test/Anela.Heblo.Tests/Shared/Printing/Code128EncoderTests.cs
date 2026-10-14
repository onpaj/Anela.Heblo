using Anela.Heblo.Application.Shared.Printing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Shared.Printing;

public class Code128EncoderTests
{
    private static readonly int[] StartBPattern = { 2, 1, 1, 2, 1, 4 };
    private static readonly int[] StopPattern = { 2, 3, 3, 1, 1, 1, 2 };

    [Theory]
    [InlineData("R-DEO001001M")]
    [InlineData("Wikipedia")]
    [InlineData(" ~")]
    public void CanEncode_ReturnsTrue_ForPrintableAscii(string text)
    {
        Code128Encoder.CanEncode(text).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("R-ŽLUŤ01")]
    [InlineData("R-DEO\t01")]
    public void CanEncode_ReturnsFalse_ForEmptyOrNonPrintableAscii(string? text)
    {
        Code128Encoder.CanEncode(text).Should().BeFalse();
    }

    [Fact]
    public void ComputeChecksum_MatchesReferenceValue_ForWikipedia()
    {
        // Start B (104) + Σ position × (char − 32), mod 103 = 88
        Code128Encoder.ComputeChecksum("Wikipedia").Should().Be(88);
    }

    [Fact]
    public void EncodeModules_StartsWithStartB_AndEndsWithStop()
    {
        var modules = Code128Encoder.EncodeModules("R-DEO001001M");

        modules.Take(6).Should().Equal(StartBPattern);
        modules.TakeLast(7).Should().Equal(StopPattern);
    }

    [Fact]
    public void EncodeModules_EncodesFirstDataCharacterAfterStart()
    {
        // 'R' = value 50 = pattern 2 3 1 1 3 1
        var modules = Code128Encoder.EncodeModules("R");

        modules.Skip(6).Take(6).Should().Equal(2, 3, 1, 1, 3, 1);
    }

    [Theory]
    [InlineData("R-DEO001001M")]
    [InlineData("A")]
    public void EncodeModules_TotalWidthIsElevenModulesPerSymbolPlusStop(string text)
    {
        var modules = Code128Encoder.EncodeModules(text);

        // start + data + checksum symbols of 11 modules, stop of 13 modules
        modules.Sum().Should().Be((text.Length + 2) * 11 + 13);
        modules.Count.Should().Be((text.Length + 2) * 6 + 7, "widths alternate bar/space and start and end with a bar");
    }

    [Fact]
    public void EncodeModules_Throws_ForUnencodableText()
    {
        var act = () => Code128Encoder.EncodeModules("R-ŽLUŤ01");

        act.Should().Throw<ArgumentException>();
    }
    [Fact]
    public void EncodeModules_MatchesIndependentReference_ForWikipedia()
    {
        // Reference produced by the zxing-cpp Code 128 writer (set B throughout), bars and spaces alternating.
        const string expected =
            "2112143113211421122412111421121112421122141412211421121211244212112331112";

        var modules = Code128Encoder.EncodeModules("Wikipedia");

        string.Concat(modules).Should().Be(expected);
    }

    [Fact]
    public void EncodeModules_EveryPrintableCharacter_YieldsWellFormedSymbol()
    {
        for (var c = ' '; c <= '~'; c++)
        {
            var symbol = Code128Encoder.EncodeModules(c.ToString()).Skip(6).Take(6).ToList();

            symbol.Sum().Should().Be(11, $"symbol for '{c}' must be 11 modules wide");
            ((symbol[0] + symbol[2] + symbol[4]) % 2).Should().Be(0, $"bars of '{c}' must total an even width");
        }
    }
}
