using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Anthropic;

public static class AnthropicAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddAnthropicAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.SectionKey));
        services.AddHttpClient("Anthropic");
        services.AddScoped<IAnswerService, AnthropicClaudeService>();

        return services;
    }
}
