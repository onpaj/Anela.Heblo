using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture;
using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Persistence;
using AutoMapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.GiftPackageManufacture;

public class GiftPackageManufactureModuleTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));

        services.AddSingleton(new Mock<IManufactureClient>().Object);
        services.AddSingleton(new Mock<ILogisticsCatalogSource>().Object);
        services.AddSingleton(new Mock<ILogisticsStockOperationService>().Object);
        services.AddSingleton(new Mock<IMapper>().Object);
        services.AddSingleton(TimeProvider.System);

        services.AddGiftPackageManufactureModule();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Resolving_BothInterfaces_FromSameScope_ReturnsSameInstance()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var manufactureService = scope.ServiceProvider.GetRequiredService<IGiftPackageManufactureService>();
        var queryService = scope.ServiceProvider.GetRequiredService<IGiftPackageQueryService>();

        queryService.Should().BeSameAs(manufactureService);
    }

    [Fact]
    public void Resolving_BothInterfaces_FromDifferentScopes_ReturnsDifferentInstances()
    {
        using var provider = BuildProvider();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var fromScopeA = scopeA.ServiceProvider.GetRequiredService<IGiftPackageManufactureService>();
        var fromScopeB = scopeB.ServiceProvider.GetRequiredService<IGiftPackageQueryService>();

        fromScopeB.Should().NotBeSameAs(fromScopeA);
    }
}
