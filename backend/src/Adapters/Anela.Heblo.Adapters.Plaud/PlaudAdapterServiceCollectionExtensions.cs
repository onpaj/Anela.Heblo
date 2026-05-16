using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Plaud;

public static class PlaudAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddPlaudAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PlaudOptions>(configuration.GetSection(PlaudOptions.SectionKey));
        services.AddSingleton<IPlaudClient, PlaudCliClient>();
        services.AddHostedService<PlaudTokenBootstrapper>();
        return services;
    }
}
