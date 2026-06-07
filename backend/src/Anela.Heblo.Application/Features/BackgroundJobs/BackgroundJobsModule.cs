using Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence.BackgroundJobs;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.BackgroundJobs;

public static class BackgroundJobsModule
{
    public static IServiceCollection AddBackgroundJobsModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by MediatR scan
        // Repository (implementation lives in the Persistence layer)
        services.AddScoped<IRecurringJobConfigurationRepository, RecurringJobConfigurationRepository>();
        // Hangfire adapter implementations (IHangfireJobEnqueuer, IHangfireRecurringJobScheduler)
        // are registered in Anela.Heblo.API.Extensions.ServiceCollectionExtensions.AddHangfireServices
        // because their implementations live in the API project (Clean Architecture dependency rule).

        // Register recurring job status checker
        services.AddScoped<IRecurringJobStatusChecker, RecurringJobStatusChecker>();

        // Register dashboard tiles
        services.RegisterTile<FailedJobsTile>();

        return services;
    }
}
