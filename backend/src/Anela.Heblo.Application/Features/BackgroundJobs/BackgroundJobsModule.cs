using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.BackgroundJobs;

public static class BackgroundJobsModule
{
    public static IServiceCollection AddBackgroundJobsModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by MediatR scan
        // Repository is registered in PersistenceModule
        // Hangfire adapter implementations (IHangfireJobEnqueuer, IHangfireRecurringJobScheduler)
        // are registered in Anela.Heblo.API.Extensions.ServiceCollectionExtensions.AddHangfireServices
        // because their implementations live in the API project (Clean Architecture dependency rule).

        // Register recurring job status checker
        services.AddScoped<IRecurringJobStatusChecker, RecurringJobStatusChecker>();

        return services;
    }
}
