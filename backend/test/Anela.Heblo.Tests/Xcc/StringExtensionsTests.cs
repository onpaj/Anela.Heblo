using Anela.Heblo.Xcc;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Xcc;

public class StringExtensionsTests
{
    [Theory]
    [InlineData("čokoláda", "cokolada")]
    [InlineData("ČOKOLÁDA", "cokolada")]
    [InlineData("Přírodní mýdlo", "prirodni mydlo")]
    [InlineData("Šampón", "sampon")]
    [InlineData("Ženšen", "zensen")]
    [InlineData("Řepík", "repik")]
    [InlineData("krém", "krem")]
    [InlineData("KRÉM na ruce", "krem na ruce")]
    [InlineData("Těžký případ", "tezky pripad")]
    [InlineData("Ďábel", "dabel")]
    [InlineData("Ťapka", "tapka")]
    [InlineData("Něco jiného", "neco jineho")]
    public void NormalizeForSearch_Should_Remove_Diacritics_And_Lowercase(string input, string expected)
    {
        // Act
        var result = input.NormalizeForSearch();
        
        // Assert
        result.Should().Be(expected);
    }
    
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void NormalizeForSearch_Should_Return_Empty_For_NullOrWhitespace(string input)
    {
        // Act
        var result = input.NormalizeForSearch();
        
        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeForSearch_Should_Return_Empty_For_Null()
    {
        // Act
        var result = ((string)null!).NormalizeForSearch();
        
        // Assert
        result.Should().BeEmpty();
    }
    
    [Fact]
    public void NormalizeForSearch_Should_Preserve_Numbers_And_Special_Characters()
    {
        // Arrange
        var input = "Krém 123 - speciální edice!";
        
        // Act
        var result = input.NormalizeForSearch();
        
        // Assert
        result.Should().Be("krem 123 - specialni edice!");
    }

    [Theory]
    [InlineData("MixedCASE", "mixedcase")]
    [InlineData("UPPERCASE", "uppercase")]
    [InlineData("lowercase", "lowercase")]
    public void NormalizeForSearch_Should_Convert_To_Lowercase(string input, string expected)
    {
        // Act
        var result = input.NormalizeForSearch();
        
        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("áéíóúůý", "aeiouu")]
    [InlineData("ÁÉÍÓÚŮÝ", "aeiouu")]
    [InlineData("čďěňřšťž", "cdenrstz")]
    [InlineData("ČĎĚŇŘŠŤŽ", "cdenrstz")]
    public void NormalizeForSearch_Should_Handle_All_Czech_Diacritics(string input, string expected)
    {
        // Act
        var result = input.NormalizeForSearch();
        
        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void NormalizeForSearch_Should_Handle_Mixed_Diacritics_And_Normal_Text()
    {
        // Arrange
        var input = "Český text s diakritikou a bez ní také";
        var expected = "cesky text s diakritikou a bez ni take";
        
        // Act
        var result = input.NormalizeForSearch();
        
        // Assert
        result.Should().Be(expected);
    }
}