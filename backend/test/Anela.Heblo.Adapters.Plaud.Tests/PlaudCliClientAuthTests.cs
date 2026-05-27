using FluentAssertions;

namespace Anela.Heblo.Adapters.Plaud.Tests;

public sealed class PlaudAuthExceptionTests
{
    [Fact]
    public void PlaudAuthExpiredException_StoresStderrInMessage()
    {
        const string stderr = "[AUTH_FAILED] Token invalid or expired";

        var ex = new PlaudAuthExpiredException(stderr);

        ex.Message.Should().Contain(stderr);
        ex.Message.Should().Contain("Plaud__TokensJson");
    }
}
