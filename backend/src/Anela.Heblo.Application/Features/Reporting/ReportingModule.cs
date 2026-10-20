using Anela.Heblo.Application.Features.Reporting.Data;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Anela.Heblo.Application.Features.Reporting;

public static class ReportingModule
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services)
    {
        // NpgsqlDataSource is registered by PersistenceModule except in in-memory mode.
        services.AddSingleton<IReportingDataReader>(sp => sp.GetService<NpgsqlDataSource>() is { } dataSource
            ? new NpgsqlReportingDataReader(dataSource)
            : new UnavailableReportingDataReader());

        // MediatR handlers are automatically registered by AddMediatR scan
        return services;
    }
}
