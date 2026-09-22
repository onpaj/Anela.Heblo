using Anela.Heblo.Domain.Features.Ecomail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Ecomail;

public static class EcomailAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddEcomailAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EcomailOptions>()
            .Bind(configuration.GetSection(EcomailOptions.SectionName))
            .ValidateOnStart();
        // No AddValidatorsFromAssembly in this project — validators are registered by hand.
        services.AddSingleton<IValidateOptions<EcomailOptions>, EcomailOptionsValidator>();

        services.AddHttpClient(EcomailApiClient.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<EcomailOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
        });

        services.AddScoped<IEcomailApiClient, EcomailApiClient>();

        return services;
    }
}
