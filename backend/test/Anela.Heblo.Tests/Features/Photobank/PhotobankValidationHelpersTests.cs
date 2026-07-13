using Anela.Heblo.Application.Features.Photobank.Validators;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class PhotobankValidationHelpersTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BeValidRegex_NullOrWhitespace_ReturnsTrue(string? pattern)
    {
        // Act
        var result = PhotobankValidationHelpers.BeValidRegex(pattern);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void BeValidRegex_ValidPattern_ReturnsTrue()
    {
        // Act
        var result = PhotobankValidationHelpers.BeValidRegex("^[a-z]+$");

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("[")]
    [InlineData("(unclosed")]
    public void BeValidRegex_InvalidPattern_ReturnsFalse(string pattern)
    {
        // Act
        var result = PhotobankValidationHelpers.BeValidRegex(pattern);

        // Assert
        Assert.False(result);
    }
}
