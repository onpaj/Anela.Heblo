using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Infrastructure;
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Anela.Heblo.Tests.Application.ShipmentLabels;

public class ShipmentLabelsModuleTests
{
    [Fact]
    public void AddShipmentLabelsModule_RegistersIShipmentDeliveryChecker_AsShipmentLabelsShipmentDeliveryCheckerAdapter()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IShipmentClient>());
        var configuration = new ConfigurationBuilder().Build();

        services.AddShipmentLabelsModule(configuration);
        var serviceProvider = services.BuildServiceProvider();

        var checker = serviceProvider.GetRequiredService<IShipmentDeliveryChecker>();
        checker.Should().BeOfType<ShipmentLabelsShipmentDeliveryCheckerAdapter>();
    }
}
