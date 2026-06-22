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
        // Register configuration options with startup validation
        services
            .AddOptions<OrgChartOptions>()
            .Bind(configuration.GetSection(OrgChartOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // MediatR handlers are automatically registered by AddMediatR scan

        return services;
    }
}
