using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;
using Microsoft.Identity.Web;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Domain.Shared;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Persistence.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.KnowledgeBase;

public static class KnowledgeBaseModule
{
    public static IServiceCollection AddKnowledgeBaseModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind options
        services.AddOptions<KnowledgeBaseOptions>()
            .Bind(configuration.GetSection(KnowledgeBaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register application services
        services.AddScoped<ChatTranscriptPreprocessor>();
        services.AddScoped<IChunkSummarizer, ChunkSummarizer>();
        services.AddScoped<IConversationTopicSummarizer, ConversationTopicSummarizer>();
        services.AddScoped<IIndexingStrategy, KnowledgeBaseDocIndexingStrategy>();
        services.AddScoped<IIndexingStrategy, ConversationIndexingStrategy>();
        services.AddScoped<IDocumentIndexingService, DocumentIndexingService>();

        // Cross-module contract: KnowledgeBase implements Leaflet's ILeafletKnowledgeSource via adapter.
        // DI registration owned by provider (KnowledgeBase), not consumer (Leaflet) — keeps the
        // dependency direction inverted properly.
        services.AddScoped<ILeafletKnowledgeSource, KnowledgeBaseLeafletSourceAdapter>();

        // Cross-module contract: KnowledgeBase implements Article's IArticleStyleGuideSource via adapter.
        // Same provider-owned-DI pattern as the Leaflet binding above.
        services.AddScoped<IArticleStyleGuideSource, KnowledgeBaseArticleStyleGuideSource>();

        // Cross-module contract: KnowledgeBase implements Article's IArticleKnowledgeSource via adapter.
        // Scoped to match existing Article contract bindings above.
        services.AddScoped<IArticleKnowledgeSource, KnowledgeBaseArticleKnowledgeSource>();

        // Repository (real EF Core implementation lives in the Persistence layer)
        services.AddScoped<IKnowledgeBaseRepository, KnowledgeBaseRepository>();

        services.AddSingleton<IProductEnrichmentCache, ProductEnrichmentCache>();

        // Register QuestionLoggingBehavior scoped to KB (not global like ValidationBehavior)
        services.AddScoped<IPipelineBehavior<AskQuestionRequest, AskQuestionResponse>, QuestionLoggingBehavior>();

        // MediatR handlers are automatically registered by AddMediatR scan

        return services;
    }
}
