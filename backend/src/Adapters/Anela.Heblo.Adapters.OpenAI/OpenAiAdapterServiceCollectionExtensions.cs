using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.OpenAI;

public static class OpenAiAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAiAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OpenAiEmbeddingOptions>(configuration.GetSection(OpenAiEmbeddingOptions.SectionKey));
        services.AddScoped<IEmbeddingService, OpenAiEmbeddingService>();

        return services;
    }
}
