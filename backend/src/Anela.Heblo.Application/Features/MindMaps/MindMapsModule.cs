using Anela.Heblo.Application.Features.MindMaps.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Domain.Features.MindMaps;
using Anela.Heblo.Persistence.MindMaps;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MindMaps;

public static class MindMapsModule
{
    public static IServiceCollection AddMindMapsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MindMapsOptions>()
            .Bind(configuration.GetSection(MindMapsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var useStubUpdater = configuration.GetValue<bool>(
            $"{MindMapsOptions.SectionName}:UseStubUpdater", false);
        if (useStubUpdater)
        {
            services.AddScoped<IMindMapUpdater, StubMindMapUpdater>();
        }
        else
        {
            services.AddScoped<IMindMapUpdater>(sp =>
                new ClaudeMindMapUpdater(
                    sp.GetRequiredKeyedService<IChatClient>(MindMapsConstants.UpdaterChatClientKey),
                    sp.GetRequiredService<IOptions<MindMapsOptions>>(),
                    sp.GetRequiredService<ILogger<ClaudeMindMapUpdater>>()));
        }

        services.AddSingleton<MindMapGuard>();
        services.AddSingleton<MindMapLockService>();

        // On-demand Hangfire job (enqueued by attach/regenerate handlers)
        services.AddScoped<MindMapUpdateJob>();

        // Repository (implementation lives in the Persistence layer)
        services.AddScoped<IMindMapRepository, MindMapRepository>();

        // MediatR handlers are auto-registered by the MediatR assembly scan in ApplicationModule.
        return services;
    }
}
