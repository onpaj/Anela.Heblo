using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.Services.DocumentExtractors;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
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

        // OneDrive service — use real Graph service when SharePoint drives are configured and auth is available
        var kbOptions = new KnowledgeBaseOptions();
        configuration.GetSection("KnowledgeBase").Bind(kbOptions);
        var sharePointConfigured = kbOptions.OneDriveFolderMappings.Any(m => !string.IsNullOrWhiteSpace(m.DriveId));

        if (sharePointConfigured)
        {
            services.AddHttpClient("MicrosoftGraph");
            services.AddScoped<IOneDriveService, GraphOneDriveService>();
        }
        else
        {
            services.AddScoped<IOneDriveService, MockOneDriveService>();
        }

        // Register QuestionLoggingBehavior scoped to KB (not global like ValidationBehavior)
        services.AddScoped<IPipelineBehavior<AskQuestionRequest, AskQuestionResponse>, QuestionLoggingBehavior>();

        // MediatR handlers are automatically registered by AddMediatR scan

        return services;
    }
}
