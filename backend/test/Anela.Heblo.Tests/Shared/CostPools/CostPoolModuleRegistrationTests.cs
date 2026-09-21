using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Shared.CostPools;
using Anela.Heblo.Domain.Accounting.CostPools;
using Anela.Heblo.Domain.Accounting.Ledger;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Shared.CostPools;

public class CostPoolModuleRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddOptions<DataSourceOptions>();
        services.AddSingleton(new Mock<ILedgerService>().Object);

        services.AddSharedCostPoolsModule();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddSharedCostPoolsModule_ResolvesCostPoolService()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act
        var service = provider.GetService<ICostPoolService>();

        // Assert
        service.Should().BeOfType<CostPoolService>();
    }

    [Fact]
    public void AddSharedCostPoolsModule_ResolvesCostPoolCache()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act
        var cache = provider.GetService<ICostPoolCache>();

        // Assert
        cache.Should().BeOfType<CostPoolCache>();
    }
}
