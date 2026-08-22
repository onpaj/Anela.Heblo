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
            // Adapter-wide fallback only — every current call site passes its own per-feature
            // EmbeddingGenerationOptions (see RagFeatureOptions.ToEmbeddingOptions). Keys are
            // deliberately neutral (OpenAI:*, like OpenAI:ApiKey above) rather than named after
            // any one feature. Absent from appsettings*.json, so the class defaults apply.
            opts.EmbeddingModel = configuration["OpenAI:EmbeddingModel"] ?? opts.EmbeddingModel;
            opts.EmbeddingDimensions = configuration.GetValue("OpenAI:EmbeddingDimensions", opts.EmbeddingDimensions);
        });

        services.AddEmbeddingGenerator<string, Embedding<float>>(sp =>
            new OpenAiEmbeddingGenerator(
                sp.GetRequiredService<IOptions<OpenAiEmbeddingOptions>>(),
                sp.GetRequiredService<ILogger<OpenAiEmbeddingGenerator>>()))
            .UseLogging();

        return services;
    }
}
