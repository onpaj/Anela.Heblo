using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Persistence.DataQuality;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.DataQuality;

public static class DataQualityModule
{
    public static IServiceCollection AddDataQualityModule(this IServiceCollection services)
    {
        services.AddScoped<IDqtRunRepository, DqtRunRepository>();
        services.AddScoped<IInvoiceDqtComparer, InvoiceDqtComparer>();
        services.AddScoped<IInvoiceDqtJobRunner, InvoiceDqtJobRunner>();

        return services;
    }
}
