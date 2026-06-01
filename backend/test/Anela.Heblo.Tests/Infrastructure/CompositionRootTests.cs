using Anela.Heblo.API;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Anela.Heblo.Tests.Infrastructure;

/// <summary>
/// Validates the full DI container at composition time to catch lifetime mismatches
/// (e.g., singleton consuming scoped) and unresolvable services before they cause
/// runtime AggregateException crashes.
///
/// Uses appsettings.Test.json which provides all required stubs:
/// UseInMemoryDatabase, Hangfire.UseInMemoryStorage, UseMockAuth, etc.
/// </summary>
public class CompositionRootTests
{
    [Fact]
    public void ServiceContainer_ValidateOnBuild_NoLifetimeMismatchesOrUnresolvableServices()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.UseDefaultServiceProvider(options =>
                {
                    options.ValidateOnBuild = true;
                    options.ValidateScopes = true;
                });
            });

        var act = () => _ = factory.Services;

        act.Should().NotThrow(
            "the DI container must have no lifetime mismatches or unresolvable services; " +
            "fix the registration causing the AggregateException shown in the failure message");
    }
}
