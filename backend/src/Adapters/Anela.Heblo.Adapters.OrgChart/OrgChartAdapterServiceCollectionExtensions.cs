using Anela.Heblo.Application.Features.OrgChart.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.OrgChart;

public static class OrgChartAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddOrgChartAdapter(
        this IServiceCollection services,
        IConfiguration configuration) // reserved for future base-URL configuration
    {
        services.AddHttpClient<IOrgChartService, OrgChartService>();
        return services;
    }
}
