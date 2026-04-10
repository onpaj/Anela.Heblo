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
            opts.HttpTimeoutSeconds = configuration.GetValue("Anthropic:HttpTimeoutSeconds", opts.HttpTimeoutSeconds);
        });

        services.AddHttpClient("Anthropic", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
        });

        services.AddChatClient(sp =>
            new AnthropicChatClient(
                sp.GetRequiredService<IOptions<AnthropicOptions>>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILogger<AnthropicChatClient>>()))
            .UseLogging()
            .Use((inner, sp) => new PostAnswerEnrichmentMiddleware(inner, sp.GetRequiredService<IProductEnrichmentCache>()));

        return services;
    }
}
