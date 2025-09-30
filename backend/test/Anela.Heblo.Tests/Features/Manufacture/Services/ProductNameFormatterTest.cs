using Anela.Heblo.Application.Features.Manufacture.Services;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Services;

public class ProductNameFormatterTest
{
    private readonly ProductNameFormatter _formatter = new();

    [Theory]
    [InlineData("Důvěrný pan Jasmín - jemný krémový deodorant 30ml", "Důvěrný pan Jasmín")]
    [InlineData("Test product - something else", "Test product")]
    [InlineData("Krásný výrobek - extra popis 50ml", "Krásný výrobek")]
    public void FormatInternalNumber_OldFormat_WithDash_ShouldReturnPartBeforeDash(string input, string expected)
    {
        // Act
        var result = _formatter.ShortProductName(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Bílá noční teenka 30ml", "Bílá noční teenka")]
    [InlineData("Krémový deodorant 50ml", "Krémový deodorant")]
    [InlineData("Test produkt 250 ml", "Test produkt")]
    [InlineData("Nějaký výrobek 10ml", "Nějaký výrobek")]
    [InlineData("Výrobek s mezerami 100 ml", "Výrobek s mezerami")]
    public void FormatInternalNumber_NewFormat_WithMl_ShouldReturnNameWithoutMl(string input, string expected)
    {
        // Act
        var result = _formatter.ShortProductName(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Testovací výrobek TESTER", "Testovací výrobek")]
    [InlineData("Krém na ruce TESTER", "Krém na ruce")]
    [InlineData("Produkt TESTER", "Produkt")]
    public void FormatInternalNumber_WithTester_ShouldReturnNameWithoutTester(string input, string expected)
    {
        // Act
        var result = _formatter.ShortProductName(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Prostý název", "Prostý název")]
    [InlineData("Název bez objemu", "Název bez objemu")]
    [InlineData("Test", "Test")]
    [InlineData("", "")]
    public void FormatInternalNumber_NoSpecialFormat_ShouldReturnOriginal(string input, string expected)
    {
        // Act
        var result = _formatter.ShortProductName(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Bílá noční teenka  30ml", "Bílá noční teenka")]
    [InlineData("  Produkt s mezerami  50 ml  ", "Produkt s mezerami")]
    [InlineData("Test TESTER  ", "Test")]
    public void FormatInternalNumber_WithExtraSpaces_ShouldTrimResult(string input, string expected)
    {
        // Act
        var result = _formatter.ShortProductName(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatInternalNumber_DashTakesPrecedence_OverMlPattern()
    {
        // Arrange
        var input = "Důvěrný pan Jasmín - jemný krémový deodorant 30ml";
        var expected = "Důvěrný pan Jasmín";

        // Act
        var result = _formatter.ShortProductName(input);

        // Assert
        Assert.Equal(expected, result);
    }
}