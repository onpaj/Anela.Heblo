using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Anthropic;

public static class AnthropicAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddAnthropicAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AnthropicOptions>(opts =>
        {
            opts.ApiKey = configuration["Anthropic:ApiKey"] ?? "";
            opts.Model = configuration["KnowledgeBase:ChatModel"] ?? opts.Model;
            opts.MaxTokens = configuration.GetValue("KnowledgeBase:ChatMaxTokens", opts.MaxTokens);
        });
        services.AddHttpClient("Anthropic");

        services.AddChatClient(sp =>
            new AnthropicChatClient(
                sp.GetRequiredService<IOptions<AnthropicOptions>>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILogger<AnthropicChatClient>>()))
            .UseLogging()
            .Use(inner => new PostAnswerEnrichmentMiddleware(inner));

        return services;
    }
}
