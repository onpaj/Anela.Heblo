using System.Reflection;
using Anela.Heblo.Application.Features.Bank.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank.Infrastructure.Jobs;

public sealed class BankImportJobDiscoveryTests
{
    [Fact]
    public void DiscoveryScan_DoesNotIncludeAbstractBase()
    {
        // Arrange: mirror the production filter from AddRecurringJobs() —
        //   t.IsClass && !t.IsAbstract && typeof(IRecurringJob).IsAssignableFrom(t)
        var applicationAssembly = Assembly.Load("Anela.Heblo.Application");

        // Act
        var discoveredJobTypes = applicationAssembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IRecurringJob).IsAssignableFrom(t))
            .ToList();

        // Assert
        discoveredJobTypes.Should().NotContain(typeof(BankImportJobBase));
    }

    [Fact]
    public void DiscoveryScan_IncludesAllThreeConcreteBankImportJobs()
    {
        // Arrange
        var applicationAssembly = Assembly.Load("Anela.Heblo.Application");

        // Act
        var discoveredJobTypes = applicationAssembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IRecurringJob).IsAssignableFrom(t))
            .ToList();

        // Assert
        discoveredJobTypes.Should().Contain(typeof(ComgateCzkImportJob));
        discoveredJobTypes.Should().Contain(typeof(ComgateEurImportJob));
        discoveredJobTypes.Should().Contain(typeof(ShoptetPayImportJob));
    }

    [Fact]
    public void BankImportJobBase_IsAbstract()
    {
        // Defence-in-depth — if a future edit drops `abstract`, the discovery
        // scan would suddenly register the base class. Catch it at the type
        // level here as well.
        typeof(BankImportJobBase).IsAbstract.Should().BeTrue();
    }
}
