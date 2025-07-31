using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rem.FlexiBeeSDK.Client.DI;

namespace Anela.Heblo.Adapters.Flexi.Tests.Integration;

public class FlexiIntegrationTestFixture : IDisposable
{
    public IServiceProvider ServiceProvider { get; }
    public IConfiguration Configuration { get; }
    
    /// <summary>
    /// Fixed reference date for deterministic testing (2025-06-01)
    /// </summary>
    public static DateTime ReferenceDate { get; } = new DateTime(2025, 6, 1);

    public FlexiIntegrationTestFixture()
    {
        var configBuilder = new ConfigurationBuilder()
            .AddUserSecrets<FlexiIntegrationTestFixture>()
            .AddEnvironmentVariables();

        Configuration = configBuilder.Build();

        var services = new ServiceCollection();

        // Add FlexiBee SDK
        services.AddFlexiAdapter(Configuration);

        // Add logging
        services.AddLogging();
        
        ServiceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}