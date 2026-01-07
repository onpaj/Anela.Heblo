using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.BackgroundJobs;

public static class BackgroundJobsModule
{
    public static IServiceCollection AddBackgroundJobsModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by MediatR scan
        // Repository is registered in PersistenceModule

        // Register recurring job status checker
        services.AddScoped<IRecurringJobStatusChecker, RecurringJobStatusChecker>();

        // Register Hangfire job enqueuer
        services.AddScoped<IHangfireJobEnqueuer, HangfireJobEnqueuer>();

        return services;
    }
}
