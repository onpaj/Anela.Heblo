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
        // Recovery must point at the Key Vault secret, not the (overridden) App Service env var.
        ex.Message.Should().Contain("Plaud--TokensJson");
        ex.Message.Should().Contain("plaud login");
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
