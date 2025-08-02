using Anela.Heblo.Domain.Features.Bank;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Comgate;

public static class ComgateAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddComgateAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient();

        // Configure ComgateSettings using Options pattern
        var comgateSection = configuration.GetSection(ComgateSettings.ConfigurationKey);
        services.Configure<ComgateSettings>(comgateSection);

        // Configure BankAccountSettings using Options pattern
        var bankAccountSection = configuration.GetSection(BankAccountSettings.ConfigurationKey);
        services.Configure<BankAccountSettings>(bankAccountSection);

        // Register IBankClient implementation
        services.AddTransient<IBankClient, ComgateBankClient>();

        return services;
    }
}
