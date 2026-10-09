using Anela.Heblo.Application.Features.Analytics.Services;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics.Services;

public class TimeWindowParserTests
{
    [Fact]
    public void SupportedTimeWindows_ContainsExactlyTheFiveKnownValues()
    {
        TimeWindowParser.SupportedTimeWindows.Should().BeEquivalentTo(new[]
        {
            "current-year",
            "current-and-previous-year",
            "last-6-months",
            "last-12-months",
            "last-24-months"
        });
    }
}
