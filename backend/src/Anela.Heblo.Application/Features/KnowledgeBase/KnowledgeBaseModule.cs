using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.Services.DocumentExtractors;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Domain.Features.Configuration;
using MediatR;
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
        services.AddScoped<IDocumentTextExtractor, PdfTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, WordDocumentExtractor>();
        services.AddScoped<IDocumentTextExtractor, PlainTextExtractor>();
        services.AddScoped<DocumentChunker>();
        services.AddScoped<ChatTranscriptPreprocessor>();
        services.AddScoped<IChunkSummarizer, ChunkSummarizer>();
        services.AddScoped<IConversationTopicSummarizer, ConversationTopicSummarizer>();
        services.AddScoped<IIndexingStrategy, KnowledgeBaseDocIndexingStrategy>();
        services.AddScoped<IIndexingStrategy, ConversationIndexingStrategy>();
        services.AddScoped<IDocumentIndexingService, DocumentIndexingService>();

        // IKnowledgeBaseRepository is registered in PersistenceModule (real EF Core implementation)

        // OneDrive service — use mock in mock auth mode (no ITokenAcquisition available)
        var useMockAuth = configuration.GetValue<bool>(ConfigurationConstants.USE_MOCK_AUTH, defaultValue: false);
        var bypassJwtValidation = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, defaultValue: false);

        if (useMockAuth || bypassJwtValidation)
        {
            services.AddScoped<IOneDriveService, MockOneDriveService>();
        }
        else
        {
            services.AddHttpClient("MicrosoftGraph");
            services.AddScoped<IOneDriveService, GraphOneDriveService>();
        }

        services.AddSingleton<IProductEnrichmentCache, ProductEnrichmentCache>();

        // Register QuestionLoggingBehavior scoped to KB (not global like ValidationBehavior)
        services.AddScoped<IPipelineBehavior<AskQuestionRequest, AskQuestionResponse>, QuestionLoggingBehavior>();

        // MediatR handlers are automatically registered by AddMediatR scan

        return services;
    }
}
