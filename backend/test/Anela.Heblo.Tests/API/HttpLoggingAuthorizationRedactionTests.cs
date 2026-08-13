using Anela.Heblo.API.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.API;

/// <summary>
/// Regression guard for the built-in HTTP logging config in AddCrossCuttingServices.
/// ASP.NET Core's HttpLoggingMiddleware logs the real value of any request header
/// explicitly added to HttpLoggingOptions.RequestHeaders, and redacts (omits the value
/// of) any header that is not listed. Authorization carries the live bearer token
/// (Entra ID access token, or the mock-auth token) for every authenticated request, so
/// it must never be added to that allow-list — see RequestLoggingMiddleware.IsSensitiveHeader
/// for the equivalent "never log this header's value" policy already enforced by the
/// project's own custom request-logging middleware.
/// </summary>
public class HttpLoggingAuthorizationRedactionTests
{
    private static HttpLoggingOptions BuildOptions()
    {
        var services = new ServiceCollection();
        services.AddCrossCuttingServices();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<HttpLoggingOptions>>().Value;
    }

    [Fact]
    public void RequestHeaders_DoesNotIncludeAuthorization()
    {
        var options = BuildOptions();

        options.RequestHeaders.Should().NotContain("Authorization",
            "the built-in HttpLoggingMiddleware logs the real value of any header explicitly " +
            "listed here; Authorization carries the live bearer token and must never be logged");
    }

    [Fact]
    public void RequestHeaders_StillIncludesUserAgent()
    {
        var options = BuildOptions();

        options.RequestHeaders.Should().Contain("User-Agent",
            "User-Agent logging is unrelated to this fix and must be preserved");
    }

    [Fact]
    public void ResponseHeaders_StillIncludesContentType()
    {
        var options = BuildOptions();

        options.ResponseHeaders.Should().Contain("Content-Type",
            "Content-Type response header logging is unrelated to this fix and must be preserved");
    }

    [Fact]
    public void LoggingFields_StillSetToAll()
    {
        var options = BuildOptions();

        options.LoggingFields.Should().Be(HttpLoggingFields.All,
            "removing Authorization from RequestHeaders must not narrow the overall logging scope");
    }
}
