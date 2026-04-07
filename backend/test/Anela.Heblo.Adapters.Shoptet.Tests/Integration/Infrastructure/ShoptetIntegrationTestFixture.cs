using System.Reflection;
using Anela.Heblo.Adapters.Shoptet;
using Anela.Heblo.Adapters.ShoptetApi;
using Anela.Heblo.API.Extensions;
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Domain.Features.Catalog;
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

        services.AddShoptetPlaywrightAdapter(Configuration);
        services.AddShoptetApiAdapter(Configuration);
        services.AddCrossCuttingServices();
        services.AddHttpClient();

        services.Configure<PrintPickingListOptions>(opts =>
        {
            opts.PrintQueueFolder = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "test_prints");
        });
        services.AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>();
        services.AddSingleton(new Mock<ICatalogRepository>().Object);

        ServiceProvider = services.BuildServiceProvider();
    }
}