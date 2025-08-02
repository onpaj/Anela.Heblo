using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        // Add logging
        services.AddLogging(builder => builder.AddConsole());

        services.AddShoptetAdapter(Configuration);

        ServiceProvider = services.BuildServiceProvider();
    }
}