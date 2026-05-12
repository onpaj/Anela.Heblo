using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Shared.Rag;

public static class SharedRagModule
{
    public static IServiceCollection AddSharedRagModule(this IServiceCollection services)
    {
        services.AddScoped<IWordWindowChunker, WordWindowChunker>();
        services.AddScoped<IRagQueryExpander, RagQueryExpander>();
        return services;
    }
}
