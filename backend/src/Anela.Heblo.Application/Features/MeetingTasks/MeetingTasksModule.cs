using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.MeetingTasks;

public static class MeetingTasksModule
{
    public static IServiceCollection AddMeetingTasksModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IMeetingTaskExtractor, ClaudeMeetingTaskExtractor>();
        // PlaudPollingJob is auto-discovered via IRecurringJob assembly scan in AddRecurringJobs().
        // IMeetingTranscriptRepository is registered in PersistenceModule (subtask 1).
        // IngestPlaudRecordingHandler is auto-registered by MediatR assembly scan.
        return services;
    }
}
