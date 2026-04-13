using Anela.Heblo.Domain.Features.Bank;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Comgate;

public static class ComgateAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddComgateAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure ComgateSettings using Options pattern
        var comgateSection = configuration.GetSection(ComgateSettings.ConfigurationKey);
        services.Configure<ComgateSettings>(comgateSection);

        // Register typed HttpClient for ComgateBankClient
        services.AddHttpClient<ComgateBankClient>();

        // Register IBankClient implementation
        services.AddTransient<IBankClient>(sp => sp.GetRequiredService<ComgateBankClient>());

        return services;
    }
}
