using Anela.Heblo.Application.Features.OrgChart.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.OrgChart;

/// <summary>
/// Module for registering OrgChart feature services
/// </summary>
public static class OrgChartModule
{
    /// <summary>
    /// Registers OrgChart feature services
    /// </summary>
    public static IServiceCollection AddOrgChartServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register configuration options
        services.Configure<OrgChartOptions>(configuration.GetSection(OrgChartOptions.SectionName));

        // Register HTTP client for fetching organization data
        services.AddHttpClient<IOrgChartService, OrgChartService>();

        return services;
    }
}
