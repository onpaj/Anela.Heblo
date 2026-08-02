using Anela.Heblo.Adapters.Flexi.Lots;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Lots;

/// <summary>
/// Regression test: AddFlexiAdapter must register ILotsClient exactly once.
///
/// Bug: ILotsClient -> FlexiLotsClient was registered twice with conflicting
/// lifetimes (Singleton and Scoped). The container silently kept both
/// descriptors, so a direct resolution used whichever was registered last
/// while an IEnumerable&lt;ILotsClient&gt;/GetServices resolution returned two
/// instances with mismatched lifetimes.
///
/// Fix: keep the single Singleton registration and delete the duplicate
/// Scoped one.
/// </summary>
public class FlexiAdapterLotsClientRegistrationTests
{
    [Fact]
    public void AddFlexiAdapter_RegistersLotsClientExactlyOnce_AsSingleton()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FlexiBeeSettings:Server"] = "https://example.flexibee.eu",
                ["FlexiBeeSettings:Login"] = "test",
                ["FlexiBeeSettings:Password"] = "test",
                ["FlexiBeeSettings:Company"] = "test"
            })
            .Build();

        var services = new ServiceCollection();

        // Act
        services.AddFlexiAdapter(configuration);

        // Assert
        var lotsClientRegistrations = services
            .Where(d => d.ServiceType == typeof(ILotsClient))
            .ToList();

        lotsClientRegistrations.Should().ContainSingle(
            "ILotsClient must be registered exactly once; a duplicate registration " +
            "with a conflicting lifetime previously caused Singleton and Scoped " +
            "descriptors to coexist for the same service");

        var descriptor = lotsClientRegistrations.Single();
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
        descriptor.ImplementationType.Should().Be(typeof(FlexiLotsClient));
    }
}
