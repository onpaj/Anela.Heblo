using System.Reflection;
using Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;
using Anela.Heblo.Adapters.Shoptet;
using Anela.Heblo.Adapters.ShoptetApi;
using Anela.Heblo.API.Extensions;
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;

public class ShoptetIntegrationTestFixture
{
    public IServiceProvider ServiceProvider { get; private set; }
    public IConfiguration Configuration { get; private set; }

    public ShoptetIntegrationTestFixture()
    {
        var configBuilder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets<ShoptetIntegrationTestFixture>()
            .AddEnvironmentVariables();

        Configuration = configBuilder.Build();

        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddConsole());

        services.AddShoptetApiAdapter(Configuration);
        services.AddShoptetCsvAdapter(Configuration);
        services.AddCrossCuttingServices();
        services.AddHttpClient();

        services.Configure<PrintPickingListOptions>(opts =>
        {
            opts.PrintQueueFolder = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "test_prints");
        });
        services.AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>();
        services.AddSingleton(new Mock<ICatalogRepository>().Object);

        var carrierCoolingMock = new Mock<ICarrierCoolingRepository>();
        carrierCoolingMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarrierCoolingSetting>());
        services.AddSingleton(carrierCoolingMock.Object);

        var giftSettingMock = new Mock<IGiftSettingRepository>();
        giftSettingMock
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GiftSetting.CreateDefault());
        services.AddSingleton(giftSettingMock.Object);

        ServiceProvider = services.BuildServiceProvider();
    }
}