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
        services.Configure<OpenAiEmbeddingOptions>(opts =>
        {
            opts.ApiKey = configuration["OpenAI:ApiKey"] ?? "";
            opts.EmbeddingModel = configuration["KnowledgeBase:EmbeddingModel"] ?? opts.EmbeddingModel;
            opts.EmbeddingDimensions = configuration.GetValue("KnowledgeBase:EmbeddingDimensions", opts.EmbeddingDimensions);
        });

        services.AddEmbeddingGenerator<string, Embedding<float>>(sp =>
            new OpenAiEmbeddingGenerator(
                sp.GetRequiredService<IOptions<OpenAiEmbeddingOptions>>(),
                sp.GetRequiredService<ILogger<OpenAiEmbeddingGenerator>>()))
            .UseLogging();

        return services;
    }
}
