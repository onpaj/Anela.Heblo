using Anela.Heblo.Application.Shared.WebSearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.WebSearch;

public static class WebSearchAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddWebSearchAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WebSearchAdapterOptions>(configuration.GetSection("WebSearch"));

        services.AddHttpClient("SerpApi", (sp, client) =>
        {
            var timeout = configuration.GetValue("WebSearch:TimeoutSeconds", 15);
            client.Timeout = TimeSpan.FromSeconds(timeout);
        });

        var provider = configuration["WebSearch:Provider"] ?? "Mock";

        if (provider.Equals("SerpApi", StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IWebSearchClient, SerpApiWebSearchClient>();
        else
            services.AddScoped<IWebSearchClient, MockWebSearchClient>();

        return services;
    }
}
