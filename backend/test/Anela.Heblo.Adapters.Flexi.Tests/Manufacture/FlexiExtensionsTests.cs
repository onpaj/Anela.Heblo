using Anela.Heblo.Adapters.Flexi.Manufacture;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Flexi.Tests.Manufacture;

public class FlexiExtensionsTests
{
    [Theory]
    [InlineData("code:PRODUCT123", "PRODUCT123")]
    [InlineData("code:  PRODUCT123  ", "PRODUCT123")]
    [InlineData("PRODUCT123", "PRODUCT123")]
    [InlineData("code:", "")]
    [InlineData("", "")]
    [InlineData(null, null)]
    [InlineData("  code:PRODUCT123  ", "PRODUCT123")]
    [InlineData("code:code:PRODUCT123", "code:PRODUCT123")]
    [InlineData("CODE:PRODUCT123", "CODE:PRODUCT123")]
    [InlineData("prefix:code:PRODUCT123", "prefix:code:PRODUCT123")]
    public void RemoveCodePrefix_ReturnsExpectedResult(string input, string expected)
    {
        // Act
        var result = input.RemoveCodePrefix();

        // Assert
        result.Should().Be(expected);
    }
}