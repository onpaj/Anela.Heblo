using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.KnowledgeBase;

public static class KnowledgeBaseModule
{
    public static IServiceCollection AddKnowledgeBaseModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind options
        services.Configure<KnowledgeBaseOptions>(configuration.GetSection("KnowledgeBase"));

        // Register application services
        services.AddScoped<IEmbeddingService, OpenAiEmbeddingService>();
        services.AddScoped<IDocumentTextExtractor, PdfTextExtractor>();
        services.AddScoped<IClaudeService, AnthropicClaudeService>();
        services.AddScoped<DocumentChunker>();

        // IKnowledgeBaseRepository is registered in PersistenceModule (real EF Core implementation)

        // MediatR handlers are automatically registered by AddMediatR scan

        return services;
    }
}
