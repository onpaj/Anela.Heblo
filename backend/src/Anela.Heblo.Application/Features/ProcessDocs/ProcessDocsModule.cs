using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ProcessDocs;

public static class ProcessDocsModule
{
    public static IServiceCollection AddProcessDocsModule(this IServiceCollection services)
    {
        services.AddSingleton<IProcessDocStore, EmbeddedProcessDocStore>();

        // MediatR handlers are automatically registered by AddMediatR scan
        return services;
    }
}
