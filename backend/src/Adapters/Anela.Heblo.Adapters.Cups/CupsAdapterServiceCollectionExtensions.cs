using Anela.Heblo.Adapters.Cups.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharpIpp;

namespace Anela.Heblo.Adapters.Cups;

public static class CupsAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddCupsAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CupsOptions>(configuration.GetSection(CupsOptions.ConfigurationKey));

        // CupsAuthHandler adds the Basic Authorization header per-request at resolve-time,
        // avoiding mutation of shared DefaultRequestHeaders on the pooled HttpMessageHandler.
        services.AddTransient<CupsAuthHandler>();
        services.AddHttpClient("Cups")
            .AddHttpMessageHandler<CupsAuthHandler>();

        // Transient: each resolve gets a fresh HttpClient from the factory (disposal managed by factory)
        services.AddTransient<ISharpIppClient>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Cups");
            return new SharpIppClient(httpClient);
        });

        services.AddScoped<ICupsPrintingService, CupsPrintingService>();
        services.AddScoped<IPrintQueueSink, CupsPrintQueueSink>();

        return services;
    }
}
