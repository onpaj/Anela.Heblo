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
        services.AddOptions<MeetingTasksOptions>()
            .Bind(configuration.GetSection(MeetingTasksOptions.SectionName))
            .ValidateOnStart();

        var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
        var bypassJwt = configuration.GetValue<bool>("BypassJwtValidation", false);

        if (!useMockAuth && !bypassJwt)
        {
            // KnowledgeBaseModule only registers "MicrosoftGraph" when SharePoint is configured.
            // Re-register defensively here so GraphTodoService always finds a client at runtime.
            // AddHttpClient with the same name is idempotent.
            services.AddHttpClient("MicrosoftGraph");
            services.AddScoped<IGraphTodoService, GraphTodoService>();
        }
        else
        {
            services.AddScoped<IGraphTodoService, NoOpGraphTodoService>();
        }
        services.AddScoped<IMeetingTaskExtractor, ClaudeMeetingTaskExtractor>();
        services.AddScoped<IMeetingSummaryExplainer, ClaudeMeetingSummaryExplainer>();
        services.AddSingleton<IMeetingUserDirectory, MeetingUserDirectory>();
        services.AddScoped<IMeetingAccessGuard, MeetingAccessGuard>();

        // PlaudPollingJob is auto-discovered via IRecurringJob assembly scan in AddRecurringJobs().
        // IMeetingTranscriptRepository is registered in PersistenceModule (subtask 1).
        // MediatR handlers are auto-registered by the MediatR assembly scan in ApplicationModule.
        return services;
    }
}
