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
        ex.Message.Should().Contain("Plaud--TokensJson");
    }

    [Fact]
    public void PlaudAuthExpiredException_WithInnerException_SetsInnerException()
    {
        var inner = new InvalidOperationException("root cause");

        var ex = new PlaudAuthExpiredException("[AUTH_FAILED] Token invalid or expired", inner);

        ex.InnerException.Should().BeSameAs(inner);
        ex.Message.Should().Contain("AUTH_FAILED");
    }
}
