using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks;

public static class MeetingTasksModule
{
    public static IServiceCollection AddMeetingTasksModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var optionsBuilder = services.AddOptions<MeetingTasksOptions>()
            .Bind(configuration.GetSection(MeetingTasksOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
        var bypassJwt = configuration.GetValue<bool>("BypassJwtValidation", false);

        if (!useMockAuth && !bypassJwt)
        {
            optionsBuilder.Validate(
                o => o.PlannerPlanId != "CONFIGURE_IN_USER_SECRETS",
                "MeetingTasks:PlannerPlanId is still set to the placeholder value. Set the real plan ID in user secrets.");
            // KnowledgeBaseModule only registers "MicrosoftGraph" when SharePoint is configured.
            // Re-register defensively here so GraphPlannerService always finds a client at runtime.
            // AddHttpClient with the same name is idempotent.
            services.AddHttpClient("MicrosoftGraph");
            services.AddScoped<IMeetingTaskExporter, GraphPlannerService>();
        }
        else
        {
            services.AddScoped<IMeetingTaskExporter, NoOpMeetingTaskExporter>();
        }
        services.AddScoped<IMeetingTaskExtractor>(sp =>
            new ClaudeMeetingTaskExtractor(
                sp.GetRequiredKeyedService<IChatClient>(MeetingTasksConstants.ExtractionChatClientKey),
                sp.GetRequiredService<IMeetingUserDirectory>(),
                sp.GetRequiredService<ILogger<ClaudeMeetingTaskExtractor>>()));
        services.AddScoped<IMeetingSummaryExplainer, ClaudeMeetingSummaryExplainer>();
        services.AddSingleton<IMeetingUserDirectory, MeetingUserDirectory>();
        services.AddScoped<IMeetingAccessGuard, MeetingAccessGuard>();

        // PlaudPollingJob is auto-discovered via IRecurringJob assembly scan in AddRecurringJobs().
        // IMeetingTranscriptRepository is registered in PersistenceModule (subtask 1).
        // MediatR handlers are auto-registered by the MediatR assembly scan in ApplicationModule.
        return services;
    }
}
