using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class MaterialContainerCodeGeneratorFormatTests
{
    [Theory]
    [InlineData(1, "M00000001")]
    [InlineData(123, "M00000123")]
    [InlineData(99999999, "M99999999")]
    public void Format_ProducesScanCompatibleCode(long seq, string expected)
    {
        var code = $"M{seq:D8}";

        code.Should().Be(expected);
        Regex.IsMatch(code, @"^M\d{8}$").Should().BeTrue();
    }
}
