using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.OpenAI;

public static class OpenAiAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAiAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OpenAiEmbeddingOptions>(configuration.GetSection(OpenAiEmbeddingOptions.SectionKey));

        services.AddEmbeddingGenerator<string, Embedding<float>>(sp =>
            new OpenAiEmbeddingGenerator(
                sp.GetRequiredService<IOptions<OpenAiEmbeddingOptions>>(),
                sp.GetRequiredService<ILogger<OpenAiEmbeddingGenerator>>()))
            .UseLogging();

        return services;
    }
}
