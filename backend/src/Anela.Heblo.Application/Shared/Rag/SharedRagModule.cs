using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Shared.Rag.DocumentExtractors;
using Anela.Heblo.Application.Shared.Rag.OneDrive;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Domain.Shared;
using Anela.Heblo.Persistence.Rag;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Shared.Rag;

public static class SharedRagModule
{
    public static IServiceCollection AddSharedRagModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IWordWindowChunker, WordWindowChunker>();
        services.AddScoped<IRagQueryExpander, RagQueryExpander>();

        // Unified RAG interaction / eval-dataset logging, shared by KnowledgeBase + Smartsupp.
        services.AddScoped<IRagInteractionRecorder, RagInteractionRecorder>();
        services.AddScoped<IRagInteractionLogRepository, RagInteractionLogRepository>();

        services.AddScoped<IDocumentTextExtractor, PdfTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, WordDocumentExtractor>();
        services.AddScoped<IDocumentTextExtractor, PlainTextExtractor>();

        // OneDrive service — use real Graph service only when SharePoint drives are configured
        // AND real authentication is active. Mock auth has no Azure AD token so Graph calls
        // would fail; MockOneDriveService is used in those environments instead.
        // Moved verbatim from KnowledgeBaseModule — the Graph-vs-Mock check intentionally still
        // only inspects the "KnowledgeBase" configuration section (not "Leaflet"), matching
        // today's behavior exactly. This is a pre-existing latent gap (Leaflet's own
        // OneDriveFolderMappings are never consulted here), tracked separately — not fixed as
        // part of this refactor (NFR-1: zero behavioral change).
        var kbOptions = new KnowledgeBaseOptions();
        configuration.GetSection("KnowledgeBase").Bind(kbOptions);
        var sharePointConfigured = kbOptions.OneDriveFolderMappings.Any(m => !string.IsNullOrWhiteSpace(m.DriveId));
        var useMockAuth = configuration.GetValue<bool>("UseMockAuth", false);
        var bypassJwtValidation = configuration.GetValue<bool>(InfrastructureConfigurationKeys.BYPASS_JWT_VALIDATION, false);

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

        return services;
    }
}
