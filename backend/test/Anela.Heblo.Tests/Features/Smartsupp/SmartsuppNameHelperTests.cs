using Anela.Heblo.Application.Features.Smartsupp;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppNameHelperTests
{
    [Theory]
    [InlineData("Ondřej Pajgrt", "Ondřej")]
    [InlineData("Jana Nováková", "Jana")]
    [InlineData("Jana", "Jana")]
    [InlineData("", "Anela")]
    [InlineData(null, "Anela")]
    [InlineData("Unknown User", "Anela")]
    [InlineData("Anonymous", "Anela")]
    [InlineData("   ", "Anela")]
    public void ExtractFirstName_ReturnsExpected(string? input, string expected)
    {
        SmartsuppNameHelper.ExtractFirstName(input).Should().Be(expected);
    }
}
