using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Xcc.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Smartsupp;

public static class SmartsuppAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddSmartsuppAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SmartsuppOptions>(configuration.GetSection(SmartsuppOptions.SectionKey));

        services.AddHttpClient("Smartsupp", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<SmartsuppOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
        }).WithHebloOutboundDefaults();

        services.AddScoped<ISmartsuppApiClient, SmartsuppApiClient>();

        return services;
    }
}
