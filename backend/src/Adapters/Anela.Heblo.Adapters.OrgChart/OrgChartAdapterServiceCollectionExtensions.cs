using Anela.Heblo.Application.Features.OrgChart.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.OrgChart;

public static class OrgChartAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddOrgChartAdapter(this IServiceCollection services)
    {
        services.AddHttpClient<IOrgChartService, OrgChartService>();
        return services;
    }
}
