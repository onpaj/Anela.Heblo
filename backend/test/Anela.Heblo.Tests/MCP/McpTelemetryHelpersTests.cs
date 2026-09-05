using Anela.Heblo.API.MCP;
using Xunit;

namespace Anela.Heblo.Tests.MCP;

public class McpTelemetryHelpersTests
{
    [Fact]
    public void TruncateSessionId_Null_ReturnsMissingSentinel()
    {
        Assert.Equal("(missing)", McpTelemetryHelpers.TruncateSessionId(null));
    }

    [Fact]
    public void TruncateSessionId_Empty_ReturnsMissingSentinel()
    {
        Assert.Equal("(missing)", McpTelemetryHelpers.TruncateSessionId(string.Empty));
    }

    [Fact]
    public void TruncateSessionId_ShorterThan8_ReturnsFullValue()
    {
        Assert.Equal("abc", McpTelemetryHelpers.TruncateSessionId("abc"));
    }

    [Fact]
    public void TruncateSessionId_Exactly8_ReturnsFullValue()
    {
        Assert.Equal("abcdefgh", McpTelemetryHelpers.TruncateSessionId("abcdefgh"));
    }

    [Fact]
    public void TruncateSessionId_LongerThan8_ReturnsFirstEightChars()
    {
        Assert.Equal("abcdefgh", McpTelemetryHelpers.TruncateSessionId("abcdefgh12345678"));
    }

    [Fact]
    public void TruncateSessionId_32CharValue_NeverExceedsEightChars()
    {
        var sessionId = new string('x', 32);
        var result = McpTelemetryHelpers.TruncateSessionId(sessionId);
        Assert.Equal(8, result.Length);
    }
}
