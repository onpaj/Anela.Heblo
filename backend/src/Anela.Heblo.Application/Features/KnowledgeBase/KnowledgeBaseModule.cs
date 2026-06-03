using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;
using Microsoft.Identity.Web;
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
        services.AddOptions<KnowledgeBaseOptions>()
            .Bind(configuration.GetSection(KnowledgeBaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register application services
        services.AddScoped<IDocumentTextExtractor, PdfTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, WordDocumentExtractor>();
        services.AddScoped<IDocumentTextExtractor, PlainTextExtractor>();
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

        // IKnowledgeBaseRepository is registered in PersistenceModule (real EF Core implementation)

        // OneDrive service — use real Graph service only when SharePoint drives are configured
        // AND real authentication is active. Mock auth has no Azure AD token so Graph calls
        // would fail; MockOneDriveService is used in those environments instead.
        var kbOptions = new KnowledgeBaseOptions();
        configuration.GetSection("KnowledgeBase").Bind(kbOptions);
        var sharePointConfigured = kbOptions.OneDriveFolderMappings.Any(m => !string.IsNullOrWhiteSpace(m.DriveId));
        var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
        var bypassJwtValidation = configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, false);

        if (sharePointConfigured && !useMockAuth && !bypassJwtValidation)
        {
            services.AddHttpClient("MicrosoftGraph");
            services.AddMemoryCache();
            services.AddScoped<IOneDriveService, GraphOneDriveService>();
        }
        else
        {
            services.AddScoped<IOneDriveService, MockOneDriveService>();
        }

        services.AddSingleton<IProductEnrichmentCache, ProductEnrichmentCache>();

        // Register QuestionLoggingBehavior scoped to KB (not global like ValidationBehavior)
        services.AddScoped<IPipelineBehavior<AskQuestionRequest, AskQuestionResponse>, QuestionLoggingBehavior>();

        // MediatR handlers are automatically registered by AddMediatR scan

        return services;
    }
}
