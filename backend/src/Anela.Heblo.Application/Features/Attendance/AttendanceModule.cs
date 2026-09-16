using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Attendance.UseCases.RunBreakInsertion;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anela.Heblo.Application.Features.Attendance;

public static class AttendanceModule
{
    public static IServiceCollection AddAttendanceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<BreakInsertionOptions>()
            .Bind(configuration.GetSection(BreakInsertionOptions.ConfigKey));

        services.AddOptions<AbsenceHoursOptions>()
            .Bind(configuration.GetSection(AbsenceHoursOptions.ConfigKey));

        services.TryAddSingleton(TimeProvider.System);
        // Singleton: the gate's whole job is to be shared between the nightly job and on-demand runs.
        services.AddSingleton<Services.IBreakInsertionRunGate, Services.BreakInsertionRunGate>();
        services.AddScoped<Services.BreakInsertionService>();
        services.AddScoped<Services.AbsenceHoursService>();

        services.AddScoped<IValidator<RunBreakInsertionRequest>, RunBreakInsertionValidator>();
        services.AddScoped<IPipelineBehavior<RunBreakInsertionRequest, RunBreakInsertionResponse>,
            ValidationBehavior<RunBreakInsertionRequest, RunBreakInsertionResponse>>();

        // BreakInsertionJob and AbsenceHoursJob are auto-discovered via the IRecurringJob
        // assembly scan in AddRecurringJobs().

        return services;
    }
}
